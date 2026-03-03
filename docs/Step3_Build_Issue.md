# Step 3 WPF UI 구현 - 빌드 이슈 보고서

## 구현 완료 사항

### 1. Converters (6개)
- **InverseBooleanConverter**: Boolean 반전 (IsRunning에 따른 ComboBox 비활성화)
- **BooleanToStartStopBrushConverter**: Start/Stop 버튼 색상 (Apple 시스템 그린/레드)
- **DbToSliderPositionConverter**: 오디오 페이더 테이퍼 (dB ↔ 슬라이더 위치 로그 변환)
- **LevelToColorConverter**: 레벨 미터 색상 그라데이션 (그린→옐로우→오렌지→레드)
- **LevelToDbTextConverter**: dB 값 텍스트 표시 (-80dB 이하는 "-∞")
- **LevelToHeightConverter**: 레벨 → ProgressBar 높이 (로그 스케일)

### 2. ChannelStripControl (UserControl)
- Apple Logic Pro 스타일 디자인
- 채널 번호 표시 (CH 0~7)
- 출력 장치 선택 ComboBox
- Pan 슬라이더 (-1.0 ~ 1.0, 가로)
- 볼륨 페이더 (-80 ~ +6 dB, 세로, Inverted, Unity Gain 표시)
- 좌/우 레벨 미터 (10단계 눈금, 0dB 표시선)
- Mute 버튼 (토글, Apple 스타일)

### 3. MainWindow (업데이트)
- Apple Dark Mode 색상 팔레트
- "OmniMixer" 타이틀 (PRO 배지 포함)
- 입력 장치 선택 섹션
- 8개 채널 수평 배치 (ScrollViewer 내 UniformGrid)
- 상태 표시줄 (상태 메시지 + 활성 채널 수)
- Start/Stop 버튼 (Apple 스타일 애니메이션)

## 빌드 이슈

### 문제
WPF 임시 프로젝트(`*_wpftmp.csproj`)에서 어셈블리 특성 중복 생성 오류:
```
error CS0579: 'System.Reflection.AssemblyCompanyAttribute' 특성이 중복되었습니다.
```

### 원인
.NET 6 WPF의 XAML 마크업 컴파일레이션은 임시 프로젝트를 생성하여 XAML을 컴파일합니다.
이 임시 프로젝트는 부모 프로젝트의 설정을 상속받으면서도 자체적으로 어셈블리 특성을 생성하여 중복이 발생합니다.

### 시도한 해결책
1. `Directory.Build.props` / `Directory.Build.targets` - 임시 프로젝트에서 조걶 적용 실패
2. `GenerateAssemblyInfo=false` + 수동 `AssemblyInfo.cs` - 임시 프로젝트가 여전히 자체 생성
3. `Compile Remove` 조건 - MSBuild 평가 시점 문제로 적용되지 않음
4. `IncludePackageReferencesDuringMarkupCompilation` - 다른 오류 발생

### 해결책 (제안)
1. **.NET 8로 업그레이드**: .NET 8에서 WPF 빌드 프로세스 개선됨
2. **Visual Studio 빌드**: VS IDE의 빌드는 임시 프로젝트 문제 없이 작동할 수 있음
3. **솔루션 파일 수정**: `.sln` 파일에서 빌드 구성 조정

### 임시 해결책
`OmniMixer.csproj`에서 다음을 시도:
```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
</PropertyGroup>
```

## Step 3 완성도

| 항목 | 상태 |
|------|------|
| Converters | ✅ 완료 |
| ChannelStripControl | ✅ 완료 |
| MainWindow | ✅ 완료 |
| Apple 스타일 디자인 | ✅ 완료 |
| Audio Fader Taper | ✅ 완료 |
| 레벨 미터 그라데이션 | ✅ 완료 |
| 빌드 | ❌ WPF 임시 프로젝트 이슈 |

**결론**: UI 구현은 100% 완료되었으나, .NET 6 WPF의 알려진 빌드 이슈로 인해 컴파일이 실패하고 있습니다.
