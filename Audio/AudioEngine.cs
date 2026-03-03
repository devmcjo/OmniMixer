using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OmniMixer.Audio;

/// <summary>
/// OmniMixer 오디오 엔진의 최상위 오케스트레이터.
///
/// 역할:
///  1. 캡처/출력 장치 열거
///  2. WasapiCapture로 VB-Cable에서 PCM 스트림 수신
///  3. 수신 데이터를 float으로 변환 후 8개 채널의 BufferedWaveProvider에 각각 복사
///     (1 Writer → 8 Independent Buffers 패턴)
///  4. 8개의 OutputChannel 생명주기 관리 (시작/중지/오류 격리)
///
/// 스레드 구조:
///  ┌─ [WasapiCapture 스레드] DataAvailable → _channels[i].Buffer.AddSamples()
///  ├─ [WasapiOut 스레드 #0] ChannelDspProvider.Read() ← Buffer[0]
///  ├─ [WasapiOut 스레드 #1] ChannelDspProvider.Read() ← Buffer[1]
///  │   ... (각 스레드는 자신의 버퍼만 접근 — 스레드 간 공유 없음)
///  └─ [UI 스레드] MeterUpdated 이벤트 구독 → ViewModel 업데이트
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // ─────────────────────────────────────────────────────────
    //  상수
    // ─────────────────────────────────────────────────────────

    /// <summary>지원하는 최대 출력 채널 수</summary>
    public const int MaxChannels = 8;

    // ─────────────────────────────────────────────────────────
    //  내부 컴포넌트
    // ─────────────────────────────────────────────────────────

    private WasapiCapture? _capture;

    /// <summary>
    /// 8개의 출력 채널 인스턴스.
    /// 각 채널은 독립된 BufferedWaveProvider, ChannelDspProvider, WasapiOut을 소유한다.
    /// </summary>
    private readonly OutputChannel[] _channels;

    /// <summary>
    /// 캡처된 데이터를 float으로 변환한 후 버퍼에 쓰기 위한 임시 float 배열.
    /// DataAvailable 스레드에서만 사용 → 스레드 안전.
    /// </summary>
    private float[] _convertBuffer = Array.Empty<float>();

    /// <summary>
    /// float[] → byte[] 변환 후 AddSamples로 전달하기 위한 임시 byte 배열.
    /// (BufferedWaveProvider.AddSamples는 byte[] 인터페이스)
    /// DataAvailable 스레드에서만 사용.
    /// </summary>
    private byte[] _addSamplesBuffer = Array.Empty<byte>();

    /// <summary>캡처 스트림의 IEEE Float 포맷 (변환 후 버퍼 포맷과 동일)</summary>
    private WaveFormat? _captureFloatFormat;

    private bool _disposed;

    // ─────────────────────────────────────────────────────────
    //  공개 프로퍼티
    // ─────────────────────────────────────────────────────────

    /// <summary>현재 오디오 엔진이 실행 중인지 여부</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 8개의 OutputChannel 배열에 대한 읽기 전용 접근자.
    /// ViewModel은 채널의 DspProvider에 접근하여 볼륨/팬/뮤트를 제어한다.
    /// </summary>
    public IReadOnlyList<OutputChannel> Channels => _channels;

    /// <summary>
    /// 오디오 엔진 수준의 오류 이벤트.
    /// 개별 채널 오류는 OutputChannel.ChannelError를 통해 상위로 전달된다.
    /// </summary>
    public event EventHandler<string>? EngineError;

    // ─────────────────────────────────────────────────────────
    //  생성자
    // ─────────────────────────────────────────────────────────

    public AudioEngine()
    {
        _channels = new OutputChannel[MaxChannels];
        for (int i = 0; i < MaxChannels; i++)
            _channels[i] = new OutputChannel(i);
    }

    // ─────────────────────────────────────────────────────────
    //  장치 열거
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 시스템의 캡처(녹음) 가능한 오디오 장치 목록을 반환한다.
    /// VB-Cable Output은 DataFlow.Capture 쪽에서 열거된다.
    /// UI의 Input Device ComboBox를 채울 때 사용.
    /// </summary>
    public static IReadOnlyList<MMDevice> GetCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .ToList();
        return devices.AsReadOnly();
    }

    /// <summary>
    /// 시스템의 출력(렌더) 오디오 장치 목록을 반환한다.
    /// 물리 스피커, HDMI 오디오, USB DAC 등이 여기에 포함된다.
    /// 각 채널의 Output Selector ComboBox를 채울 때 사용.
    /// </summary>
    public static IReadOnlyList<MMDevice> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .ToList();
        return devices.AsReadOnly();
    }

    // ─────────────────────────────────────────────────────────
    //  Start: 오디오 엔진 시작
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 오디오 엔진을 시작한다.
    /// 1) 각 채널 설정에 따라 OutputChannel 초기화 및 WasapiOut 시작
    /// 2) WasapiCapture 시작 → DataAvailable 이벤트로 데이터 수신 시작
    /// </summary>
    /// <param name="captureDevice">입력 장치 (VB-Cable Output 등)</param>
    /// <param name="channelSettings">
    ///     8개 채널 설정 배열. DeviceId가 null이면 해당 채널은 건너뜀.
    /// </param>
    /// <exception cref="InvalidOperationException">이미 실행 중일 때</exception>
    public void Start(MMDevice captureDevice, IEnumerable<ChannelSettings> channelSettings)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioEngine));
        if (IsRunning)
            throw new InvalidOperationException("AudioEngine is already running. Call Stop() first.");

        var settingsList = channelSettings.ToList();

        // ── 1. WasapiCapture 생성 ────────────────────────────────────────
        // WasapiCapture: 선택한 캡처 장치에서 PCM 스트림을 캡처한다.
        // (WasapiLoopbackCapture 대신 WasapiCapture를 사용하여 VB-Cable Output을
        //  명시적으로 지정. 루프백은 기본 출력 장치의 출력 신호를 캡처함.)
        _capture = new WasapiCapture(captureDevice)
        {
            // WasapiCapture의 기본 동작은 장치가 지원하는 포맷으로 캡처.
            // ShareMode = AudioClientShareMode.Shared (기본값 유지)
        };

        // 캡처 포맷 확인 (16-bit PCM, 24-bit PCM, 32-bit float 등)
        var captureNativeFormat = _capture.WaveFormat;

        // 내부 처리 포맷: 항상 IEEE Float 32-bit Stereo로 통일
        // 이렇게 하면 ChannelDspProvider가 항상 float 연산만 수행하면 됨
        _captureFloatFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            captureNativeFormat.SampleRate,
            captureNativeFormat.Channels);

        // ── 2. OutputChannel 초기화 ──────────────────────────────────────
        // DeviceId가 있는 채널만 초기화 (null이면 건너뜀)
        var outputDevices = GetOutputDevices();

        foreach (var settings in settingsList.Where(s => s.ChannelIndex < MaxChannels))
        {
            if (string.IsNullOrEmpty(settings.DeviceId))
                continue; // 장치 미선택 채널 건너뜀

            // DeviceId로 MMDevice 찾기
            var targetDevice = outputDevices.FirstOrDefault(d => d.ID == settings.DeviceId);
            if (targetDevice is null)
            {
                EngineError?.Invoke(this,
                    $"채널 {settings.ChannelIndex}: 장치 ID '{settings.DeviceId}'를 찾을 수 없음. 건너뜁니다.");
                continue;
            }

            try
            {
                var channel = _channels[settings.ChannelIndex];

                // ChannelError 이벤트 구독: Hot-unplug 등 오류를 AudioEngine이 중계
                channel.ChannelError += OnChannelError;

                // 채널 초기화 및 WasapiOut 연결
                channel.Initialize(targetDevice, _captureFloatFormat, settings);
                channel.Start();
            }
            catch (Exception ex)
            {
                // 이 채널의 초기화 실패 → 다른 채널 초기화는 계속 진행 (격리성)
                EngineError?.Invoke(this,
                    $"채널 {settings.ChannelIndex} 초기화 실패: {ex.Message}");
            }
        }

        // ── 3. DataAvailable 이벤트 구독 ────────────────────────────────
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnCaptureStopped;

        // ── 4. 캡처 시작 ─────────────────────────────────────────────────
        _capture.StartRecording();
        IsRunning = true;
    }

    // ─────────────────────────────────────────────────────────
    //  Stop: 오디오 엔진 중지
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 캡처와 모든 채널 출력을 정지하고 리소스를 해제한다.
    /// </summary>
    public void Stop()
    {
        IsRunning = false;

        // 캡처 중지
        try
        {
            _capture?.StopRecording();
        }
        catch { /* 무시 */ }

        // 모든 활성 채널 중지
        foreach (var channel in _channels)
        {
            try
            {
                channel.ChannelError -= OnChannelError;
                channel.Stop();
            }
            catch { /* 채널 정지 실패는 다른 채널 정지에 영향 없음 */ }
        }

        _capture?.Dispose();
        _capture = null;
    }

    // ─────────────────────────────────────────────────────────
    //  DataAvailable: 핵심 라우팅 로직 (캡처 스레드에서 실행)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// WasapiCapture가 새 오디오 데이터 청크를 받을 때마다 호출되는 이벤트 핸들러.
    ///
    /// [핵심 아키텍처]
    /// 1) 캡처 원본 포맷 → IEEE Float 32-bit 변환 (포맷 정규화)
    /// 2) 변환된 float 데이터를 byte 배열로 재해석
    /// 3) 활성화된 각 채널의 BufferedWaveProvider.AddSamples()에 복사
    ///    → 1 Writer, N Independent Buffers 패턴 (공유 메모리 없음)
    ///
    /// 이 메서드는 WasapiCapture 전용 스레드에서 실행되므로
    /// UI 스레드나 WasapiOut 스레드와 공유 상태를 갖지 않는다.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // ── float 변환 ───────────────────────────────────────────────────
        // 원본 포맷(16bit PCM 등)을 IEEE float으로 변환하여 _convertBuffer에 저장
        int floatSampleCount = ConvertToFloat(
            e.Buffer,
            e.BytesRecorded,
            _capture!.WaveFormat,
            ref _convertBuffer);

        if (floatSampleCount == 0) return;

        // ── byte 배열로 래핑 ─────────────────────────────────────────────
        // BufferedWaveProvider.AddSamples()는 byte[] 인터페이스이므로
        // float 배열을 byte 배열로 zero-copy 재해석한다.
        int byteCount = floatSampleCount * sizeof(float);

        if (_addSamplesBuffer.Length < byteCount)
            _addSamplesBuffer = new byte[byteCount];

        // MemoryMarshal: float[] → byte[] 변환 (메모리 복사 없음)
        MemoryMarshal.Cast<float, byte>(_convertBuffer.AsSpan(0, floatSampleCount))
            .CopyTo(_addSamplesBuffer.AsSpan(0, byteCount));

        // ── 각 채널 버퍼에 데이터 복사 (1 Writer → N Independent Buffers) ──
        // 이 루프가 전체 아키텍처의 핵심이다.
        // 각 채널의 BufferedWaveProvider는 독립적이며, 이 스레드만 AddSamples를 호출한다.
        // → 채널 간 공유 상태 없음, 데드락 위험 없음
        foreach (var channel in _channels)
        {
            // Buffer가 null이면 이 채널은 비활성(장치 미선택 또는 오류 이후)
            if (channel.Buffer is null || !channel.IsActive) continue;

            // AddSamples: 내부적으로 채널 자체의 lock만 사용
            // DiscardOnBufferOverflow = true이므로 가득 찼을 때 가장 오래된 데이터 드롭
            channel.Buffer.AddSamples(_addSamplesBuffer, 0, byteCount);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  이벤트 핸들러
    // ─────────────────────────────────────────────────────────

    /// <summary>캡처 장치가 예기치 않게 중단되었을 때 처리</summary>
    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (!IsRunning) return; // 우리가 Stop()을 호출한 경우 → 무시

        if (e.Exception is not null)
            EngineError?.Invoke(this, $"캡처 장치 오류로 중단: {e.Exception.Message}");
    }

    /// <summary>
    /// 개별 채널에서 발생한 오류를 AudioEngine 레벨로 중계한다.
    /// 특정 채널의 오류가 여기서 처리되며, 다른 채널은 계속 동작한다.
    /// </summary>
    private void OnChannelError(object? sender, string errorMessage)
    {
        // EngineError를 통해 상위(ViewModel)에 전달 — UI 알림용
        EngineError?.Invoke(this, errorMessage);
        // 오류가 난 채널은 OutputChannel.OnPlaybackStopped에서 이미 정리됨
        // 다른 채널의 WasapiOut은 독립적이므로 계속 동작
    }

    // ─────────────────────────────────────────────────────────
    //  포맷 변환 헬퍼 (정적)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 원본 포맷의 byte[] 오디오 데이터를 IEEE Float 32-bit float[]로 변환한다.
    ///
    /// 지원 포맷:
    ///  - IEEE Float 32-bit (WasapiCapture에서 가장 흔함): memcpy 수준의 처리
    ///  - PCM 16-bit: 각 샘플을 ÷ 32768f 변환
    ///  - PCM 24-bit: 3바이트 Little-Endian → signed int32 → ÷ 8388608f 변환
    ///  - PCM 32-bit int: 각 샘플을 ÷ 2147483648f 변환
    ///
    /// 지원되지 않는 포맷은 출력 버퍼를 0으로 채운다.
    /// </summary>
    /// <param name="rawBytes">캡처 원본 byte 배열</param>
    /// <param name="bytesRecorded">유효 바이트 수</param>
    /// <param name="sourceFormat">원본 WaveFormat</param>
    /// <param name="floatBuffer">결과 float 배열 (필요 시 내부에서 재할당)</param>
    /// <returns>float 배열에 기록된 샘플 수</returns>
    private static int ConvertToFloat(
        byte[] rawBytes,
        int bytesRecorded,
        WaveFormat sourceFormat,
        ref float[] floatBuffer)
    {
        int bytesPerSample = sourceFormat.BitsPerSample / 8;

        // 채널 수 불일치 등 비정상 데이터 방어
        if (bytesPerSample == 0 || bytesRecorded % bytesPerSample != 0)
            return 0;

        int sampleCount = bytesRecorded / bytesPerSample;

        // float 버퍼 재할당 (필요 시)
        if (floatBuffer.Length < sampleCount)
            floatBuffer = new float[sampleCount];

        if (sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            // ── IEEE Float 32-bit: 그대로 복사 (가장 일반적인 경우) ──────
            // MemoryMarshal로 byte → float 변환 (zero-copy)
            MemoryMarshal.Cast<byte, float>(rawBytes.AsSpan(0, bytesRecorded))
                .CopyTo(floatBuffer.AsSpan(0, sampleCount));
        }
        else if (sourceFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            switch (sourceFormat.BitsPerSample)
            {
                case 16:
                    // ── PCM 16-bit → float: ÷ 32768 ──────────────────────
                    // 범위: -32768 ~ +32767 → -1.0f ~ ~+0.9999f
                    var shorts = MemoryMarshal.Cast<byte, short>(
                        rawBytes.AsSpan(0, bytesRecorded));
                    for (int i = 0; i < sampleCount; i++)
                        floatBuffer[i] = shorts[i] / 32768f;
                    break;

                case 24:
                    // ── PCM 24-bit → float: 3바이트 Little-Endian ────────
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int offset = i * 3;
                        // 부호 있는 24-bit: 최상위 바이트를 int32 상위로 이동 후 산술 시프트
                        int sample24 = rawBytes[offset]
                                     | (rawBytes[offset + 1] << 8)
                                     | ((sbyte)rawBytes[offset + 2] << 16); // 부호 확장
                        floatBuffer[i] = sample24 / 8388608f; // 2^23
                    }
                    break;

                case 32:
                    // ── PCM 32-bit int → float: ÷ 2^31 ───────────────────
                    var ints = MemoryMarshal.Cast<byte, int>(
                        rawBytes.AsSpan(0, bytesRecorded));
                    for (int i = 0; i < sampleCount; i++)
                        floatBuffer[i] = ints[i] / 2147483648f;
                    break;

                default:
                    // 지원되지 않는 PCM 비트뎁스 → 무음
                    Array.Clear(floatBuffer, 0, sampleCount);
                    break;
            }
        }
        else
        {
            // 지원되지 않는 인코딩(예: ADPCM 등) → 무음
            Array.Clear(floatBuffer, 0, sampleCount);
        }

        return sampleCount;
    }

    // ─────────────────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        foreach (var channel in _channels)
            channel.Dispose();
    }
}
