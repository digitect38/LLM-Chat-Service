# 음성 입출력 상태 머신 (Voice I/O State Machine)

> FabWise WebClient 음성 대화 기능의 상태 관리 문서
> 대상 파일: `Index.razor`, `_Layout.cshtml`

---

## 1. 주요 상태 변수

| 변수 | 타입 | 설명 |
|---|---|---|
| `_autoTtsEnabled` | `bool` | Auto TTS 토글 (localStorage 저장) |
| `_isVoiceMode` | `bool` | 음성 대화 루프 활성 (STT→질문→TTS→반복) |
| `_voicePhase` | `VoicePhase` | 음성 모드 내 세부 단계 |
| `_isRecording` | `bool` | 마이크 녹음 중 |
| `_isTranscribing` | `bool` | STT 변환 중 (Whisper 업로드) |
| `_ttsPlayingIdx` | `int` | TTS 재생 중인 메시지 인덱스 (`-1` = 없음) |
| `_ttsStreamingActive` | `bool` | 스트리밍 TTS 진행 중 |
| `_ttsSendConfirmVisible` | `bool` | TTS 중 새 질문 확인 배너 표시 |

---

## 2. VoicePhase 열거형

```csharp
private enum VoicePhase { Idle, Listening, Transcribing, Waiting, Speaking }
```

| Phase | 의미 | UI 표시 |
|---|---|---|
| `Idle` | 비활성 | 상태 바 숨김 |
| `Listening` | 마이크 대기/녹음 중 | 녹색 맥동 아이콘 + "듣고 있습니다..." |
| `Transcribing` | STT 텍스트 변환 중 | 처리 중 애니메이션 |
| `Waiting` | AI 응답 대기 | 처리 중 애니메이션 + "응답 생성 중..." |
| `Speaking` | TTS 응답 재생 중 | 파란 스피커 아이콘 + "응답 읽는 중..." |

---

## 3. 전체 상태 전이 다이어그램

### 3-1. Voice Mode (음성 대화 루프)

`_autoTtsEnabled = true`, `_isVoiceMode = true` 일 때의 핸즈프리 루프.

```mermaid
stateDiagram-v2
    [*] --> Idle

    Idle --> Listening : 마이크 버튼 (autoTts ON)\nsetVoiceMode(true)

    Listening --> Listening : interim 결과 수신\n_interimText 업데이트
    Listening --> Listening : STT 완료 (텍스트 없음)\n자동 재시작
    Listening --> Listening : STT 에러 (비치명적)\n자동 재시작
    Listening --> Transcribing : STT 완료 (텍스트 있음)
    Listening --> Idle : STT 에러 (치명적)\nExitVoiceModeAsync

    Transcribing --> Waiting : VoiceAutoSendAsync(text)

    Waiting --> Speaking : AI 응답 스트림 시작\nplayStream()

    Speaking --> Listening : TTS 완료 (OnTtsEnded)\nVoiceStartListeningAsync
    Speaking --> Listening : Barge-in 감지 (OnBargeIn)\nTTS 중지 → 재시작
    Speaking --> Listening : TTS 에러 (OnTtsError)\n재시작
    Speaking --> Listening : 사용자 수동 TTS 중지

    Listening --> Idle : Auto TTS OFF\nExitVoiceModeAsync
    Transcribing --> Idle : Auto TTS OFF\nExitVoiceModeAsync
    Waiting --> Idle : Auto TTS OFF\nExitVoiceModeAsync
    Speaking --> Idle : Auto TTS OFF\nExitVoiceModeAsync
    Listening --> Idle : WebSocket 끊김\nExitVoiceModeAsync
    Speaking --> Idle : WebSocket 끊김\nExitVoiceModeAsync
```

### 3-2. Non-Voice Mode (일반 채팅 + Auto TTS)

`_autoTtsEnabled = true`, `_isVoiceMode = false` 일 때. 텍스트 입력 후 응답을 TTS로 읽어줌.

```mermaid
stateDiagram-v2
    [*] --> 대기

    대기 --> 녹음중 : 마이크 버튼\naudioRecorder.start()
    녹음중 --> STT변환 : 녹음 중지 (Whisper)
    녹음중 --> 대기 : STT 완료\nUserMessage에 텍스트 입력
    STT변환 --> 대기 : 변환 완료\nUserMessage에 텍스트 입력

    대기 --> AI응답대기 : SendMessageAsync
    AI응답대기 --> TTS재생 : 응답 완료\nautoTts → playStream()

    TTS재생 --> 대기 : TTS 완료 (OnTtsEnded)
    TTS재생 --> 대기 : Barge-in (OnBargeIn)\nTTS 중지
    TTS재생 --> 확인배너 : 새 질문 전송 시도

    확인배너 --> AI응답대기 : "끊고 새 질문"\nTTS 중지 → 전송
    확인배너 --> TTS재생_전송 : "계속 듣기"\nTTS 유지 + 전송
    확인배너 --> TTS재생 : "취소"\n배너 닫기

    TTS재생_전송 --> 대기 : TTS 완료 후 응답 대기
```

### 3-3. 일회성 녹음 (Auto TTS OFF)

`_autoTtsEnabled = false` 일 때. 수동 마이크 버튼으로 STT만 사용.

```mermaid
stateDiagram-v2
    [*] --> 대기

    대기 --> 녹음중 : 마이크 버튼\naudioRecorder.start()

    녹음중 --> 녹음중 : interim 결과\n_interimText 업데이트
    녹음중 --> STT변환 : 녹음 중지 (Whisper 엔진)
    녹음중 --> 대기 : WebSpeech 완료\nUserMessage 설정

    STT변환 --> 대기 : 변환 완료\nUserMessage 설정
    STT변환 --> 대기 : 변환 에러

    대기 --> 마이크버튼정지 : 마이크 버튼 (녹음중)\naudioRecorder.stop()
    마이크버튼정지 --> 대기 : 완료
```

---

## 4. JS 레이어: Barge-in 감지 시스템

TTS 재생 중 사용자 음성을 감지하여 TTS를 중단하는 메커니즘.

```mermaid
stateDiagram-v2
    [*] --> 비활성

    비활성 --> 마이크준비 : prepareMic()\n(Auto TTS 토글 시)
    마이크준비 --> 감지중 : startBargeIn()\n(TTS 재생 시작)

    감지중 --> 감지중 : RMS ≤ 0.02\n무음 → 타이머 리셋
    감지중 --> 음성감지 : RMS > 0.02\n타이머 시작

    음성감지 --> 감지중 : 300ms 미만 → 리셋
    음성감지 --> TTS중지 : 300ms 이상 지속\nfabTts.stop()

    TTS중지 --> 비활성 : OnBargeIn → Blazor 호출

    감지중 --> 비활성 : stopBargeIn()\n(TTS 종료/중지 시)
```

### Barge-in 구성 요소

| 구성요소 | 역할 |
|---|---|
| `fabTts.prepareMic()` | Auto TTS 토글 시 마이크 사전 획득 (user gesture) |
| `fabTts.startBargeIn()` | TTS 시작 시 마이크 RMS 분석 시작 |
| `fabTts._bargeInAnalyser` | Web Audio AnalyserNode — 실시간 음량 측정 |
| RMS > 0.02 (300ms 지속) | `fabTts.stop()` + `OnBargeIn()` Blazor 호출 |
| `fabTts.stopBargeIn()` | TTS 종료 시 감지기 정리 |
| `fabTts.releaseMic()` | Auto TTS OFF 시 마이크 해제 |

### 마이크 스트림 우선순위

```
persistentStream (Voice Mode) > bargeInMicStream (Auto TTS) > 없음
```

---

## 5. STT 엔진 선택 흐름

```mermaid
flowchart TD
    A[앱 시작] --> B{설정값 로드<br/>sttEngine}
    B -->|auto| C{Whisper 서버<br/>헬스체크}
    B -->|whisper| D[Whisper 강제]
    B -->|webspeech| E[WebSpeech 강제]

    C -->|OK| F[Whisper 사용]
    C -->|실패| G{WebSpeech API<br/>지원?}
    G -->|지원| H[WebSpeech 사용]
    G -->|미지원| I[STT 비활성화]

    D --> J{서버 연결?}
    J -->|OK| F
    J -->|실패| K[에러 표시 + 비활성화]
```

---

## 6. TTS 재생 엔진 폴백 흐름

```mermaid
flowchart TD
    A[TTS 재생 요청] --> B{AudioContext<br/>상태?}
    B -->|suspended| C[ctx.resume()]
    B -->|running| D[서버 TTS 요청]
    C --> D

    D --> E{HTTP 응답?}
    E -->|200 + audio| F[Web Audio 재생<br/>+ Barge-in 시작]
    E -->|200 + JSON<br/>engine=Browser| G[SpeechSynthesis 재생<br/>+ Barge-in 시작]
    E -->|에러/타임아웃| H[SpeechSynthesis 폴백<br/>+ Barge-in 시작]

    F --> I[재생 완료]
    G --> I
    H --> I
    I --> J[OnTtsEnded → Blazor]
```

---

## 7. TTS 중 새 질문 확인 흐름

```mermaid
flowchart TD
    A[사용자 메시지 전송] --> B{TTS 재생 중?}
    B -->|아니오| C[SendMessageAsync 진행]
    B -->|예| D[확인 배너 표시]

    D --> E{사용자 선택}
    E -->|끊고 새 질문| F[fabTts.stop<br/>→ SendMessageAsync]
    E -->|계속 듣기| G[TTS 유지<br/>→ SendMessageAsync]
    E -->|취소| H[배너 닫기<br/>메시지 복원]
```

---

## 8. 에러 처리 정책

### STT 에러

| 에러 유형 | 분류 | 처리 |
|---|---|---|
| `aborted` | 정상 | 무시 (사용자 수동 중지) |
| `no-speech` | 비치명적 | Voice Mode: 자동 재시작 / 일반: 무시 |
| `network` | 비치명적 | Voice Mode: 자동 재시작 / 일반: 무시 |
| `not-allowed` | 치명적 | UI 에러 표시 |
| 서버 연결 실패 | 치명적 | UI 에러 표시 + Voice Mode 종료 |

### TTS 에러

| 에러 유형 | 처리 |
|---|---|
| 서버 TTS 실패 | SpeechSynthesis 폴백 |
| SpeechSynthesis 실패 | `OnTtsError` → Voice Mode: 재시작 / 일반: 무시 |
| AudioContext suspended | `resume()` 시도 → 실패 시 폴백 |

---

## 9. 음성 명령 처리 ("멈춰")

TTS 재생 중 WebSpeech API를 백그라운드로 실행하여 음성 명령을 인식한다.
기존 RMS barge-in과 병행 운영된다.

### 지원 명령어

| 명령어 | 동작 |
|---|---|
| `멈춰`, `멈춰라` | TTS 즉시 중지 |
| `그만`, `그만해` | TTS 즉시 중지 |
| `중지`, `정지` | TTS 즉시 중지 |
| `스톱`, `스탑`, `stop` | TTS 즉시 중지 |

### 동작 흐름

```mermaid
flowchart TD
    A[TTS 재생 시작] --> B[startBargeIn]
    B --> C[RMS 음량 감지기 시작]
    B --> D[WebSpeech 명령 리스너 시작]

    C --> E{RMS > 0.02<br/>300ms 지속?}
    E -->|아니오| C
    E -->|예| F{명령 감지됨?}
    F -->|예| G[무시 — 명령 리스너가 처리]
    F -->|아니오| H[OnBargeIn<br/>TTS 중지 + 녹음 시작]

    D --> I{음성 인식 결과}
    I -->|명령어 키워드 매칭<br/>15자 이내| J[OnVoiceCommand 'stop'<br/>TTS 즉시 중지]
    I -->|인식 실패/무음| D
    I -->|TTS 에코 15자 초과| D
```

### OnBargeIn vs OnVoiceCommand 차이

| 항목 | `OnBargeIn` (RMS) | `OnVoiceCommand` (WebSpeech) |
|---|---|---|
| 트리거 | 아무 소리 300ms 지속 | 특정 명령어 인식 |
| TTS 중지 | O | O |
| Voice Mode → 녹음 재시작 | O (사용자가 말하는 중이므로) | X (명령일 뿐, Listening 대기) |
| Non-Voice Mode | TTS 중지 | TTS 중지 |
| 에코 필터 | 없음 | 15자 초과 무시 |

### 구현 세부사항

- **JS**: `fabTts._startCommandListener()` — TTS 시작 시 WebSpeech `continuous` 모드로 인식 시작
- **JS**: `fabTts._STOP_COMMANDS` — 명령어 키워드 배열
- **JS**: 에코 방지 — 인식 텍스트가 15자 초과이면 TTS 에코로 간주하고 무시
- **JS**: `_commandDetected` 플래그 — 명령 감지 시 RMS barge-in이 `OnBargeIn`을 중복 호출하지 않도록 차단
- **C#**: `OnVoiceCommand("stop")` — `_ttsPlayingIdx`/`_ttsStreamingActive` 리셋, Voice Mode에서 `Listening` 대기
- **폴백**: WebSpeech API 미지원 브라우저에서는 기존 RMS 전용 barge-in으로 동작
