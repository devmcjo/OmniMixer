# OmniMixer Step 2 - MVVM 구조 구축 완료

> **Date**: 2026-03-03 | **Status**: Completed

## 완성된 작업 목록

### 1. AudioDeviceItem 모델 (`Models/AudioDeviceItem.cs`)
- MMDevice를 래핑하는 POCO 클래스
- IDisposable인 MMDevice 대신 UI에서 사용
- Equals/GetHashCode 재정의로 ComboBox 선택 관리 용이

### 2. ChannelViewModel (`ViewModels/ChannelViewModel.cs`)
- CommunityToolkit.Mvvm 사용 (ObservableObject, ObservableProperty)
- 8개 채널 인스턴스 (Index 0~7)
- **Dispatcher.Invoke**로 MeterUpdated → UI 스레드 마샬링
- PropertyChanged 이벤트로 ViewModel → DSP 양방향 바인딩
- Attach/DetachDspProvider로 AudioEngine과 동적 연결

### 3. MainViewModel (`ViewModels/MainViewModel.cs`)
- **AudioEngine 유일 소유자** (핵심 설계 원칙 준수)
- Start/Stop RelayCommand 구현
- ObservableCollection<AudioDeviceItem>으로 장치 목록 관리
- EngineError 이벤트 구독 → UI 알림
- VB-Cable 자동 감지 및 선택

### 4. MainWindow.xaml 업데이트
- DataContext = MainViewModel
- 입력 장치 ComboBox 바인딩
- 8개 채널 스트립 ItemsControl로 동적 생성
- 각 채널: 출력 장치 선택, Pan 슬라이더, Volume 슬라이더, 레벨 미터, Mute 버튼
- Start/Stop 버튼 Command 바인딩

## 주요 설계 결정

### MVVM 아키텍처
```
MainWindow (View)
    └── DataContext = MainViewModel
        ├── AudioEngine (유일 소유)
        ├── ObservableCollection<InputDevices>
        ├── ObservableCollection<OutputDevices>
        └── ChannelViewModel[8]
            └── AttachDspProvider(ChannelDspProvider)
                ├── Volume/Pan/IsMuted (양방향 바인딩)
                └── MeterUpdated → Dispatcher → MeterLevel
```

### 스레드 안전성
- **오디오 스레드** → MeterUpdated 이벤트 발생
- **Dispatcher.Invoke** → UI 스레드로 마샬링
- **UI 스레드** → ViewModel 프로퍼티 변경 → DSP 반영

## 빌드 결과
```
빌드 성공: 0 errors, 2 warnings (App.Main 중복 - 정상)
```

## 다음 단계 (Step 3)

### 예정 작업
- `ChannelStripControl.xaml` UserControl로 채널 스트립 분리
- 오디오 페이더 테이퍼 (dB 로그 컬브) 구현
- 레벨 미터 컬러 그레이디언트 (초록→노랑→빨강)
- Peak Hold 기능 구현
- UI 폴리싱 및 스타일링

### 개선 가능 사항
1. **레벨 미터 성능**: 현재 30FPS로 Dispatcher 호출. 높은 부하 시 조정 필요
2. **장치 변경 감지**: Windows 장치 변경 시 자동 목록 갱신 (현재는 수동 새로고침)
3. **슬라이더 민감도**: Volume/Pan 슬라이더의 단계 설정 (현재는 연속값)

---

**Step 2 완료로 MVVM 기반 WPF 애플리케이션 뼈대가 구축되었습니다.**
