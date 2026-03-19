# OmniMixer

> **Windows용 다중 채널 오디오 라우팅 믹서**

[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## 개요

OmniMixer는 Windows PC에서 **단일 가상 오디오 스트림(VB-Cable)을 캡처**하여 **최대 8개의 물리적 출력 장치에 동시에 실시간 분배**하는 다채널 오디오 라우팅 믹서입니다.

각 출력 채널은 독립적인 볼륨, 팬, 음소거 제어가 가능하며, 실시간 레벨 미터링을 제공합니다.

### 주요 기능

- 🎛️ **8채널 동시 출력** - 독립적인 DSP 파이프라인 (Volume/Pan/Mute)
- 🎚️ **실시간 레벨 미터** - Peak/RMS 값 기반 30FPS 업데이트
- 🔄 **자동 포맷 변환** - 샘플레이트/비트뎁스 불일치 자동 리샘플링
- 🔌 **Hot-unplug 지원** - 장치 연결 해제 시 해당 채널만 격리 처리
- ⚡ **낮은 지연시간** - 50~150ms 수준의 왕복 지연

---

## ⚠️ 필수 요구사항: VB-Cable 설치

OmniMixer를 사용하려면 **VB-Audio Virtual Cable**이라는 가상 오디오 장치를 먼저 설치해야 합니다.

### 1단계: VB-Cable 다운로드 및 설치

1. [VB-Audio Software 공식 웹사이트](https://vb-audio.com/Cable/) 접속
2. **"Download VB-Cable"** 버튼 클릭
3. 다운로드된 ZIP 파일 압축 해제
4. **VBCABLE_Setup.exe** 실행 (관리자 권한 필요)
5. 설치 완료 후 **PC 재부팅** 권장

### 2단계: Windows 기본 출력 장치 설정

VB-Cable 설치 후, Windows에서 기본 출력 장치로 설정해야 합니다:

1. **Windows 설정** → **시스템** → **소리** 열기
2. **출력** 섹션에서 **"CABLE Input"** 선택
3. **"기본으로 설정"** 클릭

또는 빠른 방법 (시스템 트레이):
```
1. 작업 표시줄 오른쪽 하단의 스피커 아이콘 클릭
2. 오른쪽 화살표(▶) 클릭으로 출력 장치 선택 화면 열기
3. "CABLE Input" 선택
```

### 3단계: OmniMixer 실행

1. OmniMixer.exe 실행
2. 상단 **Input Device** 콤복박스에서 **"CABLE Output"** 선택
3. 각 채널의 **Output Selector**에서 실제 스피커/헤드폰 선택
4. **▶ START** 버튼 클릭

> 💡 **참고**: 이제 모든 Windows 소리는 VB-Cable로 전송되고, OmniMixer가 이를 분배하여 선택된 물리적 장치로 출력합니다.

---

## 🚀 빠른 시작

### 시스템 요구사항

- Windows 10 21H2 이상 (x64)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 설치
- VB-Audio Virtual Cable 설치

### 빌드 방법

#### 1) 개발 빌드 및 실행

```bash
# 리포지토리 클론
git clone https://github.com/devmcjo/OmniMixer.git
cd OmniMixer

# 디버그 빌드
dotnet build

# 실행 (VB-Cable이 설치되어 있어야 함)
dotnet run --project OmniMixer.csproj
```

#### 2) 배포용 빌드 (단일 실행 파일)

```bash
# 단일 파일 빌드 (.NET 8 런타임 포함)
dotnet publish OmniMixer.csproj -c Release

# 출력 경로
# bin/Static/Release/net8.0-windows/win-x64/publish/OmniMixer.exe
```

**배포 파일 특징:**
- 크기: 약 69MB (런타임 및 모든 종속성 포함)
- 실행 조건: .NET 8 Runtime 설치 불필요 (자체 포함)
- 단일 파일: `OmniMixer.exe` 하나만 배포하면 됨

### 다운로드

[Releases](https://github.com/devmcjo/OmniMixer/releases) 페이지에서 최신 버전을 다운로드할 수 있습니다.

---

## 🏗️ 아키텍처

```
[VB-Cable Output] → [WasapiCapture]
                           │
                           │ DataAvailable
                           ├──→ [BufferedWaveProvider #1] → [Channel DSP] → [WasapiOut #1] → 스피커 1
                           ├──→ [BufferedWaveProvider #2] → [Channel DSP] → [WasapiOut #2] → 스피커 2
                           │       ...
                           └──→ [BufferedWaveProvider #8] → [Channel DSP] → [WasapiOut #8] → 스피커 8
```

### 기술 스택

- **언어**: C# (.NET 8)
- **UI 프레임워크**: WPF (Windows Presentation Foundation)
- **오디오 라이브러리**: NAudio 2.2.1
- **MVVM Toolkit**: CommunityToolkit.Mvvm

---

## 📋 사용 가이드

### UI 구성

```
┌─────────────────────────────────────────────────────────────┐
│ [Input: CABLE Output ▼]                           OmniMixer │
├────────┬────────┬────────┬────────┬────────┬────────┬──────┤
│ CH 1   │ CH 2   │ CH 3   │ CH 4   │ CH 5   │ CH 6   │ ...  │
│[Out▼]  │[Out▼]  │[Out▼]  │[Out▼]  │[Out▼]  │[Out▼]  │      │
│        │        │        │        │        │        │      │
│ L─●─R  │ L─●─R  │        │        │        │        │      │
│ (Pan)  │ (Pan)  │        │        │        │        │      │
│        │        │        │        │        │        │      │
│ [▲][█] │ [▲][█] │        │        │        │        │      │
│ [│][│] │ [│][│] │ Volume │        │        │        │      │
│ [▼][ ] │ [▼][ ] │ Fader  │        │        │        │      │
│        │        │        │        │        │        │      │
│ [MUTE] │ [MUTE] │        │        │        │        │      │
├────────┴────────┴────────┴────────┴────────┴────────┴──────┤
│                    [ ▶ START ] / [ ■ STOP ]                 │
└─────────────────────────────────────────────────────────────┘
```

### 각 채널 컨트롤

| 컨트롤 | 기능 | 범위 |
|--------|------|------|
| **Output Selector** | 출력 장치 선택 | 시스템의 모든 출력 장치 |
| **Pan Slider** | 좌우 밸런스 | -1.0 (왼쪽) ~ +1.0 (오른쪽) |
| **Volume Fader** | 볼륨 조절 | -80 dB ~ +6 dB |
| **Level Meter** | 실시간 레벨 표시 | 0.0 ~ 1.0 (선형 진폭) |
| **Mute Button** | 음소거 토글 | On/Off |

---

## ⚙️ DSP 파이프라인

### Volume (dB → 선형)
```
Amplitude = 10^(dB/20)
```

### Equal-Power Panning
```
L_gain = cos(π/4 × (P + 1))
R_gain = sin(π/4 × (P + 1))

P = -1.0 → L=1.0, R=0.0  (완전 왼쪽)
P =  0.0 → L≈0.707, R≈0.707  (중앙)
P = +1.0 → L=0.0, R=1.0  (완전 오른쪽)
```

---

## 🔧 고급 설정

### 샘플레이트 통일 (권장)

Windows 소리 설정에서 모든 입출력 장치의 샘플레이트를 **48kHz**로 통일하면 리샘플러가 동작하지 않아 CPU 부하가 크게 줄어듭니다:

1. **Windows 설정** → **시스템** → **소리**
2. **출력** → 장치 선택 → **속성** → **고급**
3. **기본 형식**: "2채널, 24비트, 48000Hz" 선택
4. **입력** → "CABLE Output"에 대해서도 동일하게 설정

---

## 📝 개발 로드맵

| 단계 | 내용 | 상태 |
|------|------|------|
| **Step 1** | 핵심 오디오 파이프라인 아키텍처 | ✅ 완료 |
| **Step 2** | WPF 프로젝트 세팅 & ViewModel | ✅ 완료 |
| **Step 3** | WPF UI 구현 (채널 스트립 & 미터) | ✅ 완료 |
| **Step 4** | 통합 테스트 & 실장치 검증 | 📋 예정 |
| **Step 5** | 안정화, 예외 처리 & 폴리싱 | 📋 예정 |

---

## ⚠️ 알려진 제한사항

- **지연 시간**: 음악 감상, BGM 재생 등 실시간성이 극도로 요구되지 않는 환경에 최적화되어 있습니다. 마이크 모니터링, 리듬 게임 등 초저지연(< 20ms)이 필요한 작업에는 적합하지 않습니다. 설계 목표 지연은 **50 ~ 150ms** 수준입니다.

- **CPU 사용률**: 8채널 동시 리샘플링 시 CPU 부하가 증가할 수 있습니다. 샘플레이트를 통일하면 해결됩니다.

---

## 📄 라이선스

MIT License - 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

---

## 🙏 크레딧

- [NAudio](https://github.com/naudio/NAudio) - .NET 오디오 라이브러리
- [VB-Audio](https://vb-audio.com/) - Virtual Cable 제공
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM Toolkit

---

**OmniMixer** © 2026 devmcjo
