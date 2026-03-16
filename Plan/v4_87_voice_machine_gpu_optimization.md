# v4.87 Voice State Machine 개선 & GPU/시작 스크립트 최적화

**작업일**: 2026-03-17
**버전**: v4.87.1
**작업자**: Claude Opus 4.6

---

## 1. XState v5 Voice State Machine 개선

### 1.1 WS_DISCONNECT 전체 상태 처리

**문제**: WebSocket 연결 끊김(`WS_DISCONNECT`) 이벤트가 `listening` 상태에서만 처리되어, 다른 상태에서 연결이 끊기면 상태 머신이 고착됨.

**수정 파일**: `src/Client/FabCopilot.WebClient/wwwroot/js/voice-machine.js`

| 상태 | WS_DISCONNECT 처리 |
|------|-------------------|
| `idle` | `voiceMode=false`, `exitVoiceMode` action |
| `listening` | (기존) → `stopping` 전이 |
| `processing` | → `stopping` 전이 (신규) |
| `responding` | → `stopping` 전이 (신규) |
| `recovering` | → `idle` 전이, `voiceMode=false`, `retryCount=0` (신규) |
| `stopping` | → `idle` 전이, `voiceMode=false` (신규) |

### 1.2 AUTO_TTS_OFF/ON in responding 상태

**문제**: TTS 재생 중 Auto TTS를 끄면 응답 완료 후 idle로 돌아가지 않음.

**해결**: `responding` 상태에 `AUTO_TTS_OFF` / `AUTO_TTS_ON` 이벤트 추가.

- `AUTO_TTS_OFF`: `ttsEnabled=false`, `ttsDone=true` 설정 → `isResponseComplete` guard가 `screenDone && ttsDone`로 판정 → idle 전이
- `AUTO_TTS_ON`: `ttsEnabled=true` 재활성화

### 1.3 Q_end Race Condition 수정

**파일**: `src/Client/voice-panel/src/audio/recorder.ts`

**문제**: WebSpeech `recognition.stop()` 호출 시 `onerror('aborted')` 이벤트가 발생하여, 이미 `MIC.FINAL`로 Q_end를 보낸 후에도 `MIC.ERROR`가 발행됨.

**해결**: `_qendTriggered` 플래그 추가
- Q_end 판정 시 `_qendTriggered = true` 설정 후 `recognition.stop()` 호출
- `onerror` 핸들러에서 `_qendTriggered && error === 'aborted'` 이면 suppress

### 1.4 FormData 필드명 통일

**파일**: `src/Client/voice-panel/src/audio/recorder.ts` (line 665)

- **Before**: interim transcription에서 `form.append('file', blob, 'interim.webm')`
- **After**: `form.append('audio', blob, 'interim.webm')` — final과 동일하게 통일

### 1.5 테스트 추가

**파일**: `src/Client/FabCopilot.WebClient/tests/voice-machine.test.js`

11개 신규 테스트 추가:
- WS_DISCONNECT handling across all states (6개)
- AUTO_TTS toggle during responding (5개)

---

## 2. XState Visualizer 파일

**파일**: `src/Client/voice-panel/src/machine/voiceMachine.visualizer.js`

- XState **v4 문법**으로 작성 (https://stately.ai/viz 호환)
- `guard:` → `cond:`, `({ context })` → `(ctx)` 등 v4 변환
- 모든 상태/전이에 한국어 `description` 추가
- WS_DISCONNECT, AUTO_TTS_OFF/ON 등 개선사항 반영

---

## 3. TTS 정지 버튼 (입력 영역)

**파일**: `src/Client/FabCopilot.WebClient/Pages/Index.razor`

채팅 입력 영역(send 버튼 옆)에 TTS 정지 버튼 추가:
- TTS 재생 중(`_ttsStreamingActive` 또는 `_ttsPlayingIdx >= 0`)일 때만 표시
- 빨간색 정지 아이콘 (■)
- `StopTtsFromInput()` 메서드: streaming TTS 중지 + 재생 상태 초기화

**CSS**: `src/Client/FabCopilot.WebClient/wwwroot/css/app.css`
- `.tts-stop-btn` 스타일 추가 (red #dc2626, hover #b91c1c)

---

## 4. GPU VRAM 최적화

### 4.1 문제 분석

**환경**: RTX 4060 Ti 16GB

| 모델 | VRAM 사용 |
|------|----------|
| exaone3.5:7.8b (Chat) | ~5.4 GB |
| snowflake-arctic-embed2 (Embedding) | ~1.2 GB |
| faster-whisper-large-v3 (STT) | ~5.0 GB |
| Windows GUI | ~3.5 GB |

### 4.2 발견된 문제

**문제 1: RAG Entity Extraction이 GPU 독점**
- `ExtractEntitiesOnIngest: true` + `ScanOnStartup: true`
- 서비스 시작 시 12개 문서 × 수십 chunk에 대해 LLM `/api/chat` 호출
- 각 호출 22~39초 → 총 45회+ → **20분 이상 GPU 독점**
- 이 동안 사용자 LLM 요청이 큐잉되어 25~30초 대기

**문제 2: 모델 Swap으로 인한 지연**
- `OLLAMA_MAX_LOADED_MODELS=1`이라 embedding ↔ chat 모델 전환 시 매번 VRAM에서 unload/reload
- 모델 swap 1회당 3~5초 소요
- RAG 검색 시 embedding → chat → embedding → chat 반복으로 지속적 swap 발생

**문제 3: Whisper 영구 로드**
- `WHISPER__MODEL_TTL=-1` (무기한 로드)
- Whisper ~5GB가 항상 VRAM 점유 → LLM 모델과 경쟁
- (이전 세션에서 `WHISPER__MODEL_TTL=120`으로 수정 완료)

### 4.3 수정 내용

| 파일 | 변경 | 효과 |
|------|------|------|
| `docker-compose.yml` | `OLLAMA_MAX_LOADED_MODELS=1` → `2` | chat+embedding 동시 VRAM 유지 (6.6GB) |
| `RagService/appsettings.json` | `ExtractEntitiesOnIngest: true` → `false` | 시작 시 GPU 독점 제거 |

### 4.4 성능 결과

| 시나리오 | Before | After |
|----------|--------|-------|
| Entity extraction 진행 중 | 25~30초 (큐잉) | 0초 (비활성) |
| Embedding 후 chat (모델 swap) | 4.7~8.9초 | 0.15초 |
| Warm chat 응답 | - | 0.15초 |
| VRAM (chat+embed 로드) | - | 9.7GB / 16.4GB (여유 40%) |

---

## 5. 시작 스크립트 최적화

**파일**: `start-all-services.ps1`

### 5.1 `--build` 기본값 제거

- **Before**: 매번 10개 서비스 Docker 이미지 재빌드 (30~120초+)
- **After**: 캐시된 이미지 사용이 기본, `-Build` 플래그로 명시적 빌드

```powershell
# 빠른 시작 (기본)
.\start-all-services.ps1

# 코드 변경 후 빌드
.\start-all-services.ps1 -Build
```

### 5.2 고정 Sleep → Polling 전환

`Wait-ForPorts` 함수 추가: 1초 간격으로 포트 체크, 준비되면 즉시 통과.

| 구간 | Before | After |
|------|--------|-------|
| Phase 1 (인프라 대기) | 고정 5초 + 실패 시 10초 | 1초 간격 polling (최대 30초) |
| Phase 3 (Cloudflare) | 고정 3초 | 1초 |
| Phase 4 (Health Check) | 고정 8초 + 단발 체크 | 1초 간격 polling (최대 60초) |

### 5.3 예상 시작 시간

| 시나리오 | Before | After |
|----------|--------|-------|
| 코드 변경 없는 재시작 | 1~3분 | **15~25초** |
| 코드 변경 후 빌드 재시작 | 1~3분 | 1~3분 (`-Build`) |

---

## 6. 수정 파일 전체 목록

### VisualFactoryHome (루트)

| 파일 | 변경 내용 |
|------|----------|
| `docker-compose.yml` | `OLLAMA_MAX_LOADED_MODELS=2`, `WHISPER__MODEL_TTL=120` |
| `start-all-services.ps1` | `--build` 기본값 제거, Sleep→Polling, `-Build` 플래그 |

### LLM-Chat-Service

| 파일 | 변경 내용 |
|------|----------|
| `wwwroot/js/voice-machine.js` | WS_DISCONNECT 전체 상태, AUTO_TTS 토글, 기타 개선 |
| `Pages/Index.razor` | TTS 정지 버튼 (입력 영역), voice loop 관련 수정 |
| `wwwroot/css/app.css` | `.tts-stop-btn` 스타일 |
| `Pages/_Layout.cshtml` | voice-panel 통합 관련 수정 |
| `tests/voice-machine.test.js` | 11개 신규 테스트 |
| `RagService/appsettings.json` | `ExtractEntitiesOnIngest: false` |
| `ChatGateway/appsettings.json` | Whisper/TTS 설정 업데이트 |
| `ChatGateway/Program.cs` | Whisper/TTS 서비스 등록 수정 |
| `Dockerfile` | 빌드 타겟 업데이트 |
| `Pages/ServerSettings.razor` | 설정 페이지 업데이트 |

### 신규 파일

| 파일 | 설명 |
|------|------|
| `voice-panel/src/machine/voiceMachine.visualizer.js` | XState v4 Visualizer용 |
| `Plan/v4_87_voice_machine_gpu_optimization.md` | 본 문서 |
