# CLAUDE.md

이 파일은 Claude Code (claude.ai/code)가 이 저장소의 코드를 작업할 때 참고하는 가이드입니다.

## 프로젝트 개요

OmniMixer는 Windows WPF 애플리케이션으로, 단일 가상 오디오 스트림(VB-Cable)을 최대 8개의 물리적 출력 장치에 동시에 라우팅하며, 각 채널마다 독립적인 볼륨, 팬, 음소거 제어를 제공합니다.

- **언어**: C# (.NET 8)
- **UI 프레임워크**: WPF (Windows Presentation Foundation)
- **오디오 라이브러리**: NAudio 2.2.1
- **MVVM 툴킷**: CommunityToolkit.Mvvm 8.2.2

## 빌드 명령어

```bash
# 프로젝트 빌드
dotnet build

# 애플리케이션 실행 (VB-Cable 설치 필요)
dotnet run --project OmniMixer.csproj

# 릴리즈 빌드 (단일 파일 자체 포함 실행 파일)
dotnet publish -c Release
# 출력 경로: bin/Static/Release/OmniMixer.exe
```

## 아키텍처 개요

### 데이터 흐름 아키텍처

```
[VB-Cable Output] → [WasapiCapture]
                           │
                           │ DataAvailable (캡처 스레드)
                           ├──→ [BufferedWaveProvider #1] → [ChannelDspProvider] → [WasapiOut #1] → 스피커 1
                           ├──→ [BufferedWaveProvider #2] → [ChannelDspProvider] → [WasapiOut #2] → 스피커 2
                           │       ...
                           └──→ [BufferedWaveProvider #8] → [ChannelDspProvider] → [WasapiOut #8] → 스피커 8
```

### 스레드 모델

- **캡처 스레드**: `WasapiCapture`가 `DataAvailable` 이벤트를 발생시켜 오디오를 8개 채널 버퍼로 라우팅
- **재생 스레드**: 각 `WasapiOut`이 독립적으로 실행되며 자신의 `ChannelDspProvider`에서 읽음
- **UI 스레드**: `MeterUpdated` 이벤트가 `Dispatcher`를 통해 UI 스레드로 마샬링되어 레벨 미터 업데이트

### 핵심 설계 패턴

**1-Writer N-Independent-Buffers 패턴**

핵심 오디오 라우팅은 락프리 아키텍처를 사용합니다:
- 단일 작성자(캡처 스레드)가 동일한 데이터를 8개의 `BufferedWaveProvider` 인스턴스에 쓰기
- 각 버퍼는 하나의 재생 스레드만 소비
- 채널 간 공유 상태 없음 → 데드락 없음, 핫언플러그 안전

**DSP 파이프라인 (채널당)**

```
입력 (float) → [볼륨: 10^(dB/20)] → [팬: Equal-Power] → [음소거] → 출력
```

- 볼륨: -80 dB ~ +6 dB 범위
- 팬: `cos(π/4 × (P + 1))` / `sin(π/4 × (P + 1))`를 사용한 등파워 팬닝
- 레벨 미터링: `ChannelDspProvider.Read()`에서 Peak/RMS 계산

### 프로젝트 구조

```
Audio/
  AudioEngine.cs         # 최상위 오케스트레이터, 장치 열거, 라우팅
  OutputChannel.cs       # 채널당 라이프사이클 관리 (버퍼 → DSP → 출력)
  ChannelDspProvider.cs  # DSP 처리 및 미터링
  ChannelSettings.cs     # 설정 DTO
  MeteringEventArgs.cs   # 레벨 미터 이벤트 데이터

ViewModels/
  MainViewModel.cs       # AudioEngine 소유, Start/Stop 관리
  ChannelViewModel.cs    # 채널당 바인딩, 미터 업데이트

Controls/
  ChannelStripControl.xaml/.cs  # 커스텀 채널 UI (팬/볼륨/음소거/미터)

Converters/
  # WPF 바인딩용 값 변환기 (dB ↔ 슬라이더, 레벨 → 높이/색상)

Models/
  AudioDeviceItem.cs     # MMDevice 래퍼

AudioEngineTest/
  Program.cs             # 콘솔 테스트 앱 (Step 1 검증)
```

## 핵심 클래스 및 역할

**AudioEngine** (`Audio/AudioEngine.cs:24`)
- NAudio의 `MMDeviceEnumerator`를 통해 캡처/출력 장치 열거
- `WasapiCapture` 라이프사이클 관리
- 캡처에서 8개의 `OutputChannel` 버퍼로 오디오 라우팅
- 포맷 변환 처리 (PCM 16/24/32 → IEEE Float)

**OutputChannel** (`Audio/OutputChannel.cs`)
- `BufferedWaveProvider`, `ChannelDspProvider`, `WasapiOut` 소유
- 채널당 초기화 및 핫언플러그 오류 처리
- 다른 채널과 격리되어 있음 (한 채널의 실패가 다른 채널에 영향 없음)

**ChannelDspProvider** (`Audio/ChannelDspProvider.cs`)
- `IWaveProvider` 구현, `Read()` 호출에서 오디오 처리
- 볼륨(dB→선형), 팬(등파워), 음소거 적용
- Peak/RMS 레벨 계산 및 `MeterUpdated` 이벤트 발생

**MainViewModel** (`ViewModels/MainViewModel.cs:16`)
- `AudioEngine` 인스턴스의 유일한 소유자
- `IsTransitioning` 가드를 사용한 Start/Stop 명령 관리
- 시작 시 VB-Cable 장치 자동 선택

**ChannelViewModel** (`ViewModels/ChannelViewModel.cs:15`)
- `ChannelDspProvider`와 양방향 바인딩 (볼륨/팬/음소거)
- `MeterUpdated` 이벤트 수신 및 `Dispatcher`를 통한 UI 업데이트
- VolumeDb(-80 ~ +6) 및 Pan(-1.0 ~ +1.0) 범위 검증

## 중요한 구현 세부사항

**포맷 변환**

캡처된 오디오는 낮부 처리를 위해 IEEE Float 32-bit 스테레오로 변환됩니다 (`AudioEngine.cs:163-165`). 지원되는 입력 포맷:
- IEEE Float 32-bit (제로카피)
- PCM 16-bit (`÷ 32768f`)
- PCM 24-bit (3바이트 Little-Endian 파싱)
- PCM 32-bit int (`÷ 2147483648f`)

**레이스 컨디션 방지**

`OnDataAvailable`는 `channel.Buffer`에 접근하기 전에 `channel.IsActive`를 확인합니다 (P1 수정, `AudioEngine.cs:297-301`). 버퍼는 핫언플러그 중 다른 스레드에 의해 null로 설정될 수 있습니다.

**CS7022 억제**

프로젝트는 경고 CS7022를 억제합니다 (WPF가 `Main()`이 있는 `App.g.cs`를 생성함). 이는 .NET 6+ WPF 프로젝트의 표준입니다 (`OmniMixer.csproj:19`).

## 외부 의존성: VB-Cable

애플리케이션은 **VB-Audio Virtual Cable** 설치가 필요합니다:
1. https://vb-audio.com/Cable/ 에서 다운로드
2. `VBCABLE_Setup.exe` 설치 (관리자 권한 필요, 재부팅 권장)
3. Windows 기본 출력을 "CABLE Input"으로 설정
4. OmniMixer에서 입력 장치로 "CABLE Output" 선택

## 핫언플러그 처리

출력 장치가 연결 해제되면:
1. `WasapiOut` 재생이 예외와 함께 중지됨
2. `OutputChannel.OnPlaybackStopped`이 리소스 정리
3. 오류가 `ChannelError` → `EngineError` 이벤트를 통해 상위로 전파됨
4. 다른 채널은 영향 없이 계속 동작
