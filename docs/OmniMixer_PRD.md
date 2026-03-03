# OmniMixer — 제품 요구사항 정의서 (PRD)

> **Version**: 1.1 | **Date**: 2026-03-03 | **Status**: Draft (Rev.1 — Architecture Updated)

---

## 1. 제품 개요 (Product Overview)

| 항목 | 내용 |
|---|---|
| **제품명** | OmniMixer |
| **실행 파일** | OmniMixer.exe |
| **플랫폼** | Windows 10/11 (x64) |
| **기술 스택** | C# (.NET 8), WPF, NAudio |
| **의존성** | VB-Audio Virtual Cable (사용자 수동 설치) |

### 1.1 목적
Windows PC 환경에서 **단일 가상 오디오 스트림(VB-Cable)을 캡처**하여 **최대 8개의 물리적 출력 장치에 동시에 실시간 분배**하는 다채널 오디오 라우팅 믹서.

### 1.2 사용 시나리오
1. 사용자가 VB-Audio Virtual Cable을 설치하고 Windows 기본 출력 장치로 설정
2. OmniMixer를 실행하여 각 채널별 출력 장치, 볼륨, 팬을 설정
3. **Start** 버튼 클릭 → VB-Cable 출력 스트림을 캡처하여 선택된 모든 물리적 장치로 분배 시작
4. 실시간 레벨 미터로 각 출력 채널의 상태를 모니터링

> [!WARNING]
> **지연 시간(Latency) 고지**: 본 믹서는 음악 감상, BGM 재생 등 실시간성이 극도로 요구되지 않는 환경에 최적화되어 있습니다. 마이크 모니터링, 리듬 게임 등 초저지연(< 20ms)이 필요한 작업에는 적합하지 않으며, 설계 목표 지연은 50 ~ 150ms 수준입니다.

---

## 2. 기능 요구사항 (Functional Requirements)

### 2.1 오디오 입력 (Input)

| ID | 요구사항 |
|---|---|
| **FR-IN-01** | `WasapiCapture`를 사용하여 VB-Cable Output (혹은 선택된 캡처 장치)에서 PCM 스트림을 캡처한다 |
| **FR-IN-02** | 입력 장치 목록을 앱 시작 시 열거하여 사용자가 선택할 수 있어야 한다 |
| **FR-IN-03** | 캡처된 데이터는 스레드-안전한 공유 링 버퍼에 즉시 기록된다 |

### 2.2 오디오 출력 (Multi-Output Routing)

| ID | 요구사항 |
|---|---|
| **FR-OUT-01** | 최대 8개의 독립적인 출력 채널을 지원한다 |
| **FR-OUT-02** | 각 출력 채널은 독립적인 `WasapiOut` 인스턴스를 사용한다 |
| **FR-OUT-03** | 캡처 스레드는 수신된 PCM 데이터를 채널별로 독립 할당된 **8개의 `BufferedWaveProvider`에 각각 복사(1 Writer → 8 Independent Buffers)** 하여 밀어 넣는다. 각 출력 스레드는 자신의 버퍼에서만 읽어 처리한다. |
| **FR-OUT-04** | 선택된 출력 장치가 없는 채널은 비활성화(Skip)된다 |
| **FR-OUT-05** | **채널 간 완전한 격리성**: 특정 채널의 장치 연결 해제(Hot-unplug) 또는 오류 발생 시, 해당 채널만 개별적으로 중단·복구 처리하며, 나머지 정상 채널의 오디오 출력은 절대 중단되지 않아야 한다 |

### 2.3 DSP 파이프라인

| ID | 요구사항 | 상세 사양 |
|---|---|---|
| **FR-DSP-01** | **Volume 제어** | dB → 선형 변환: `Amplitude = 10^(dB/20)` |
| **FR-DSP-02** | **Pan 제어** | Equal-Power Panning 적용 |
| | L_gain | `cos(π/4 × (P + 1))` |
| | R_gain | `sin(π/4 × (P + 1))` |
| | P 범위 | `-1.0(Full Left) ~ 0.0(Center) ~ +1.0(Full Right)` |
| **FR-DSP-03** | **Mute** | 해당 채널 신호를 0으로 만든다 (처리 중단 아님) |

### 2.4 레벨 미터링

| ID | 요구사항 |
|---|---|
| **FR-LV-01** | DSP 처리 후 샘플에서 Left/Right 채널별 Peak 또는 RMS 값을 추출한다 |
| **FR-LV-02** | 약 30FPS(~33ms) 주기로 UI 스레드에 미터 데이터를 전달한다 |
| **FR-LV-03** | 레벨 값 범위는 0.0 ~ 1.0 (선형 진폭) |
| **FR-LV-04** | Peak Hold 기능: 최근 피크 값을 일정 시간 유지 후 감소 |

### 2.5 UI 기능

| ID | 요구사항 |
|---|---|
| **FR-UI-01** | 채널 스트립 8개를 수평으로 나란히 배치 |
| **FR-UI-02** | 각 채널: ComboBox (출력 장치), Pan 슬라이더, Volume 슬라이더, 레벨 미터, Mute 버튼 |
| **FR-UI-03** | 전체 하단: Start/Stop 버튼 (상태에 따라 텍스트 변경) |
| **FR-UI-04** | Start 클릭 시 ComboBox 비활성화, Stop 클릭 시 재활성화 |
| **FR-UI-05** | 입력 장치 선택 ComboBox (상단 또는 헤더 영역) |

---

## 3. 비기능 요구사항 (Non-Functional Requirements)

| ID | 항목 | 목표 |
|---|---|---|
| **NFR-01** | **지연 (Latency)** | 입력~출력 왕복 지연 ≤ 150ms |
| **NFR-02** | **안정성** | 8채널 동시 출력 시 오디오 끊김(Glitch) 없이 24시간 연속 동작 |
| **NFR-03** | **CPU 사용률** | 8채널 풀 가동 시 최신 CPU 기준 ≤ 15% |
| **NFR-04** | **메모리** | 프로세스 메모리 ≤ 200MB |
| **NFR-05** | **호환성** | Windows 10 21H2 이상, .NET 6 Runtime 필요 |

---

## 4. 핵심 기술 제약사항 (Critical Technical Constraints)

### 4.1 실시간 리샘플링
- 입력 장치와 출력 장치의 샘플레이트/비트뎁스가 다를 수 있음
- 각 채널 출력 전에 `MediaFoundationResampler` (또는 `WdlResamplingSampleProvider`) 를 거쳐 포맷 변환

> [!IMPORTANT]
> **CPU 과부하 방지**: 8채널 동시 리샘플링은 CPU 목표치(15%)를 초과할 수 있습니다. 다음 두 가지 완화 전략을 함께 적용합니다.
> 1. **사용자 안내 UI**: 앱 내 설정 영역에 "Windows 소리 설정에서 입출력 장치의 샘플레이트를 48kHz로 통일하면 리샘플러가 동작하지 않아 CPU 부하가 크게 줄어듭니다"라는 안내 메시지를 제공합니다.
> 2. **Quality 속성 튜닝**: `MediaFoundationResampler.ResamplerQuality`를 최고값(60) 대신 중간값(30 ~ 40)으로 설정하여 품질과 CPU 사용률의 균형을 맞추는 단계를 개발 로드맵 Step 4에 포함합니다.

### 4.2 버퍼 관리
- 각 `WasapiOut` 의 WASAPI 버퍼 크기: **50ms ~ 100ms** 범위
- `BufferedWaveProvider` 또는 직접 구현한 `RingBuffer<float>` 활용

### 4.3 클럭 드리프트 대응
- 서로 다른 물리 장치는 클럭 오차가 발생할 수 있음
- **전략**: 각 채널의 `BufferedWaveProvider` 점유율을 주기적으로 모니터링 → 임계값 초과 시 오래된 데이터 드롭, 미만 시 무음(Zero Padding) 삽입
- **주의**: 드롭/삽입 경계에서 틱(Tick) 노이즈가 발생할 수 있으며, 임계값은 Step 4의 2시간 이상 장기 테스트를 통해 정밀 튜닝한다

### 4.4 스레드 모델
```
[Capture Thread]  WasapiCapture DataAvailable
        │
        │  (데이터 도착 시 8개 버퍼에 순차 복사)
        ├──→ [BufferedWaveProvider #1]
        ├──→ [BufferedWaveProvider #2]
        │            ....
        └──→ [BufferedWaveProvider #8]

[Output Thread #1]  WasapiOut Pull → ChannelDspProvider.Read() → BufferedWaveProvider #1
[Output Thread #2]  WasapiOut Pull → ChannelDspProvider.Read() → BufferedWaveProvider #2
        ... (이하 동일, 각 스레드는 독립 버퍼만 접근 — 스레드 간 공유 없음)

[UI Thread]  DispatcherTimer(~30fps) → ChannelViewModel.MeterLevel 업데이트
```

> [!NOTE]
> 각 출력 스레드는 자신의 `BufferedWaveProvider`에만 접근하므로 **스레드 간 공유 상태가 없어** 데드락 및 글리치 위험이 제거됩니다. 버퍼 복사 오버헤드는 PCM float 데이터 기준 미미합니다.

---

## 5. UI 화면 구성 (Screen Layout)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ [Input Device: CABLE Output ▼]                              OmniMixer   │
├──────────┬──────────┬──────────┬──────────┬──────────┬──────────┬──────┤
│ CH 1     │ CH 2     │ CH 3     │ CH 4     │ CH 5     │ CH 6     │ ...  │
│[Device▼] │[Device▼] │[Device▼] │[Device▼] │[Device▼] │[Device▼] │      │
│          │          │          │          │          │          │      │
│ L──●──R  │ L──●──R  │   ...    │          │          │          │      │
│  (Pan)   │  (Pan)   │          │          │          │          │      │
│          │          │          │          │          │          │      │
│  [▲] [█] │  [▲] [█] │          │          │          │          │      │
│  [│] [│] │  [│] [│] │  Volume  │          │          │          │      │
│  [▼] [ ] │  [▼] [ ] │  Fader & │          │          │          │      │
│          │          │  Meter   │          │          │          │      │
│ [MUTE]   │ [MUTE]   │          │          │          │          │      │
├──────────┴──────────┴──────────┴──────────┴──────────┴──────────┴──────┤
│                        [ ▶ START ] / [ ■ STOP ]                        │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 6. 용어 정의

| 용어 | 설명 |
|---|---|
| **채널 (Channel)** | 하나의 출력 스트림 처리 단위 (DSP + 출력 장치 한 쌍) |
| **Ring Buffer** | 캡처 스레드와 출력 스레드 간 데이터를 공유하는 순환 버퍼 |
| **DSP Pipeline** | Volume → Pan → Mute → Metering 순서로 신호를 처리하는 체인 |
| **Clock Drift** | 서로 다른 하드웨어 클럭이 미세하게 달라 버퍼 불균형이 발생하는 현상 |
| **Equal-Power Panning** | 팬 이동 시 인지 음량이 일정하게 유지되도록 삼각함수 게인을 적용하는 방식 |
