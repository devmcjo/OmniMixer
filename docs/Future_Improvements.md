# Future Improvements / 수정 필요 사항

> 이 문서는 현재 코드에서 개선이 필요하지만 즉시 수정하지 않기로 결정한 항목들을 기록합니다.
> 각 항목은 문제 설명, 영향, 권장 수정 방향을 포함합니다.

---

## 1. 과도한 디버그 로깅 (AudioLogger)

**관련 파일**: `Audio/AudioEngine.cs`, `Audio/ChannelDspProvider.cs`, `Audio/OutputChannel.cs`

### 현재 상태
- `AudioLogger.LogDebug()`가 **매 캡처 이벤트마다** 호출됨 (실시간 오디오 처리 스레드)
- 매 샘플 읽기/쓰기마다 디스크에 로그 기록

### 영향
- 디스크 I/O로 인한 성능 저하
- 오디오 끊김/지연의 원인이 될 수 있음
- 프로덕션 코드에 디버그 로그가 과도하게 남아 있음

### 권장 수정 방향
```csharp
// 방법 1: 조걸부 컴파일 사용
#if DEBUG_AUDIO
AudioLogger.LogDebug($"[CAPTURE#{_captureEventCount}] ...");
#endif

// 방법 2: 로깅 레벨 제어
AudioLogger.SetMinLevel(LogLevel.Warning); // Release 빌드에서 Warning 이상만
```

### 우선순위: 중간
- 프로덕션 배포 전에 반드시 개선 필요

---

## 2. 버퍼 설정 변경 ✅ 완료

**관련 파일**: `Audio/OutputChannel.cs:91`

### 변경 내역
- ~~2000ms → 500ms로 조정~~ (2026-03-19 완료)

---

## 3. useEventSync: false 변경

**관련 파일**: `Audio/OutputChannel.cs:114`

### 현재 상태
```csharp
_wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared,
    useEventSync: false, latency: 200);  // 원래: true
```

### 영향
- **폴링 모드**로 전환되어 CPU 사용률 증가 가능
- 이벤트 기반 동기화(`true`)가 성능상 유리함

### 권장 수정 방향
1. `useEventSync: true`로 복원하여 테스트
2. 실제 CPU 사용량 비교 (true vs false)
3. 호환성 문제가 없다면 `true` 유지

### 우선순위: 낮음
- 현재 동작에 문제가 없다면 그대로 유지 가능
- 성능 최적화 단계에서 검토

---

## 4. ResamplerQuality: 60 (최고 품질)

**관련 파일**: `Audio/OutputChannel.cs:133`

### 현재 상태
```csharp
ResamplerQuality = 60 // 기본값: 35 → 변경: 60 (최고 품질)
```

### 영향
- 최고 품질은 CPU 부하 증가
- 불필요하게 높은 품질 설정일 수 있음

### 권장 수정 방향
1. 중간 품질(35-45)에서 시작
2. 실제 CPU 사용량 측정
3. 청각적으로 차이가 없는 최저 품질 값 찾기

### 우선순위: 낮음
- 현재 CPU 사용량이 문제 없다면 유지 가능

---

## 5. DiscardOnBufferOverflow: false

**관련 파일**: `Audio/OutputChannel.cs:95`

### 현재 상태
```csharp
DiscardOnBufferOverflow = false  // 원래: true
```

### 영향
- **오버플로우 시 오래된 데이터 보존** → 새 데이터 손실 가능
- 오디오 싱크 문제 발생 가능
- 일반적으로 오디오 애플리케이션은 `true` 사용 (드롭이 Tick/Pop보다 나음)

### 권장 수정 방향
```csharp
DiscardOnBufferOverflow = true  // 오버플로우 시 오래된 데이터 드롭
```

### 우선순위: 중간
- 오디오 끊김/노이즈 발생 시 체크할 항목

---

## 수정 계획 요약

| 항목 | 우선순위 | 예상 작업 시간 | 비고 |
|------|---------|---------------|------|
| 1. 디버그 로깅 최적화 | 중간 | 30분 | 프로덕션 배포 전 필수 |
| 2. 버퍼 크기 조정 | ✅ 완료 | - | 500ms로 설정됨 |
| 3. useEventSync 검토 | 낮음 | 1시간 | 성능 테스트 필요 |
| 4. ResamplerQuality 튜닝 | 낮음 | 30분 | CPU 프로파일링 필요 |
| 5. DiscardOnBufferOverflow 복원 | 중간 | 10분 | 오디오 품질 관련 |

---

*마지막 업데이트: 2026-03-19*
