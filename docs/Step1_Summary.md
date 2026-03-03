# OmniMixer - Step 1 개발 완료 요약

> **Date**: 2026-03-03 | **Status**: Completed | **Total Lines**: ~1,031 lines

---

## 완성된 핵심 클래스 (Core Classes)

### 1. AudioEngine.cs (436 lines)
**역할**: 오디오 엔진의 최상위 오케스트레이터

**주요 기능**:
- `WasapiCapture`로 VB-Cable에서 PCM 스트림 수신
- 캡처된 데이터를 float으로 변환 후 8개 채널의 `BufferedWaveProvider`에 각각 복사 (1 Writer → 8 Buffers)
- 8개의 `OutputChannel` 생명주기 관리 (시작/중지/오류 격리)
- 다양한 포맷 지원: IEEE Float 32-bit, PCM 16-bit, 24-bit, 32-bit

**스레드 모델**:
```
[Capture Thread]  DataAvailable → 8개 버퍼에 순차 복사
        ├──→ [BufferedWaveProvider #1]
        ├──→ [BufferedWaveProvider #2]
        └──→ [BufferedWaveProvider #8]

[Output Thread #1]  WasapiOut Pull → ChannelDspProvider.Read() ← Buffer #1
[Output Thread #2]  WasapiOut Pull → ChannelDspProvider.Read() ← Buffer #2
        ... (각 스레드는 독립 버퍼만 접근)
```

---

### 2. ChannelDspProvider.cs (274 lines)
**역할**: DSP 처리 파이프라인의 핵심 (`ISampleProvider` 구현)

**처리 순서**: `BufferedWaveProvider` → Volume → Pan → Mute → Peak/RMS 미터링

**DSP 구현**:
- **Volume**: dB → 선형 변환 `Amplitude = 10^(dB/20)`
- **Pan**: Equal-Power Panning
  - L_gain = cos(π/4 × (P + 1))
  - R_gain = sin(π/4 × (P + 1))
- **Mute**: 출력 신호 0으로 설정 (처리는 계속)

**미터링**:
- 약 30FPS 주기로 Peak/RMS 값 계산
- `MeterUpdated` 이벤트로 UI에 전달

---

### 3. OutputChannel.cs (248 lines)
**역할**: 단일 물리적 출력 장치를 담당하는 채널 클래스

**소유 구조**:
```
BufferedWaveProvider → ChannelDspProvider → [MediaFoundationResampler] → WasapiOut
```

**핵심 기능**:
- 각 채널이 독립적인 `BufferedWaveProvider` 소유 (200ms 버퍼)
- 입출력 포맷 불일치 시 `MediaFoundationResampler`로 변환
- Hot-unplug 감지 및 채널 격리: 한 채널 오류 시 다른 채널에 영향 없음
- Resampler Quality = 35 (품질/CPU 균형)

---

### 4. ChannelSettings.cs (34 lines)
**역할**: 단일 출력 채널의 설정값 POCO

**속성**:
- `ChannelIndex`: 채널 인덱스 (0~7)
- `DeviceId`: 출력 장치 MMDevice ID (null이면 비활성)
- `VolumeDb`: 볼륨 (-80.0 ~ +6.0 dB)
- `Pan`: 팬 (-1.0 ~ +1.0)
- `IsMuted`: 음소거 여부

---

### 5. MeteringEventArgs.cs (39 lines)
**역할**: 레벨 미터 이벤트 데이터 페이로드

**데이터**:
- `ChannelIndex`: 채널 인덱스
- `PeakLeft`, `PeakRight`: 좌/우 채널 피크 값
- `RmsLeft`, `RmsRight`: 좌/우 채널 RMS 값

---

## 버그 수정 내역

### Issue #1: WasapiOut 생성자 파라미터 오류
**원인**: `latencyMilliseconds`는 존재하지 않는 파라미터명
**해결**: `latency`로 변경 (positional parameter)
```csharp
// 수정 전
_wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared,
    useEventSync: true, latencyMilliseconds: 80);

// 수정 후
_wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared,
    useEventSync: true, latency: 80);
```

### Issue #2: SampleToWaveProvider namespace 누락
**원인**: `NAudio.Wave.SampleProviders` using 지시문 없음
**해결**: namespace 추가
```csharp
using NAudio.Wave.SampleProviders;
```

---

## 검증 결과

### 빌드 상태
```
빌드 성공: 0개 오류, 1개 경고 (CS7022: App.Main 중복 - WPF 정상)
```

### 콘솔 테스트 실행
```
[1/4] 장치 열거 테스트...          ✓ 성공
[2/4] AudioEngine 초기화...        ✓ 성공
[3/4] 채널 설정 준비...            ✓ 성공
[4/4] 오디오 엔진 시작 테스트...    ✓ 성공 (5초 동안 정상 동작)
[정리] AudioEngine Dispose...      ✓ 성공
```

---

## 다음 단계 (Step 2)

MVVM 구조 확립 및 AudioEngine 바인딩:
1. `MainViewModel`: `AudioEngine` 소유, Start/Stop 커맨드 정의
2. `ChannelViewModel`: 8개 인스턴스. Volume, Pan, IsMuted, MeterLevel 프로퍼티
3. `ChannelDspProvider.MeterUpdated` → `ChannelViewModel.MeterLevel` 자동 반영

---

## 기술 스택 확인

| 구성요소 | 버전 |
|---------|------|
| .NET | 6.0-windows |
| NAudio | 2.2.1 |
| CommunityToolkit.Mvvm | 8.2.2 |

---

## 프로젝트 구조

```
omnimixer/
├── Audio/
│   ├── AudioEngine.cs          (436 lines)
│   ├── ChannelDspProvider.cs   (274 lines)
│   ├── ChannelSettings.cs      (34 lines)
│   ├── MeteringEventArgs.cs    (39 lines)
│   └── OutputChannel.cs        (248 lines)
├── AudioEngineTest/            (Step 1 검증용)
│   ├── AudioEngineTest.csproj
│   └── Program.cs
├── docs/
│   ├── OmniMixer_PRD.md
│   ├── OmniMixer_Roadmap.md
│   └── Step1_Summary.md        (this file)
├── OmniMixer.csproj
└── omnimixer.sln
```
