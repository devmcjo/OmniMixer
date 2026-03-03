using System.Runtime.InteropServices;
using NAudio.Wave;

namespace OmniMixer.Audio;

/// <summary>
/// DSP 처리 파이프라인의 핵심 구성 요소.
/// NAudio의 ISampleProvider를 구현하여 WasapiOut의 Pull 모델에서 직접 호출된다.
///
/// 처리 순서: BufferedWaveProvider(소스 버퍼) → Volume → Pan → Mute → Peak/RMS 미터링
///
/// ※ WasapiOut은 재생 스레드에서 Read()를 반복 호출하므로, 이 클래스는
///    항상 출력 스레드에서 실행된다. 볼륨/팬/뮤트 프로퍼티는 volatile 또는
///    Interlocked를 통해 안전하게 UI 스레드에서 업데이트된다.
/// </summary>
public sealed class ChannelDspProvider : ISampleProvider
{
    // ─────────────────────────────────────────────────────────
    //  상수
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 미터 업데이트 주기: 약 33ms = ~30FPS.
    /// 이 샘플 수에 도달하면 MeterUpdated 이벤트를 발생시킨다.
    /// </summary>
    private const double MeterIntervalMs = 33.3;

    /// <summary>
    /// 볼륨 최소값 (dB). 이 이하는 무음으로 처리하여 10^(-80/20) ≈ 0.0001의 연산 낭비를 막는다.
    /// </summary>
    private const float MinVolumeDb = -80.0f;

    // ─────────────────────────────────────────────────────────
    //  소스 버퍼
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 이 채널이 단독으로 소유하는 버퍼.
    /// AudioEngine의 캡처 스레드가 AddSamples()로 데이터를 밀어 넣고,
    /// WasapiOut의 출력 스레드가 Read()를 통해 이 프로바이더를 거쳐 데이터를 뽑아간다.
    /// </summary>
    private readonly BufferedWaveProvider _sourceBuffer;

    // ─────────────────────────────────────────────────────────
    //  DSP 파라미터 (UI 스레드에서 쓰기, 출력 스레드에서 읽기)
    // ─────────────────────────────────────────────────────────

    private volatile float _volumeDb = 0.0f;
    private volatile float _pan = 0.0f;
    private volatile bool _isMuted = false;

    /// <summary>볼륨 페이더 값 (dB). UI 슬라이더 변경 시 직접 설정.</summary>
    public float VolumeDb
    {
        get => _volumeDb;
        set => _volumeDb = Math.Clamp(value, MinVolumeDb, 6.0f);
    }

    /// <summary>팬 포지션 (-1.0 Left ~ 0.0 Center ~ +1.0 Right). UI 슬라이더 변경 시 설정.</summary>
    public float Pan
    {
        get => _pan;
        set => _pan = Math.Clamp(value, -1.0f, 1.0f);
    }

    /// <summary>음소거 토글. true이면 출력 신호를 0으로 만든다 (처리 자체는 계속됨).</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set => _isMuted = value;
    }

    // ─────────────────────────────────────────────────────────
    //  미터링
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 미터 업데이트 이벤트. DSP 처리 후 약 30FPS 주기로 발생.
    /// UI 스레드에서 구독하여 레벨 미터 프로그레스바를 업데이트한다.
    /// 이벤트 핸들러 내부에서 반드시 Dispatcher.Invoke를 사용할 것.
    /// </summary>
    public event EventHandler<MeteringEventArgs>? MeterUpdated;

    // 채널 인덱스 (MeteringEventArgs에 포함하기 위해 보관)
    private readonly int _channelIndex;

    // 미터 데이터 누적 변수 (출력 스레드에서만 접근)
    private float _meterPeakLeft;
    private float _meterPeakRight;
    private double _meterSumSqLeft;   // RMS 계산용 제곱합
    private double _meterSumSqRight;
    private int _meterSampleCount;    // 현재까지 누적된 스테레오 프레임 수
    private int _meterIntervalSamples; // 미터 업데이트 트리거 샘플 수 (sampleRate × interval)

    // ─────────────────────────────────────────────────────────
    //  임시 읽기 버퍼 (매 Read 호출마다 재할당을 피하기 위해 고정)
    // ─────────────────────────────────────────────────────────
    private byte[] _readByteBuffer = Array.Empty<byte>();

    // ─────────────────────────────────────────────────────────
    //  ISampleProvider 구현
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 이 프로바이더의 출력 포맷: IEEE Float 32-bit Stereo.
    /// 항상 float 스테레오로 동작하므로 Pan/Volume 연산이 단순해진다.
    /// </summary>
    public WaveFormat WaveFormat { get; }

    // ─────────────────────────────────────────────────────────
    //  생성자
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// ChannelDspProvider 생성자.
    /// </summary>
    /// <param name="channelIndex">이 채널의 인덱스 (0~7). 미터 이벤트에 포함된다.</param>
    /// <param name="sourceBuffer">
    ///     AudioEngine이 데이터를 밀어 넣는 BufferedWaveProvider.
    ///     반드시 IEEE Float Stereo 포맷이어야 한다.
    /// </param>
    public ChannelDspProvider(int channelIndex, BufferedWaveProvider sourceBuffer)
    {
        _channelIndex = channelIndex;
        _sourceBuffer = sourceBuffer;

        // 출력 포맷은 소스 버퍼와 동일한 IEEE Float Stereo
        WaveFormat = sourceBuffer.WaveFormat;

        // 미터 업데이트 간격을 샘플 수로 계산
        // 예) 48000 Hz × 0.0333 s ≈ 1600 프레임 (스테레오이므로 × 2 = 3200 샘플)
        _meterIntervalSamples = (int)(WaveFormat.SampleRate * (MeterIntervalMs / 1000.0));
    }

    // ─────────────────────────────────────────────────────────
    //  Read(): WasapiOut 출력 스레드에서 반복 호출됨
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// WasapiOut이 오디오 데이터를 요청할 때 호출하는 Pull 메서드.
    /// 1) BufferedWaveProvider에서 읽기
    /// 2) Volume 게인 적용 (dB → 선형)
    /// 3) Equal-Power Pan 적용
    /// 4) Mute 적용
    /// 5) Peak/RMS 누적 및 미터 이벤트 발생
    /// </summary>
    /// <param name="buffer">float[] 출력 버퍼 (L, R, L, R ... 인터리브 스테레오)</param>
    /// <param name="offset">buffer 내 시작 오프셋 (float 단위)</param>
    /// <param name="count">요청된 float 샘플 수 (프레임 수 × 2)</param>
    /// <returns>실제 채운 float 샘플 수</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        // === P0 FIX #3: DSP 파라미터 스냅샷 복사 ===
        // UI 스레드가 _volumeDb, _pan, _isMuted를 변경할 수 있으므로
        // Read() 시작 시점에 로컬 변수로 스냅샷을 복사하여 일관성 보장.
        float volDb = _volumeDb;
        float pan = _pan;
        bool muted = _isMuted;

        // ── 1. BufferedWaveProvider에서 바이트 읽기 ──────────────────────
        // BufferedWaveProvider.Read()는 byte[] 인터페이스이므로
        // float 1개 = 4바이트임을 고려하여 byte 버퍼를 준비한다.
        int byteCount = count * sizeof(float);

        // 재할당을 최소화하기 위해 기존 버퍼가 충분하면 재사용
        if (_readByteBuffer.Length < byteCount)
            _readByteBuffer = new byte[byteCount];

        int bytesRead = _sourceBuffer.Read(_readByteBuffer, 0, byteCount);
        int samplesRead = bytesRead / sizeof(float);

        if (samplesRead == 0)
        {
            // 버퍼에 데이터가 없는 경우(Underrun): 무음으로 채움
            // BufferedWaveProvider 자체도 0을 반환하지만 명시적으로 처리
            Array.Clear(buffer, offset, count);
            // === P0 FIX #1: 실제 읽은 샘플 수(0)를 반환 ===
            // return count; ← 버그: WASAPI가 데이터가 준비됐다고 오인
            return samplesRead; // 정확히 0을 반환하여 WASAPI가 재요청하도록 함
        }

        // byte[] → float[] 변환: MemoryMarshal로 zero-copy 재해석
        // _readByteBuffer의 앞 bytesRead 바이트를 float 스팬으로 해석
        var floatSpan = MemoryMarshal.Cast<byte, float>(
            _readByteBuffer.AsSpan(0, bytesRead));

        // float 스팬을 출력 버퍼의 offset 위치에 복사
        floatSpan.CopyTo(buffer.AsSpan(offset, samplesRead));

        // ── 2. DSP 게인 계산 (스냅샷 변수 사용) ───────────────────────────
        // Volume: dB → 선형 진폭 (Amplitude = 10^(dB/20))
        float volumeGain = (volDb <= MinVolumeDb)
            ? 0.0f
            : MathF.Pow(10.0f, volDb / 20.0f);

        // Mute 적용
        if (muted)
            volumeGain = 0.0f;

        // ── 3. 채널 수에 따른 처리 분기 ───────────────────────────────────
        // === P0 FIX #2: 스테레오 고정 가정 제거 ===
        int channels = WaveFormat.Channels;
        int frames = samplesRead / channels;

        if (channels == 2)
        {
            // 스테레오: Volume + Equal-Power Pan 적용
            // L_gain = cos(π/4 × (P + 1)), R_gain = sin(π/4 × (P + 1))
            float panAngle = (float)(Math.PI / 4.0 * (pan + 1.0f));
            float gainL = volumeGain * MathF.Cos(panAngle);
            float gainR = volumeGain * MathF.Sin(panAngle);

            for (int i = 0; i < frames; i++)
            {
                int idxL = offset + i * 2;
                int idxR = idxL + 1;

                float sampleL = buffer[idxL] * gainL;
                float sampleR = buffer[idxR] * gainR;

                buffer[idxL] = sampleL;
                buffer[idxR] = sampleR;

                // Peak/RMS 누적
                float absL = MathF.Abs(sampleL);
                float absR = MathF.Abs(sampleR);

                if (absL > _meterPeakLeft) _meterPeakLeft = absL;
                if (absR > _meterPeakRight) _meterPeakRight = absR;

                _meterSumSqLeft += sampleL * sampleL;
                _meterSumSqRight += sampleR * sampleR;
            }
        }
        else
        {
            // 모노 또는 다채널: Volume만 적용 (Pan 없음)
            for (int i = 0; i < samplesRead; i++)
            {
                buffer[offset + i] *= volumeGain;
            }

            // 미터링: 첫 번째 채널만 추적 (간략화)
            for (int i = 0; i < frames; i++)
            {
                float sample = buffer[offset + i * channels];
                float abs = MathF.Abs(sample);

                if (abs > _meterPeakLeft) _meterPeakLeft = abs;
                _meterSumSqLeft += sample * sample;
            }
            // 우측 채널 미터는 0으로 유지 (모노/다채널의 경우)
            _meterPeakRight = 0;
        }

        _meterSampleCount += frames;

        // ── 4. 미터 이벤트 발생 ─────────────────────────────────────────
        if (_meterSampleCount >= _meterIntervalSamples)
        {
            FireMeterUpdate(channels == 2);
        }

        return samplesRead;
    }

    // ─────────────────────────────────────────────────────────
    //  미터 이벤트 발생 (내부 헬퍼)
    // ─────────────────────────────────────────────────────────

    private void FireMeterUpdate(bool isStereo = true)
    {
        // RMS = sqrt(N분의 제곱합)
        float rmsL = _meterSampleCount > 0
            ? MathF.Sqrt((float)(_meterSumSqLeft  / _meterSampleCount))
            : 0.0f;
        float rmsR = isStereo && _meterSampleCount > 0
            ? MathF.Sqrt((float)(_meterSumSqRight / _meterSampleCount))
            : 0.0f;

        // 이벤트 발생: 구독자(ChannelViewModel)는 Dispatcher.Invoke로 UI 갱신
        MeterUpdated?.Invoke(this, new MeteringEventArgs(
            _channelIndex,
            _meterPeakLeft,
            isStereo ? _meterPeakRight : 0.0f,
            rmsL,
            rmsR));

        // 누적 값 초기화
        _meterPeakLeft  = 0.0f;
        _meterPeakRight = 0.0f;
        _meterSumSqLeft  = 0.0;
        _meterSumSqRight = 0.0;
        _meterSampleCount = 0;
    }
}
