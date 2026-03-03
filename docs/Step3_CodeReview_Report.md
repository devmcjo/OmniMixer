# Step 3 코드 리뷰 보고서

> **작성일**: 2026-03-03
> **검토 범위**: Step 3 (WPF UI 구현) 전체 소스코드
> **기준 문서**: OmniMixer_PRD.md, OmniMixer_Roadmap.md

---

## 🔴 핵심 발견 사항 요약

| 중요도 | 항목 | 상태 |
|--------|------|------|
| 🔴 **P0** | 콤볼박스에 출력 장치 목록 표시 안 됨 | ❌ 미해결 |
| 🔴 **P0** | 볼륨 슬라이더 트랙 클릭 시 최대/최소값으로 점프 | ⚠️ 수정 필요 |
| 🟡 **P1** | 장치 목록 변경 시 UI 미업데이트 | ⚠️ 개선 필요 |
| 🟡 **P1** | 예외 처리 누락 (Race Condition) | ⚠️ 개선 필요 |
| 🟢 **P2** | 코드 일관성 및 가독성 | ✅ 양호 |

---

## 🔴 P0: 콤볼박스 출력 장치 목록 미표시

### 문제 현상
각 채널(CH0~CH7)의 출력 장치 선택 콤볼박스에 스피커/출력 장치 목록이 나타나지 않음.

### 근본 원인 분석
```csharp
// MainViewModel.cs - 생성자
public MainViewModel()
{
    // 1. LoadDevices()가 OutputDevices를 채우기 전에
    LoadDevices();  // OutputDevices 채움

    // 2. 채널 ViewModel 생성
    for (int i = 0; i < AudioEngine.MaxChannels; i++)
    {
        Channels[i] = new ChannelViewModel(i, _dispatcher)
        {
            OutputDevices = OutputDevices  // 참조 전달
        };
    }
}
```

문제는 `ChannelViewModel.OutputDevices`가 `ObservableCollection` 참조를 받지만, **XAML 바인딩이 이를 인식하지 못함**.

```xml
<!-- ChannelStripControl.xaml -->
<ComboBox ItemsSource="{Binding OutputDevices}" ... />
```

`DataContext`는 `ChannelViewModel`이므로 `OutputDevices`는 null일 수 있음.

### 해결 방안
**방법 A**: MainViewModel의 OutputDevices를 직접 참조하도록 변경
```xml
<ComboBox ItemsSource="{Binding DataContext.OutputDevices,
                          RelativeSource={RelativeSource AncestorType=Window}}" ... />
```

**방법 B**: ChannelViewModel 생성 시 OutputDevices가 null이 아님을 보장

**방법 C**: INotifyPropertyChanged를 활용한 지연 바인딩

**권장**: 방법 A (가장 직관적이고 WPF 표준 패턴)

---

## 🔴 P0: 볼륨 슬라이더 트랙 클릭 동작 문제

### 문제 현상
볼륨 페이더의 트랙(Thumb이 아닌 영역)을 클릭하면:
1. 첫 클릭: 최대값으로 점프
2. 두 번째 클릭: 최소값으로 점프

### 기대 동작
클릭한 위치에 해당하는 값으로 이동 (IsMoveToPointEnabled)

### 근본 원인
```xml
<Slider Value="{Binding VolumeDb,
              Converter={StaticResource DbToSliderPositionConverter}}" />
```

**문제점**: `IsMoveToPointEnabled="True"`는 Slider에 설정되어 있으나:
1. `Value` 바인딩이 `Converter`를 거쳐서 실제 클릭 위치와 다른 값이 설정됨
2. `Track.DecreaseRepeatButton` / `IncreaseRepeatButton`이 `Command`로 인해 트랙 클릭을 가로챔

### 해결 방안
Custom Thumb 템플릿에서 트랙 클릭을 직접 처리하거나, `RepeatButton` 제거 후 `Track`에만 `IsMoveToPointEnabled` 적용.

---

## 🟡 P1: 장치 목록 변경 시 UI 미업데이트

### 문제
`MainViewModel.RefreshDevicesCommand`가 호출되면:
1. `OutputDevices.Clear()` 호출
2. 새로운 장치 추가

하지만 이미 생성된 채널의 `ComboBox`는 이 변경을 감지하지 못함.

### 해결
`ObservableCollection` 참조는 유지되므로 `INotifyCollectionChanged` 이벤트가 발생해야 함. 하지만 채널별 `OutputDevices`는 같은 참조를 공유하므로 자동 업데이트되어야 함.

**실제 문제**: `ComboBox`가 `null`이었거나 바인딩이 깨진 상태.

---

## 🟡 P1: 예외 처리 누락 (Race Condition)

### AudioEngine.cs
```csharp
private void OnDataAvailable(object? sender, WaveInEventArgs e)
{
    foreach (var channel in _channels)
    {
        // 문제: IsActive와 Buffer 체크 순서
        if (channel.Buffer is null || !channel.IsActive) continue;
        //        ↑ null 체크 후 ↓에서 사용
        channel.Buffer.AddSamples(...);  // 여기서 Buffer가 null이 될 수 있음 (Race)
    }
}
```

**해결**: 한 번의 null 체크로 충분하지 않음. `?.` 연산자 사용:
```csharp
channel.Buffer?.AddSamples(...);
```

### OutputChannel.cs
```csharp
private void SafeStopAndDispose()
{
    try { _wasapiOut?.Stop(); } catch { /* 무시 */ }
    // 모든 예외를 무시하면 디버깅 어려움
}
```

**개선**: Debug.WriteLine이라도 추가

---

## 🟡 P1: PAN 슬라이더 Track.Thumb 누락

```xml
<Track x:Name="PART_Track">
    <Track.Thumb>  <!-- 여기에 Decrease/Increase RepeatButton이 없음 -->
```

Pan 슬라이더에는 `DecreaseRepeatButton`과 `IncreaseRepeatButton`이 없어 트랙 클릭이 동작하지 않을 수 있음.

---

## 🟢 P2: PRD vs 구현 대조

| PRD 요구사항 | 구현 상태 | 비고 |
|-------------|----------|------|
| FR-UI-01: 8채널 수평 배치 | ✅ | UniformGrid Rows=1 |
| FR-UI-02: 콤볼박스, Pan, Volume, Meter, Mute | ⚠️ | 콤볼박스 동작 안 함 |
| FR-UI-03: Start/Stop 버튼 | ✅ | 구현됨 |
| FR-UI-04: Start 시 ComboBox 비활성화 | ✅ | IsEnabled 바인딩 |
| FR-UI-05: 입력 장치 선택 | ✅ | 헤더에 구현 |
| FR-DSP-01: Volume dB 변환 | ✅ | ChannelDspProvider |
| FR-DSP-02: Equal-Power Panning | ✅ | cos/sin 적용 |
| FR-DSP-03: Mute | ✅ | 구현됨 |
| FR-LV-01~04: 레벨 미터링 | ⚠️ | 이벤트는 있으나 UI 연결 확인 필요 |

---

## 📝 권장 수정 우선순위

### 즉시 수정 필요 (P0)
1. 콤볼박스 출력 장치 표시 문제
2. 볼륨 슬라이더 트랙 클릭 동작

### 단기 개선 (P1)
3. Race Condition 방어 코드
4. 예외 로깅 추가
5. Pan 슬라이더 트랙 클릭 지원

### 중기 개선 (P2)
6. 레벨 미터 UI 실제 연결 확인
7. 디자인 피드백 반영

---

**보고서 작성**: Claude Code
**검토 완료**: Step 3 코드베이스 전체
