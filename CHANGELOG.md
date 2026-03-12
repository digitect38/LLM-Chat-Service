# Fab Copilot Changelog

## v4.86.0 — Streaming Token Ordering & TTS Init Race Fix (2026-03-13)

### 문제 현상
채팅 응답 텍스트가 깨져 보이는(garbled) 현상 발생. 토큰이 뒤섞여 "## 요약"이 텍스트 중간에
나타나거나, 응답 앞부분이 잘려 나오는 증상. 중간에 정지(stop)하지 않아도 첫 질문부터 발생.
Redis에 저장된 대화 기록은 정상 — 새로고침 후 기록 열면 정상 텍스트 확인됨.

### 근본 원인 분석

**파일**: `Index.razor` — `HandleChunkReceived` 메서드

Blazor Server의 `InvokeAsync` 콜백이 `await` 지점에서 양보(yield)할 때, 후속 콜백이
먼저 실행될 수 있음. 기존 코드에서는 TTS 초기화(`await JS.InvokeVoidAsync("fabTts.playStream")`)가
**토큰 append 전에** 실행되어, `await`로 양보하는 동안 후속 chunk 콜백이 먼저 토큰을 append하면서
순서가 뒤바뀜.

```
[기존 흐름 — 토큰 순서 깨짐 가능]
chunk 도착 → guard 검사 → TTS init (await — yield!) → 토큰 append

[수정 흐름 — 토큰 순서 보장]
chunk 도착 → guard 검사 → 토큰 append (동기) → TTS init (await — yield OK)
```

### 변경 사항

#### 1. 토큰 순서 보장 (Token Ordering Fix)

**파일**: `Index.razor` — `HandleChunkReceived`

- **핵심 원칙**: 모든 상태 변경(토큰 append, IsComplete 처리, Error 처리)은 `await` 이전에
  동기적으로 실행
- TTS 초기화/피드, 콘솔 로깅 등 비동기 작업은 토큰이 안전하게 저장된 후 실행
- 불필요한 진단용 `await JS.InvokeVoidAsync("console.log", ...)` 제거 (hot path에서의
  불필요한 yield 방지)
- stale chunk에 대한 `console.warn` 로깅도 불필요한 await 제거 (silent return)

```csharp
// 변경 전: TTS init이 토큰 append 전에 실행됨
if (!_ttsStreamingActive && (_isVoiceMode || _autoTtsEnabled))
{
    await JS.InvokeVoidAsync("fabTts.playStream", _dotNetRef);  // ← yield!
    _ttsStreamingActive = true;
}
// ... (Error/IsComplete/Token 분기)
assistantMsg.Text += chunk.Token;  // ← TTS yield 이후에 실행

// 변경 후: 토큰이 먼저 append됨
assistantMsg.Text += chunk.Token;  // ← 동기 실행, 순서 보장
// ... (ScheduleRenderUpdate, ResetStaleStreamTimer)
if (!_ttsStreamingActive && !_ttsInitPending && (...))
{
    _ttsInitPending = true;  // 동기 guard
    await JS.InvokeVoidAsync("fabTts.playStream", _dotNetRef);  // ← yield OK
    _ttsStreamingActive = true;
    await JS.InvokeVoidAsync("fabTts.feedText", assistantMsg.Text);  // 축적된 전체 텍스트
}
```

#### 2. TTS Init 중복 진입 방지 (TTS Init Guard)

**파일**: `Index.razor` — `HandleChunkReceived`, 필드 추가

- `_ttsInitPending` 플래그 추가: `playStream` await 전에 **동기적으로** 설정
- TTS init 중 도착하는 chunk들은 개별 `feedText` 호출을 건너뜀
- init 완료 후 `feedText(assistantMsg.Text)`로 init 동안 축적된 **전체 텍스트를 한 번만** 전달
- 이후 chunk는 기존처럼 `feedText(chunk.Token)`으로 개별 전달

```csharp
// 3가지 상태 분기:
// 1. !_ttsStreamingActive && !_ttsInitPending → 최초 init 시작
// 2. _ttsStreamingActive                      → 개별 토큰 feed
// 3. _ttsInitPending && !_ttsStreamingActive   → init 대기 중, feed 건너뜀 (init 후 일괄 전달)
```

**문제 해결**: TTS init 중 여러 chunk가 각각 init 블록에 진입 →
`feedText(assistantMsg.Text)` 반복 호출 → 첫 문장 무한 반복 TTS 현상 수정

#### 3. JIT 렌더 간격 단축 (Render Interval Optimization)

**파일**: `Index.razor` — `ScheduleRenderUpdate`

- JIT 모드 렌더 간격: **300ms → 150ms**
- 초당 화면 갱신 빈도: ~3회 → ~6회
- 스트리밍 중 텍스트가 더 빠르게 화면에 나타남 (체감 속도 개선)

```csharp
// 변경 전
var interval = _renderMode == "jit" ? 300 : 100;
// 변경 후
var interval = _renderMode == "jit" ? 150 : 100;
```

#### 4. 완료 시 텍스트 무결성 검증 로그

**파일**: `Index.razor` — `HandleChunkReceived` (IsComplete 분기)

- IsComplete 수신 시 `console.warn`으로 축적된 텍스트 길이와 처음 200자를 출력
- 향후 문제 발생 시 브라우저 F12 콘솔에서 실제 데이터 확인 가능

```
[Blazor] COMPLETE chunks=325 len=4521 text=## 요약\nCMP 패드 교체 주기는...
```

### 검증

3개 Python 테스트 스크립트로 백엔드 토큰 전달 정합성 확인:

| 테스트 | 결과 | 설명 |
|--------|------|------|
| `test_duplicate_detect.py` | **5/5 통과** | 단일 요청에 중복 ConversationId 없음 |
| `test_interleave_detect.py` | **10/10 통과** | 정지 후 즉시 재연결 시 교차 오염 없음 |
| `test_stop_resume.py` | **10/10 통과** | 정지 후 재개 시 정상 응답 |

### 영향 범위

- **수정 파일**: `Index.razor` (HandleChunkReceived, ScheduleRenderUpdate, 필드 추가)
- **신규 필드**: `_ttsInitPending` (bool)
- **버전**: v4.85.2 → **v4.86.0**
- **하위 호환성**: 완전 호환 (내부 렌더링 로직 변경만, API/프로토콜 변경 없음)

---

## v4.83 — Phase 1.0~2.5 System Design Implementation

### Phase 1.0 보완

#### 1.0-A. Config Hot-Reload (설계서 2.4)
- `EmbeddingClientResolver`: IOptionsMonitor 기반 Embedding Provider 동적 전환 (Ollama ↔ TEI)
- `LlmClientResolver`: IOptionsMonitor 기반 LLM Provider 동적 전환 (Ollama ↔ TGI)
- 서비스 재시작 없이 appsettings.json 변경 즉시 반영
- **파일**: `EmbeddingClientResolver.cs`, `LlmClientResolver.cs`, `LlmServiceExtensions.cs`

#### 1.0-B. LLM Server Fallback (설계서 2.5.1)
- `FallbackLlmClient`: Primary → Secondary → SLM 3단계 자동 페일오버
- `LlmHealthChecker`: 10초 주기 heartbeat + inference latency 모니터링
- Primary 30초 무응답 시 자동 Secondary 전환
- **파일**: `FallbackLlmClient.cs`, `LlmHealthChecker.cs`, `FallbackServerOptions.cs`

#### 1.0-C. GPU Pre-flight Check (설계서 2.6)
- `scripts/preflight-check.sh`: GPU 감지, CUDA 버전, VRAM 용량 → 모델 자동 선택
- Hardware profile: entry / standard / high_end / apple / cpu_only
- **파일**: `scripts/preflight-check.sh`

#### 1.0-D. Multi-Tier Chunking 강화 (설계서 3.1)
- 3-tier fallback: Structure-Based → Semantic Boundary → Sliding Window
- Semantic boundary detection: 연속 문단 간 cosine similarity (threshold: 0.65)
- 최소/최대 chunk 크기: 64~512 tokens
- **파일**: `DocumentIngestor.cs`

#### 1.0-F. Response Timeout UX 강화 (설계서 9.9)
- Progressive notification: 10초/30초/1분/3분 단계별 알림
- 사용자 옵션: 시간 연장, 쿼리 간소화 제안, 부분 응답 표시, 취소 & 재시도
- **파일**: `Index.razor`, `_Layout.cshtml`

#### 1.0-G. DLP Input/Output Guardrails (설계서 11.5)
- `DlpFilter`: Equipment ID 마스킹, 민감 파라미터 제거
- Output guardrail: Regex 기반 yield %, recipe, lot ID 등 감지 → [REDACTED]
- appsettings 기반 개별 룰 ON/OFF
- **파일**: `DlpFilter.cs`, `LlmWorker.cs`, `LlmService/appsettings.json`

### Phase 1.5: RAG Quality Enhancement

#### 1.5-A. RAGAS 기반 검색 품질 평가 (설계서 12.3)
- `RagEvaluationService`: Recall@K, MRR@K, NDCG@K 자동 평가
- Ground Truth: 100+ 트리플 (query, expected_docs, expected_answer)
- `scripts/evaluate-rag.sh`: CLI 평가 스크립트
- **파일**: `Services/Evaluation/`, `rag-evaluation-groundtruth.json`

#### 1.5-C. Image OCR + Figure Cross-reference (설계서 3.1)
- `IImageOcrExtractor` 인터페이스 + Tesseract/EasyOCR 구현
- `FigureCrossReferenceParser`: 본문 "그림 X.X 참조" → 이미지 메타데이터 연결
- **파일**: `Services/ImageOcr/`, `IImageOcrExtractor.cs`

#### 1.0-E. Dual-Index Transition (설계서 4.3.2)
- `DualIndexManager`: Active/Standby Qdrant 컬렉션 병렬 관리
- 새 모델 재인덱싱 → 평가 통과 → Active 전환 → 롤백용 보관
- **파일**: `DualIndexManager.cs`, `VectorStoreServiceExtensions.cs`

### Phase 1.5+: Query Intelligence & Voice

#### 1.5+-A. Query Intelligence Pipeline (설계서 9.12)
- 3-Stage 교정: Dictionary Matching → Pattern Correction → LLM Correction
- Stage 3는 미교정 토큰 30%+ 시만 조건부 트리거
- **파일**: `QueryIntelligencePipeline.cs`

#### 1.5+-B. Voice Conversation Mode (설계서 9.13)
- TTS 프록시 엔드포인트 (`/api/tts/*`)
- `pronunciation_rules.json`: 반도체 도메인 발화 규칙
- Docker profile "tts" (Coqui XTTS-v2)
- **파일**: `ChatGateway/Program.cs`, `pronunciation_rules.json`, `docker-compose.yml`

### Phase 2.0: Log Collection + Time-Series Analysis

#### 2.0-A. Equipment Data Collection Pipeline
- `IEquipmentDataAdapter`, `IEquipmentRegistry` 인터페이스
- `RedisEquipmentRegistry`: 장비 등록, 상태, bay/fab 매핑
- `MockEquipmentDataAdapter`: 시뮬레이션 데이터 어댑터
- **파일**: `Contracts/Interfaces/`, `Contracts/Models/`, `RedisEquipmentRegistry.cs`

#### 2.0-B/C. Time-Series + Alarm Analysis
- `TimeSeriesAnalyzer`: SMA, EMA, CUSUM change point detection, 3-sigma anomaly
- `AlarmPatternAnalyzer`: Top-N 빈발, 시간대 분포, cascading 패턴, MTBF/MTTR
- **파일**: `McpLogServer/Analysis/`

#### 2.0-D. Dashboard v1
- `Dashboard.razor`: Sensor trend, alarm timeline, anomaly status
- **파일**: `Pages/Dashboard.razor`

#### 2.0-E. Tier 3 Auto-Extraction
- `CausalKnowledgeExtractor`: 문서 기반 error→cause→action 인과관계 자동 추출
- **파일**: `McpLogServer/Analysis/CausalKnowledgeExtractor.cs`

### Phase 2.5: Anomaly Detection + Predictive Diagnostics

#### 2.5-A. ML Anomaly Detection
- `IsolationForestDetector`: Isolation Forest 기반 다변량 이상 탐지
- `AnomalyDetectorManager`: 장비별 독립 모델 관리
- **파일**: `McpLogServer/Analysis/IsolationForestDetector.cs`

#### 2.5-B. Expert Knowledge Base
- `ExpertKnowledgeBase`: 구조화된 전문가 규칙 (trigger, cause, action)
- 자동 검증 루프, confidence score, 비활성화 관리
- **파일**: `McpLogServer/Analysis/ExpertKnowledgeBase.cs`

#### 2.5-C. Fusion Engine
- 3-Tier 증거 통합 → 다중 가설 순위 진단
- 각 가설: confidence, Tier별 증거, 권장 조치
- **파일**: `McpLogServer/Analysis/FusionEngine.cs`

#### 2.5-D. Predictive Alerts
- `PredictiveAlertGenerator`: Multi-tier 증거 패키지 알림
- 중복 억제, 에스컬레이션, 알림 확인 (acknowledge)
- **파일**: `McpLogServer/Analysis/PredictiveAlertGenerator.cs`

#### 2.5-E. Dashboard Enhancement
- Predictive summary, RUL 게이지, KB 브라우저
- **파일**: `Pages/Dashboard.razor`

### Security & Operations

#### RBAC 확장 (설계서 11.1~11.3)
- `AccessPolicy`, `UserIdentity` 모델
- 5개 역할: Operator, MaintenanceEngineer, SeniorEngineer, EquipmentOwner, Admin
- 장비별 + 기능별 접근 제어
- **파일**: `Contracts/Models/AccessPolicy.cs`, `UserIdentity.cs`

#### Structured Logging (설계서 12.6)
- JSON 구조화 로그 (Serilog File Sink)
- `SensitiveDataMaskingEnricher`: 민감 데이터 마스킹
- **파일**: `Observability/Enrichers/`, `ObservabilityServiceExtensions.cs`

### RAG Pipeline 디버깅 및 수정

#### OllamaEmbeddingClient 리팩터링
- OllamaSharp 라이브러리 제거, 직접 HTTP POST `/api/embed` 호출로 교체
- 외부 라이브러리 의존성 감소, 투명한 API 호출
- **파일**: `OllamaEmbeddingClient.cs`

#### 설정 수정
- `RagService/appsettings.json`: Embedding.Provider "Tei"→"Ollama", MinScore 0.45→0.35
- `LlmService/appsettings.json`: Embedding.Provider "Tei"→"Ollama", GateAThreshold 0.55→0.40
- DLP 설정 추가, FallbackServer 설정 추가

#### RagWorker 운영 로깅 개선
- ProcessRagRequestAsync: 요청 수신 시 Query, EquipmentId, Pipeline 기록
- Graph pipeline: Query, TopK 정보 Debug 로그 추가

### 테스트

- **전체 1,522개 단위 테스트 통과** (실패: 0, 건너뜀: 0)
- 문서 콘텐츠 테스트 12개 파일 (CMP 장비 12개 문서 커버리지)
- Citation 테스트, Gate 테스트, Intent 라우팅 테스트
- Analysis 모듈 테스트 (TimeSeriesAnalyzer, AlarmPatternAnalyzer 등)
- Security 테스트 (RBAC, DLP)
- Evaluation 테스트 (RAGAS 메트릭)
