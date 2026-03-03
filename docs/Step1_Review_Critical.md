# OmniMixer Step 1 - 설계 검토 보고서 (Critical Issues)

> **Date**: 2026-03-03 | **Status**: CRITICAL REVIEW REQUIRED

---

## 🚨 심각한 문제점 (Critical Issues)

### Issue #1: 버퍼 언더런 처리 오류 → 오디오 끊김/Glitch

**위치**: `ChannelDspProvider.cs` Line 165-170

**문제 코드**:
```csharp
if (samplesRead == 0)
{
    Array.Clear(buffer, offset, count);
    return count; // ← 문제: 실제로는 0을 읽었지만 count를 반환
}
```

**문제점**:
- WASAPI는 요청한 `count`만큼의 샘플을 기대합니다.
- 실제로는 0개를 읽었지만 `count`를 반환하면, WASAPI는 "데이터가 모두 준비됨"으로 인식합니다.
- 결과: 실제 오디오 데이터 없이 0(무음)만 재생되어 **틱(Tick) 노이즈** 발생

**해결책**:
```csharp
if (samplesRead == 0)
{
    // Underrun: 요청한 만큼 0으로 채우고, 실제 채운 양 반환
    Array.Clear(buffer, offset, count);
    // return samplesRead; ← 0을 반환하면 WASAPI가 다시 요청
}
// 항상 실제 읽은 만큼 반환
return samplesRead;
```

---

### Issue #2: 스테레오 고정 가정 → 모노/다채널 장치 오류

**위치**: `ChannelDspProvider.cs` Line 210

**문제 코드**:
```csharp
int frames = samplesRead / 2; // ← 하드코딩된 '2'
```

**문제점**:
- `WaveFormat.Channels`가 2(스테레오)가 아닌 경우(모노, 5.1ch 등) 계산 오류
- Pan 처리, 미터링 모두 잘못된 결과

**해결책**:
```csharp
int channels = WaveFormat.Channels;
int frames = samplesRead / channels;

// Pan 처리도 채널 수에 따라 조정 필요
// 2채널 이상일 때만 Pan有意義
```

---

### Issue #3: 볼륨/팬 변경 중 일관성 없는 상태 → 오디오 글리치

**위치**: `ChannelDspProvider.cs` Line 184-206

**문제 코드**:
```csharp
// Read() 메서드 내에서:
float volumeGain = MathF.Pow(10.0f, _volumeDb / 20.0f); // ← 시점 A
float panAngle = (float)(Math.PI / 4.0 * (_pan + 1.0f)); // ← 시점 B
// UI 스레드가 이 사이에 값을 변경하면?
```

**문제점**:
- `Read()` 호출 도중 UI 스레드가 `_volumeDb`나 `_pan`을 변경 가능
- 하나의 버퍼 내에서도 일부 샘플은 이전 값, 일부는 새 값 → **클릭/팝 노이즈**

**해결책**:
```csharp
public int Read(float[] buffer, int offset, int count)
{
    // 로컬 변수에 스냅샷 복사 (atomic read)
    float volDb = _volumeDb;
    float pan = _pan;
    bool muted = _isMuted;

    // 이후 모든 계산에 로컬 변수 사용
    float volumeGain = (volDb <= MinVolumeDb) ? 0.0f : MathF.Pow(10.0f, volDb / 20.0f);
    float panAngle = (float)(Math.PI / 4.0 * (pan + 1.0f));
    // ...
}
```

---

### Issue #4: 메모리 복사 비효율 → GC Pressure 및 CPU 오버헤드

**위치**: `AudioEngine.cs` Line 285-286, `ChannelDspProvider.cs` Line 175-179

**문제 코드**:
```csharp
// AudioEngine.cs
MemoryMarshal.Cast<float, byte>(_convertBuffer.AsSpan(0, floatSampleCount))
    .CopyTo(_addSamplesBuffer.AsSpan(0, byteCount));

// ChannelDspProvider.cs
var floatSpan = MemoryMarshal.Cast<byte, float>(_readByteBuffer.AsSpan(0, bytesRead));
floatSpan.CopyTo(buffer.AsSpan(offset, samplesRead));
```

**문제점**:
- `MemoryMarshal.Cast` + `CopyTo`는 실제로 **데이터 복사** 발생
- 매 캡처 이벤트마다 8채널 × 샘플 수만큼 복사 → CPU 부하 증가
- "Zero-copy" 주석은 잘못된 정보

**최적화 방안**:
- `BufferedWaveProvider`가 내부적으로 `CircularBuffer` 사용
- 직접적인 zero-copy는 구현 복잡도 대비 이득이 적음
- 현재 구조로도 목표 CPU 15% 미만은 달성 가능 (Step 4에서 측정)

---

### Issue #5: WasapiOut Init 후 PlaybackStopped 이벤트 등록 → Race Condition

**위치**: `OutputChannel.cs` Line 142-147

**문제 코드**:
```csharp
_wasapiOut.Init(_finalSource);        // Line 142
_wasapiOut.PlaybackStopped += OnPlaybackStopped; // Line 147
```

**문제점**:
- `Init()` 호출 즉시 WASAPI 스레드가 데이터 요청 시작 가능
- PlaybackStopped 이벤트 등록 전에 중단되면 핸들러가 호출되지 않음
- Hot-unplug 감지 누락 가능성

**해결책**:
```csharp
// 이벤트 등록을 Init 이전으로 이동
_wasapiOut.PlaybackStopped += OnPlaybackStopped;
_wasapiOut.Init(_finalSource);
```

---

## ⚠️ 개선 필요 사항 (Improvements Required)

### IMP-1: MMDevice 수명 관리 불명확

**문제**:
```csharp
// AudioEngine.cs
public static IReadOnlyList<MMDevice> GetCaptureDevices()
{
    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(...).ToList();
    return devices.AsReadOnly(); // ← MMDevice는 IDisposable
}
```

**해결책**:
- MMDevice는 COM 기반 → 명시적 Dispose 필요
- `AudioDeviceItem` 래퍼 클래스 도입 또는 MMDevice 종속성 제거

---

### IMP-2: 클럭 드리프트 대응 부재

**PRD 4.3 요구사항**: 버퍼 점유율 모니터링 → 임계값 초과 시 데이터 드롭/제로 패딩

**현재 상태**:
- `DiscardOnBufferOverflow = true`만 설정 (Overflow 시 드롭)
- **언더런(버퍼 부족)에 대한 드리프트 보상 없음**
- 장시간 실행 시 버퍼 고갈 가능성

**해결책 (Step 4에서 구현)**:
```csharp
// BufferedWaveProvider.BufferedBytes 주기적 모니터링
// 임계값 미만 → 제로 샘플 삽입
// 임계값 초과 → 오래된 데이터 드롭
```

---

### IMP-3: Start() 중 예외 시 채널 정리 불완전

**문제**:
```csharp
// AudioEngine.cs Line 139-211
public void Start(...)
{
    // 채널 0-3 초기화 성공
    // 채널 4 초기화 실패 → 예외 발생
    // 채널 5-7은 초기화되지 않음
    // → 이미 시작된 채널 0-3은 계속 실행됨 (리소스 누수)
}
```

**해결책**:
```csharp
public void Start(...)
{
    var initializedChannels = new List<OutputChannel>();
    try
    {
        foreach (var settings in settingsList)
        {
            channel.Initialize(...);
            initializedChannels.Add(channel);
        }
    }
    catch
    {
        // 롤백: 이미 초기화된 채널 정리
        foreach (var ch in initializedChannels)
            ch.Stop();
        throw;
    }
}
```

---

### IMP-4: 리샘플링 체인의 불필요한 변환

**문제**:
```csharp
// OutputChannel.cs Line 125
var waveProviderSource = new SampleToWaveProvider(_dspProvider); // ISampleProvider → IWaveProvider
var resampler = new MediaFoundationResampler(waveProviderSource, deviceMixFormat); // 다시 내부 처리
```

**개선**:
- NAudio 2.2.1은 `ISampleProvider` 기반 리샘플러 제공
- `WdlResamplingSampleProvider` 또는 `MediaFoundationResampler`의 ISampleProvider 오버로드 확인

---

## 📋 검토 요약

| 항목 | 상태 | 우선순위 |
|------|------|----------|
| 버퍼 언더런 처리 | ❌ 오류 | **P0 - 즉시 수정** |
| 스테레오 고정 가정 | ❌ 오류 | **P0 - 즉시 수정** |
| 볼륨/팬 스냅샷 | ❌ 경쟁조건 | **P0 - 즉시 수정** |
| PlaybackStopped 이벤트 순서 | ❌ Race Condition | **P1 - 수정 권장** |
| Start() 예외 처리 | ⚠️ 미흡 | P2 - Step 2에서 수정 |
| 클럭 드리프트 | ⚠️ 미구현 | P2 - Step 4에서 구현 |
| 메모리 복사 | ⚠️ 비최적화 | P3 - Step 4에서 프로파일링 |
| MMDevice 수명 | ⚠️ 불명확 | P3 - Step 2/5에서 개선 |

---

## 권장 수정 순서

1. **즉시 수정 (P0)**: Issue #1, #2, #3
2. **Step 2 진행 전**: Issue #5, IMP-3
3. **Step 4 (통합 테스트)**: IMP-2, IMP-4
4. **Step 5 (안정화)**: MMDevice 수명 관리

---

## P0 수정 코드 제안

### ChannelDspProvider.cs 수정

```csharp
public int Read(float[] buffer, int offset, int count)
{
    // === P0 Fix #3: 스냅샷 복사 ===
    float volDb = _volumeDb;
    float pan = _pan;
    bool muted = _isMuted;

    // 버퍼 읽기...
    int bytesRead = _sourceBuffer.Read(_readByteBuffer, 0, byteCount);
    int samplesRead = bytesRead / sizeof(float);

    // === P0 Fix #1: 항상 실제 읽은 만큼 반환 ===
    if (samplesRead == 0)
    {
        Array.Clear(buffer, offset, count);
        // return count; ← 삭제
    }
    else
    {
        // 데이터 처리...
    }

    return samplesRead; // ← 실제 읽은 양 반환
}

private void ProcessSamples(float[] buffer, int offset, int samplesRead,
    float volDb, float pan, bool muted)
{
    // === P0 Fix #2: 채널 수 동적 처리 ===
    int channels = WaveFormat.Channels;
    int frames = samplesRead / channels;

    if (channels == 2)
    {
        // 스테레오: Pan 적용
        float volumeGain = (volDb <= MinVolumeDb) ? 0.0f : MathF.Pow(10.0f, volDb / 20.0f);
        float panAngle = (float)(Math.PI / 4.0 * (pan + 1.0f));
        float gainL = muted ? 0.0f : volumeGain * MathF.Cos(panAngle);
        float gainR = muted ? 0.0f : volumeGain * MathF.Sin(panAngle);

        for (int i = 0; i < frames; i++)
        {
            int idxL = offset + i * 2;
            buffer[idxL] *= gainL;
            buffer[idxL + 1] *= gainR;
        }
    }
    else
    {
        // 모노/다채널: Pan 없이 볼륨만 적용
        float gain = muted ? 0.0f : ((volDb <= MinVolumeDb) ? 0.0f : MathF.Pow(10.0f, volDb / 20.0f));
        for (int i = offset; i < offset + samplesRead; i++)
            buffer[i] *= gain;
    }
}
```

---

> **결론**: P0 문제 3가지는 **Step 2 진행 전 반드시 수정**해야 합니다. 그렇지 않으면 오디오 글리치, 데이터 손실, 예측 불가능한 동작이 발생합니다.
