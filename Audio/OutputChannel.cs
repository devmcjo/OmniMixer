using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OmniMixer.Audio;

/// <summary>
/// 단일 물리적 출력 장치(스피커)를 담당하는 채널 클래스.
///
/// 소유 구조:
///   BufferedWaveProvider → ChannelDspProvider(DSP) → [Resampler(옵션)] → WasapiOut
///
/// 책임:
///  - AudioEngine으로부터 전달받은 BufferedWaveProvider를 소유
///  - Volume/Pan/Mute DSP를 수행하는 ChannelDspProvider 연결
///  - 입출력 포맷이 다를 경우 MediaFoundationResampler로 변환
///  - Hot-unplug 등 장치 오류 시 이 채널만 안전하게 중지 (격리성)
///  - 다른 채널의 WasapiOut에는 절대 영향을 주지 않음
/// </summary>
public sealed class OutputChannel : IDisposable
{
    // ─────────────────────────────────────────────────────────
    //  내부 컴포넌트
    // ─────────────────────────────────────────────────────────

    private BufferedWaveProvider? _buffer;
    private ChannelDspProvider? _dspProvider;
    private IWaveProvider? _finalSource;       // WasapiOut에 공급되는 최종 소스 (DSP 또는 Resampler)
    private WasapiOut? _wasapiOut;
    private bool _disposed;

    // ─────────────────────────────────────────────────────────
    //  공개 프로퍼티
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 이 채널이 단독으로 보유하는 데이터 버퍼.
    /// AudioEngine의 캡처 스레드만 AddSamples()를 호출하고,
    /// WasapiOut의 출력 스레드만 Read()를 호출한다 → 락 경합 없음.
    /// </summary>
    public BufferedWaveProvider? Buffer => _buffer;

    /// <summary>DSP 파라미터(볼륨/팬/뮤트) 조절용 프로바이더. ViewModel이 직접 프로퍼티 변경.</summary>
    public ChannelDspProvider? DspProvider => _dspProvider;

    /// <summary>채널 인덱스 (0 ~ 7)</summary>
    public int ChannelIndex { get; }

    /// <summary>현재 이 채널이 활성화(재생 중) 상태인지 여부</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 장치 오류 또는 Hot-unplug 발생 시 발생.
    /// AudioEngine이 구독하여 UI에 채널 오류 상태를 알린다.
    /// 이 이벤트 발생 후에도 다른 채널은 정상 동작한다.
    /// </summary>
    public event EventHandler<string>? ChannelError;

    // ─────────────────────────────────────────────────────────
    //  생성자
    // ─────────────────────────────────────────────────────────

    public OutputChannel(int channelIndex)
    {
        ChannelIndex = channelIndex;
    }

    // ─────────────────────────────────────────────────────────
    //  Initialize: 채널 초기화 (Start 전에 호출)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 채널을 초기화한다. WasapiOut, DSP Provider, 버퍼를 모두 세팅한다.
    /// </summary>
    /// <param name="device">출력할 물리적 MMDevice (스피커 등)</param>
    /// <param name="captureFloatFormat">
    ///     캡처 스트림의 IEEE Float 포맷.
    ///     BufferedWaveProvider와 ChannelDspProvider가 이 포맷을 사용한다.
    /// </param>
    /// <param name="settings">이 채널의 초기 DSP 설정값</param>
    public void Initialize(MMDevice device, WaveFormat captureFloatFormat, ChannelSettings settings)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OutputChannel));

        // ── 1. BufferedWaveProvider 생성 ──────────────────────────────────
        // 이 채널만의 독립 버퍼. IEEE Float 포맷으로 고정.
        // BufferDuration = 200ms: 클럭 드리프트에 대한 여유 공간 확보.
        // DiscardOnBufferOverflow = true: Overflow 시 오래된 데이터를 조용히 드롭(Tick 방지 조치).
        _buffer = new BufferedWaveProvider(captureFloatFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(200),
            DiscardOnBufferOverflow = true  // Overflow → 오래된 데이터 자동 드롭 (Tick 최소화)
        };

        // ── 2. ChannelDspProvider 생성 ────────────────────────────────────
        // 버퍼로부터 float 샘플을 읽어 Volume/Pan/Mute를 적용하는 ISampleProvider
        _dspProvider = new ChannelDspProvider(ChannelIndex, _buffer)
        {
            VolumeDb = settings.VolumeDb,
            Pan = settings.Pan,
            IsMuted = settings.IsMuted
        };

        // ── 3. WasapiOut 생성 ────────────────────────────────────────────
        // AudioClientShareMode.Shared: 다른 앱과 장치를 공유 (WASAPI 공유 모드)
        // latency = 80ms: 끊김 방지용 넉넉한 버퍼 (PRD NFR-01 참조)
        _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared,
            useEventSync: true, latency: 80);

        // ── 4. 리샘플링 필요 여부 판단 ───────────────────────────────────
        // WasapiOut 공유 모드에서 장치 믹스 포맷을 얻어 캡처 포맷과 비교
        var deviceMixFormat = device.AudioClient.MixFormat;

        bool needsResampling =
            deviceMixFormat.SampleRate    != captureFloatFormat.SampleRate ||
            deviceMixFormat.Channels      != captureFloatFormat.Channels   ||
            deviceMixFormat.BitsPerSample != captureFloatFormat.BitsPerSample ||
            deviceMixFormat.Encoding      != captureFloatFormat.Encoding;

        if (needsResampling)
        {
            // ── 4a. 리샘플링 필요: MediaFoundationResampler 삽입 ─────────
            // ISampleProvider → IWaveProvider 변환 후 리샘플러에 연결
            var waveProviderSource = new SampleToWaveProvider(_dspProvider);
            var resampler = new MediaFoundationResampler(waveProviderSource, deviceMixFormat)
            {
                // Quality 35: 품질과 CPU 부하의 균형점 (60=최고품질, 1=최저품질)
                // Step 4 통합 테스트에서 CPU 측정 후 최적값으로 튜닝 예정
                ResamplerQuality = 35
            };
            _finalSource = resampler;
        }
        else
        {
            // ── 4b. 리샘플링 불필요: DSP → WasapiOut 직결 ───────────────
            // ISampleProvider를 IWaveProvider로 래핑
            _finalSource = new SampleToWaveProvider(_dspProvider);
        }

        // ── 5. PlaybackStopped 구독 (Init 이전에 등록) ─────────────────
        // === P1 FIX #5: 이벤트를 Init() 전에 등록하여 Race Condition 방지
        // Init() 호출 즉시 WASAPI 스레드가 시작될 수 있으므로, 미리 등록해야 함
        _wasapiOut.PlaybackStopped += OnPlaybackStopped;

        // ── 6. WasapiOut에 소스 연결 ─────────────────────────────────────
        _wasapiOut.Init(_finalSource);
    }

    // ─────────────────────────────────────────────────────────
    //  Start / Stop
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// WasapiOut 재생을 시작한다.
    /// Initialize() 이후에 호출해야 한다.
    /// </summary>
    public void Start()
    {
        if (_wasapiOut is null || IsActive) return;

        try
        {
            _wasapiOut.Play();
            IsActive = true;
        }
        catch (Exception ex)
        {
            // 재생 시작 실패 — 이 채널만 오류 처리, 다른 채널에 영향 없음
            IsActive = false;
            ChannelError?.Invoke(this, $"채널 {ChannelIndex} 시작 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// WasapiOut 재생을 중지하고 리소스를 정리한다.
    /// 다른 채널의 상태에 영향을 주지 않는다.
    /// </summary>
    public void Stop()
    {
        IsActive = false;
        SafeStopAndDispose();
    }

    // ─────────────────────────────────────────────────────────
    //  Hot-unplug / 장치 오류 처리 (격리 핵심 로직)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// WasapiOut.PlaybackStopped 이벤트 핸들러.
    /// 정상 Stop()이 아닌 예외(장치 해제 등)로 멈춘 경우에만 ChannelError를 발생시킨다.
    ///
    /// ※ 이 핸들러는 WasapiOut 내부 스레드에서 호출된다.
    ///    UI 업데이트가 필요하면 ChannelError 구독자가 Dispatcher.Invoke를 사용한다.
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // IsActive가 false이면 우리가 Stop()을 호출한 것 → 정상 종료, 무시
        if (!IsActive) return;

        // IsActive가 true인데 멈췄다 → 예기치 않은 중단 (Hot-unplug, 드라이버 오류 등)
        IsActive = false;

        string reason = e.Exception is not null
            ? $"장치 오류: {e.Exception.Message}"
            : "장치 연결 해제(Hot-unplug)로 인한 재생 중단";

        // 이 채널만 자원 해제 (다른 OutputChannel._wasapiOut은 독립적이므로 영향 없음)
        SafeStopAndDispose();

        // 상위(AudioEngine 또는 ViewModel)에 오류 알림
        ChannelError?.Invoke(this, $"채널 {ChannelIndex}: {reason}");
    }

    /// <summary>
    /// WasapiOut과 리샘플러를 안전하게 정리. 예외를 삼켜 다른 채널에 전파되지 않게 한다.
    /// </summary>
    private void SafeStopAndDispose()
    {
        try { _wasapiOut?.Stop(); } catch { /* 무시: 이미 중지된 경우 */ }

        // MediaFoundationResampler는 IDisposable
        if (_finalSource is IDisposable disposableFinal)
            try { disposableFinal.Dispose(); } catch { /* 무시 */ }

        try { _wasapiOut?.Dispose(); } catch { /* 무시 */ }

        _wasapiOut = null;
        _finalSource = null;

        // BufferedWaveProvider와 ChannelDspProvider는 관리형 객체, 별도 Dispose 불필요
        _buffer = null;
        _dspProvider = null;
    }

    // ─────────────────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IsActive = false;
        SafeStopAndDispose();
    }
}
