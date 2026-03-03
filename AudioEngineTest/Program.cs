// Step 1 검증용 콘솔 테스트 앱
// AudioEngine이 UI 없이도 정상 동작하는지 확인

using OmniMixer.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

Console.WriteLine("========================================");
Console.WriteLine("  OmniMixer AudioEngine - Step 1 Test");
Console.WriteLine("========================================\n");

// ── 1. 장치 열거 테스트 ─────────────────────────────────────────
Console.WriteLine("[1/4] 장치 열거 테스트...");
try
{
    var captureDevices = AudioEngine.GetCaptureDevices();
    var outputDevices = AudioEngine.GetOutputDevices();

    Console.WriteLine($"  캡처 장치 수: {captureDevices.Count}");
    foreach (var dev in captureDevices.Take(5))
        Console.WriteLine($"    - {dev.FriendlyName} [{dev.ID.Substring(0, Math.Min(20, dev.ID.Length))}...]");

    Console.WriteLine($"  출력 장치 수: {outputDevices.Count}");
    foreach (var dev in outputDevices.Take(5))
        Console.WriteLine($"    - {dev.FriendlyName} [{dev.ID.Substring(0, Math.Min(20, dev.ID.Length))}...]");

    Console.WriteLine("  ✓ 장치 열거 성공\n");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 장치 열거 실패: {ex.Message}\n");
    return 1;
}

// ── 2. AudioEngine 생성 및 설정 ─────────────────────────────────
Console.WriteLine("[2/4] AudioEngine 초기화...");
AudioEngine? engine = null;
try
{
    engine = new AudioEngine();
    Console.WriteLine("  ✓ AudioEngine 생성 성공\n");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ AudioEngine 생성 실패: {ex.Message}\n");
    return 1;
}

// ── 3. 채널 설정 및 이벤트 구독 ─────────────────────────────────
Console.WriteLine("[3/4] 채널 설정 준비...");

// VB-Cable 찾기
var captureDevs = AudioEngine.GetCaptureDevices();
var cableDevice = captureDevs.FirstOrDefault(d =>
    d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
    d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));

if (cableDevice is null)
{
    Console.WriteLine("  ⚠ VB-Cable을 찾을 수 없습니다. 테스트를 위해 첫 번째 캡처 장치를 사용합니다.");
    cableDevice = captureDevs.FirstOrDefault();
}

if (cableDevice is null)
{
    Console.WriteLine("  ✗ 사용 가능한 캡처 장치가 없습니다.");
    return 1;
}

Console.WriteLine($"  선택된 입력 장치: {cableDevice.FriendlyName}");

// 출력 장치 설정 (첫 번째 출력 장치를 채널 0에 할당)
var outDevs = AudioEngine.GetOutputDevices();
var firstOutput = outDevs.FirstOrDefault();

var channelSettings = new List<ChannelSettings>();
for (int i = 0; i < AudioEngine.MaxChannels; i++)
{
    var settings = new ChannelSettings
    {
        ChannelIndex = i,
        DeviceId = i == 0 && firstOutput != null ? firstOutput.ID : null,
        VolumeDb = 0.0f,
        Pan = 0.0f,
        IsMuted = false
    };
    channelSettings.Add(settings);
}

if (firstOutput != null)
    Console.WriteLine($"  채널 0 출력 장치: {firstOutput.FriendlyName}");
else
    Console.WriteLine("  ⚠ 출력 장치 없음 (시각적 확인 불가)");

// 미터 이벤트 구독
int meterUpdateCount = 0;
foreach (var channel in engine.Channels)
{
    if (channel.DspProvider != null)
    {
        channel.DspProvider.MeterUpdated += (s, e) =>
        {
            meterUpdateCount++;
            if (meterUpdateCount % 30 == 0) // 약 1초마다
            {
                Debug.WriteLine($"[Meter] CH{e.ChannelIndex}: Peak L={e.PeakLeft:F3} R={e.PeakRight:F3}, RMS L={e.RmsLeft:F3} R={e.RmsRight:F3}");
            }
        };
    }
}

// 엔진 오류 이벤트 구독
engine.EngineError += (s, msg) =>
{
    Console.WriteLine($"  [Engine Error] {msg}");
};

Console.WriteLine("  ✓ 채널 설정 완료\n");

// ── 4. 오디오 시작 테스트 ───────────────────────────────────────
Console.WriteLine("[4/4] 오디오 엔진 시작 테스트 (5초 동안 실행)...");
Console.WriteLine("      (VB-Cable을 통해 오디오를 재생하면 채널 0에서 소리가 나와야 합니다)");

try
{
    engine.Start(cableDevice, channelSettings);
    Console.WriteLine($"  상태: IsRunning = {engine.IsRunning}");
    Console.WriteLine($"  활성 채널 수: {engine.Channels.Count(c => c.IsActive)}");

    // 5초 동안 대기
    for (int i = 5; i > 0; i--)
    {
        Console.Write($"\r  남은 시간: {i}초... (미터 이벤트: {meterUpdateCount}회)");
        await Task.Delay(1000);
    }
    Console.WriteLine();

    // 정지
    engine.Stop();
    Console.WriteLine("  ✓ 오디오 엔진 정지 완료\n");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ 오디오 시작 실패: {ex.Message}\n");
    engine?.Dispose();
    return 1;
}

// ── 정리 ────────────────────────────────────────────────────────
Console.WriteLine("[정리] AudioEngine Dispose...");
engine?.Dispose();
Console.WriteLine("  ✓ 정리 완료\n");

Console.WriteLine("========================================");
Console.WriteLine("  Step 1 테스트 완료!");
Console.WriteLine("========================================");

return 0;
