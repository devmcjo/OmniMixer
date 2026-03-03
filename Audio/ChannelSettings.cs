namespace OmniMixer.Audio;

/// <summary>
/// 단일 출력 채널의 설정값 POCO (Plain Old C# Object).
/// UI의 ViewModel에서 AudioEngine.Start()를 호출할 때 이 객체 배열을 전달하며,
/// 나중에는 JSON 직렬화를 통해 앱 설정으로 저장/복원된다 (Step 5).
/// </summary>
public sealed class ChannelSettings
{
    /// <summary>채널 인덱스 (0 ~ 7)</summary>
    public int ChannelIndex { get; set; }

    /// <summary>
    /// 선택된 출력 장치의 MMDevice ID.
    /// null이면 해당 채널은 비활성화(Skip)된다.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// 볼륨 페이더 값 (데시벨, dB).
    /// 범위: -80.0 (묵음에 가까움) ~ +6.0 (최대 게인).
    /// 기본값: 0.0 dB (유니티 게인, 원음 그대로)
    /// </summary>
    public float VolumeDb { get; set; } = 0.0f;

    /// <summary>
    /// 팬 (좌우 밸런스).
    /// 범위: -1.0 (완전 좌) ~ 0.0 (중앙) ~ +1.0 (완전 우).
    /// </summary>
    public float Pan { get; set; } = 0.0f;

    /// <summary>음소거 여부. true이면 해당 채널 신호를 0으로 만든다.</summary>
    public bool IsMuted { get; set; } = false;
}
