namespace OmniMixer.Audio;

/// <summary>
/// 레벨 미터 이벤트 데이터 페이로드.
/// DSP 처리 후 약 30FPS 주기로 ChannelDspProvider가 발생시키며,
/// UI 스레드에서 구독하여 Level Meter 프로그레스바를 업데이트한다.
/// </summary>
public sealed class MeteringEventArgs : EventArgs
{
    /// <summary>이 미터 데이터가 속한 채널 인덱스 (0 ~ 7)</summary>
    public int ChannelIndex { get; }

    /// <summary>좌채널 Peak 값 (0.0 ~ 1.0+ 클리핑 가능)</summary>
    public float PeakLeft { get; }

    /// <summary>우채널 Peak 값 (0.0 ~ 1.0+ 클리핑 가능)</summary>
    public float PeakRight { get; }

    /// <summary>
    /// 좌채널 RMS 값 (0.0 ~ 1.0).
    /// RMS = sqrt(sum(x^2) / N) — 체감 음량에 더 가까운 지표.
    /// </summary>
    public float RmsLeft { get; }

    /// <summary>우채널 RMS 값 (0.0 ~ 1.0)</summary>
    public float RmsRight { get; }

    public MeteringEventArgs(
        int channelIndex,
        float peakLeft, float peakRight,
        float rmsLeft, float rmsRight)
    {
        ChannelIndex = channelIndex;
        PeakLeft = peakLeft;
        PeakRight = peakRight;
        RmsLeft = rmsLeft;
        RmsRight = rmsRight;
    }
}
