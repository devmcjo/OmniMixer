using NAudio.CoreAudioApi;

namespace OmniMixer.Models;

/// <summary>
/// 오디오 장치 정보를 담는 모델 클래스.
/// MMDevice는 IDisposable이므로 UI에서는 이 래퍼 클래스만 사용한다.
/// </summary>
public sealed class AudioDeviceItem
{
    /// <summary>장치 고유 ID (MMDevice.ID와 동일)</summary>
    public string Id { get; }

    /// <summary>사용자에게 표시할 장치 이름</summary>
    public string FriendlyName { get; }

    /// <summary>캡처/렌더 구분</summary>
    public DataFlow DataFlow { get; }

    /// <summary>장치 상태</summary>
    public DeviceState State { get; }

    public AudioDeviceItem(string id, string friendlyName, DataFlow dataFlow, DeviceState state)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        FriendlyName = friendlyName ?? string.Empty;
        DataFlow = dataFlow;
        State = state;
    }

    /// <summary>
    /// MMDevice에서 AudioDeviceItem으로 변환 (팩토리 메서드)
    ///
    /// P4 Fix: MMDevice는 COM 기반 IDisposable 객체이므로,
    /// 이 메서드는 MMDevice의 값을 즉시 복사하여 새로운 AudioDeviceItem을 생성한다.
    /// 반환된 AudioDeviceItem은 MMDevice의 수명에 의존하지 않으며,
    /// 원본 MMDevice가 Dispose되어도 안전하게 사용할 수 있다.
    /// </summary>
    /// <param name="device">변환할 MMDevice (null 불가)</param>
    /// <returns>값이 복사된 AudioDeviceItem</returns>
    /// <exception cref="ArgumentNullException">device가 null일 경우</exception>
    public static AudioDeviceItem FromMMDevice(MMDevice device)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));

        return new AudioDeviceItem(
            device.ID,
            device.FriendlyName,
            device.DataFlow,
            device.State);
    }

    /// <summary>
    /// 디버깅용 ToString 재정의
    /// </summary>
    public override string ToString() => FriendlyName;

    /// <summary>
    /// ComboBox 등에서 동일 장치 판별을 위한 Equals 재정의
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is AudioDeviceItem other)
            return Id == other.Id;
        return false;
    }

    public override int GetHashCode() => Id.GetHashCode();
}
