# Step 1 P0/P1 문제 수정 완료 보고서

> **Date**: 2026-03-03 | **Status**: All Fixes Applied & Verified

---

## 수정 완료된 문제

### ✅ Issue #3 - Volume/Pan Race Condition (P0)
**파일**: `Audio/ChannelDspProvider.cs`

**문제**: `Read()` 메서드 내에서 `_volumeDb`, `_pan`, `_isMuted`를 직접 읽어서 UI 스레드와 경쟁조건 발생

**해결**: Read() 시작 시점에 로컬 변수로 스냅샷 복사
```csharp
public int Read(float[] buffer, int offset, int count)
{
    // === P0 FIX #3: DSP 파라미터 스냅샷 복사 ===
    float volDb = _volumeDb;
    float pan = _pan;
    bool muted = _isMuted;

    // 이후 모든 계산에 로컬 변수 사용
    float volumeGain = (volDb <= MinVolumeDb) ? 0.0f : MathF.Pow(10.0f, volDb / 20.0f);
    // ...
}
```

---

### ✅ Issue #1 - Buffer Underrun Handling (P0)
**파일**: `Audio/ChannelDspProvider.cs`

**문제**: Underrun 시 `return count`로 거짓 반환 → WASAPI 오인

**해결**: 실제 읽은 샘플 수 반환
```csharp
if (samplesRead == 0)
{
    Array.Clear(buffer, offset, count);
    // === P0 FIX #1: 실제 읽은 샘플 수(0)를 반환 ===
    return samplesRead; // 정확히 0을 반환
}
```

---

### ✅ Issue #2 - Hardcoded Stereo Assumption (P0)
**파일**: `Audio/ChannelDspProvider.cs`

**문제**: `int frames = samplesRead / 2;`로 스테레오 고정 가정

**해결**: 동적 채널 수 처리
```csharp
// === P0 FIX #2: 스테레오 고정 가정 제거 ===
int channels = WaveFormat.Channels;
int frames = samplesRead / channels;

if (channels == 2)
{
    // 스테레오: Volume + Pan 적용
}
else
{
    // 모노/다채널: Volume만 적용
}
```

**추가 수정**: `FireMeterUpdate(bool isStereo)` 파라미터 추가하여 모노/다채널 미터링 지원

---

### ✅ Issue #5 - PlaybackStopped Event Registration Order (P1)
**파일**: `Audio/OutputChannel.cs`

**문제**: `_wasapiOut.Init()` 이후에 이벤트 등록 → Race Condition

**해결**: 이벤트 등록을 Init 이전으로 이동
```csharp
// === P1 FIX #5: 이벤트를 Init() 전에 등록 ===
_wasapiOut.PlaybackStopped += OnPlaybackStopped;
_wasapiOut.Init(_finalSource);
```

---

## 검증 결과

| 항목 | 결과 |
|------|------|
| 빌드 | ✅ 성공 (0 errors, 1 warning) |
| 콘솔 테스트 | ✅ 5초 동안 정상 동작 |
| 장치 열거 | ✅ 정상 |
| AudioEngine Start/Stop | ✅ 정상 |

---

## 수정된 파일

1. `Audio/ChannelDspProvider.cs` - Issues #1, #2, #3
2. `Audio/OutputChannel.cs` - Issue #5

---

## 다음 단계

- Step 2 진행: MVVM 구조 확립 및 UI 바인딩
- 클럭 드리프트 대응 (PRD 4.3)은 Step 4에서 구현 예정
