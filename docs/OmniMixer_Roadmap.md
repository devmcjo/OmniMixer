# OmniMixer — 단계별 개발 로드맵

> **Version**: 1.1 | **Date**: 2026-03-03 | **Status**: Rev.1 — Architecture Updated

---

## Step 1 — 핵심 오디오 파이프라인 아키텍처 (뼈대 코드)

> **목표**: UI 없이 오디오 엔진이 단독으로 동작하는 C# 클래스 설계

### 1.1 설계할 클래스 목록

| 클래스 | 역할 |
|---|---|
| `AudioEngine` | 전체 파이프라인 오케스트레이터 (Start/Stop, 장치 열거) |
| `BufferedWaveProvider` ×8 | NAudio 내장 클래스. 캡처 스레드가 쌓고(채널별 복사), 출력 스레드가 읽는다 |
| `ChannelDspProvider` | `ISampleProvider` 구현체. Volume, Pan, Mute DSP 처리 + 미터 데이터 추출 |
| `MeteringEventArgs` | 레벨 미터 데이터를 UI로 전달하는 이벤트 페이로드 |
| `OutputChannel` | 단일 출력 채널. 리샘플러 + `WasapiOut` 래핑, 드리프트 보상 |
| `ChannelSettings` | 채널 설정값 POCO (DeviceId, Volume, Pan, IsMuted) |

### 1.2 파이프라인 데이터 흐름

```
[WasapiCapture]
      │ DataAvailable → float[] 정규화
      │
      ├──→ [BufferedWaveProvider #1]  ←───┬─ 채널별 독립 인스턴스, 데이터 복사
      ├──→ [BufferedWaveProvider #2]       │   Lock 범위 = 채널 내부만
      │       ...                          │   데드락 위험 제로
      └──→ [BufferedWaveProvider #8]  ───┘

      ├─ [ChannelDspProvider #1] → Volume → Pan → Mute → Metering
      │          │
      │  [MediaFoundationResampler #1]  ← 포맷 불일치 시
      │          │
      │  [WasapiOut #1]  (80ms buffer)
      │
      └─ [ChannelDspProvider #N] → ...
```

### 1.3 핵심 뼈대 코드

**`BufferedWaveProvider` 설정 (8개 독립 인스턴스)**
```csharp
// 채널별로 NAudio 내장 BufferedWaveProvider 활용
// 임계지: BufferDuration = 200ms(Overflow 방지), DiscardOnBufferOverflow = true
// 캡처 스레드: DataAvailable 마다 8개 인스턴스에 바이트 배열 복사 후 AddSamples()
// 신호 흐름: Capture → float 변환 → BufferedWaveProvider.AddSamples() → ChannelDspProvider.Read() → WasapiOut
private BufferedWaveProvider[] _channelBuffers = new BufferedWaveProvider[8];
```

**`ChannelDspProvider.cs`**
```csharp
// ISampleProvider 구현
// Read() 호출마다: 링버퍼 → Volume 게인 → Equal-Power Pan → Mute → 미터 샘플 누적
public class ChannelDspProvider : ISampleProvider
{
    public float VolumeLevelDb { get; set; }   // -80 ~ +6 dB
    public float Pan { get; set; }             // -1.0 ~ +1.0
    public bool IsMuted { get; set; }
    public event EventHandler<MeteringEventArgs> MeterUpdated;

    public int Read(float[] buffer, int offset, int count) { ... }

    private void ApplyVolumeAndPan(float[] buf, int offset, int count) { ... }
    private void UpdateMeter(float[] buf, int offset, int count) { ... }
}
```

**`OutputChannel.cs`**
```csharp
// 하나의 물리 출력 장치를 담당
// WasapiOut + MediaFoundationResampler + ChannelDspProvider 조합
// 예외 접수가 잤 등 실패 시 본 채널만 졸료, 다른 채널에 영향 없음
public class OutputChannel : IDisposable
{
    public void Initialize(MMDevice device, BufferedWaveProvider buffer) { ... }
    public void Start() { ... }
    public void Stop() { ... }   // 다른 채널 중단 없이 이 채널만 Dispose
    public ChannelDspProvider DspProvider { get; }
}
```

**`AudioEngine.cs`**
```csharp
public class AudioEngine : IDisposable
{
    public IReadOnlyList<MMDevice> GetCaptureDevices() { ... }
    public IReadOnlyList<MMDevice> GetOutputDevices() { ... }
    public void Start(MMDevice captureDevice, IEnumerable<ChannelSettings> channelSettings) { ... }
    public void Stop() { ... }
    public OutputChannel[] Channels { get; }  // 총 8개
}
```

### 1.4 검증 방법
- 콘솔 테스트 앱(Console App)을 별도 작성하여 UI 없이 오디오가 흐르는지 확인
- `Debug.WriteLine`으로 미터 값 출력 확인

---

## Step 2 — WPF 프로젝트 세팅 & ViewModel 기반 구축

> **목표**: WPF 프로젝트 생성, MVVM 구조 확립, AudioEngine 바인딩

### 2.1 작업 목록
- [ ] `dotnet new wpf -n OmniMixer` 프로젝트 생성
- [ ] NuGet 패키지 추가: `NAudio`, `CommunityToolkit.Mvvm`
- [ ] `MainViewModel`: `AudioEngine` 소유, Start/Stop 커맨드 정의
- [ ] `ChannelViewModel`: 8개 인스턴스. Volume, Pan, IsMuted, MeterLevel 프로퍼티
- [ ] `ChannelDspProvider.MeterUpdated` → `ChannelViewModel.MeterLevel` 자동 반영 (Dispatcher)

### 2.2 핵심 설계 원칙
- `MainViewModel`이 `AudioEngine`의 유일한 소유자
- UI는 ViewModel의 프로퍼티만 바인딩, 오디오 클래스에 직접 접근 금지
- `ObservableCollection<AudioDeviceItem>`으로 장치 목록 관리

---

## Step 3 — WPF UI 구현 (채널 스트립 & 레벨 미터)

> **목표**: 시각적으로 완성된 믹서 UI 구현

### 3.1 작업 목록
- [ ] `ChannelStripControl.xaml` (UserControl): 8채널 재사용을 위한 컨트롤
  - ComboBox (출력 장치)
  - Pan Slider (-1.0 ~ 1.0, 가로)
  - Volume Slider (-80 ~ +6 dB, 세로, Inverted)
  - Level Meter (ProgressBar, 세로, Dispatcher 업데이트)
  - Mute ToggleButton
- [ ] `MainWindow.xaml`: 8개 `ChannelStripControl` 수평 배치 + Start/Stop 버튼
- [ ] dB ↔ 슬라이더 값 변환 `IValueConverter` 구현
- [ ] **오디오 페이더 테이퍼(Audio Fader Taper) 적용**: dB의 로그 특성을 반영하여 UI 슬라이더의 **중간 지점이 체감상 절반 볼륨(-12dB ≈ 50%)**으로 들리도록 로그 컬브 변환을 `IValueConverter`에 통합. 실제 체감에 맥으는 에너지 변화를 제공하여 자연스러운 페이더 등마감 구현
- [ ] 레벨 미터 컬러 그레이디언트 (초록 → 노랑 → 빨강)

### 3.2 UI 상태 관리
| 상태 | ComboBox | Slider | Mute | Start/Stop |
|---|---|---|---|---|
| Stopped | 활성화 | 활성화 | 활성화 | "▶ START" |
| Running | **비활성화** | 활성화 | 활성화 | "■ STOP" |

---

## Step 4 — 통합 테스트 & 실장치 검증

> **목표**: 실제 VB-Cable + 물리 스피커 환경에서 전체 파이프라인 검증

### 4.1 테스트 체크리스트
- [ ] VB-Cable → 단일 출력 장치 라우팅 정상 동작
- [ ] 샘플레이트 불일치(48kHz input → 44.1kHz output) 리샘플링 확인
- [ ] **리샘플러 Quality 튜닝**: `MediaFoundationResampler.ResamplerQuality를` 중간값(30 ~ 40)으로 설정하고 CPU 사용률 측정. 목표치(15%) 초과 여부 확인 후 최적값 선정
- [ ] 8채널 동시 출력 시 CPU/메모리 측정
- [ ] Pan 슬라이더 L/R 방향 오디오 편향 확인
- [ ] Mute 즉각 반응 확인
- [ ] **클럭 드리프트 장기 테스트**: 2시간 이상 연속 동작 시 드리프트 보상으로 발생하는 무음 삽입/데이터 드롭 경계에서 **틱(Tick) 노이즈 발생 여부** 를 청감으로 확인하고, `AddSamples` ~ `Read` 간 임계값을 정밀 튜닝
- [ ] 장시간(30분+) 동작 시 버퍼 드리프트 여부 확인

### 4.2 디버그 도구
- `AudioEngine` 내부에 버퍼 점유율 로그 출력 (Debug.WriteLine)
- 레벨 미터 값 범위 확인

---

## Step 5 — 안정화, 예외 처리 & 마무리 폴리싱

> **목표**: 상용 수준의 안정성과 사용성 확보

### 5.1 작업 목록
- [ ] 장치 연결 해제(Hot-unplug) 예외 처리 및 UI 알림
- [ ] Start 실패 시 에러 다이얼로그 표시
- [ ] 앱 설정 저장/불러오기 (`ChannelSettings` JSON 직렬화 → `appsettings.json`)
- [ ] 앱 아이콘 및 어셈블리 버전 정보 설정
- [ ] 단일 인스턴스 실행 보장 (Mutex)
- [ ] `README.md` 및 VB-Cable 설치 안내 문서 작성

---

## 일정 추정

| Step | 예상 복잡도 | 비고 |
|---|---|---|
| Step 1 | ★★★★☆ | 가장 핵심적이고 복잡한 단계 |
| Step 2 | ★★☆☆☆ | MVVM 패턴에 익숙하다면 빠르게 완료 |
| Step 3 | ★★★☆☆ | WPF 커스텀 컨트롤 스타일링 포함 |
| Step 4 | ★★☆☆☆ | 실장치 환경 필요 |
| Step 5 | ★★☆☆☆ | 빠듯하게 완성도 향상 |
