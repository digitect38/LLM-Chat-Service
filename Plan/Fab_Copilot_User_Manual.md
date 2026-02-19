
# Fab Copilot 사용자 매뉴얼

> **Version:** 1.2
> **Date:** 2026-02-20
> **대상:** Fab 장비 엔지니어, 오퍼레이터
> **접속 URL:** `http://<서버IP>:5010`

---

## 목차

1. [개요](#1-개요)
2. [화면 구성](#2-화면-구성)
3. [장비 연결](#3-장비-연결)
4. [LLM 모델 선택](#4-llm-모델-선택)
5. [채팅 사용법](#5-채팅-사용법)
6. [응답 형식 (Markdown / 수식)](#6-응답-형식)
7. [RAG 지식 관리](#7-rag-지식-관리)
8. [오류 및 문제 해결](#8-오류-및-문제-해결)
9. [관리자 설정 가이드](#9-관리자-설정-가이드)

---

## 1. 개요

Fab Copilot은 반도체 Fab 장비 엔지니어를 위한 **On-Prem AI 어시스턴트**입니다.

**주요 기능:**
- 장비별 1:1 채팅 (장비 ID 기반 세션 관리)
- RAG 기반 지식 검색 (매뉴얼, SOP, 트러블슈팅 문서 자동 참조)
- 다중 LLM 모델 선택 (대화 중에도 모델 전환 가능)
- 실시간 토큰 스트리밍 (응답을 기다리지 않고 즉시 확인)
- Markdown + LaTeX 수식 렌더링

---

## 2. 화면 구성

화면은 크게 **3개 영역**으로 나뉩니다.

```
+------------------------------------------------------------------+
|  [헤더]   Fab Copilot — Equipment Operator Assistant              |
+------------------------------------------------------------------+
|  [상단 패널]  Equipment ID [____] [Connect] (상태)  |  Model [v]  |
+------------------------------------------------------------------+
|                                                                    |
|  [채팅 영역]                                                       |
|                                                                    |
|    You: 질문 내용...                                               |
|                                                                    |
|    Fab Copilot: 응답 내용...                                       |
|                                                                    |
+------------------------------------------------------------------+
|  [입력 영역]  [메시지 입력...                        ] [Send]      |
+------------------------------------------------------------------+
```

### 2.1 상단 패널 (Equipment Panel)

| 요소 | 설명 |
|------|------|
| **Equipment ID** | 연결할 장비 ID 입력 (예: `CMP01`, `ETCH01`) |
| **Connect / Disconnect** | 장비 연결 / 해제 버튼 |
| **연결 상태** | 초록색 점 = 연결됨, 회색 점 = 미연결 |
| **Model** | LLM 모델 선택 드롭다운 |

### 2.2 채팅 영역

- **파란색 말풍선** (오른쪽): 사용자 메시지
- **흰색 말풍선** (왼쪽): Copilot 응답
- 응답 생성 중에는 **깜빡이는 커서(`|`)** 가 표시됩니다
- 오류 발생 시 말풍선 하단에 **빨간색 오류 메시지**가 표시됩니다

### 2.3 입력 영역

- 텍스트 입력 후 **Send** 버튼 클릭 또는 **Enter** 키로 전송
- **Shift + Enter**: 줄바꿈 (전송하지 않음)
- 장비 미연결 시 입력 비활성화

---

## 3. 장비 연결

### 3.1 연결 절차

1. **Equipment ID** 입력란에 장비 ID를 입력합니다 (기본값: `CMP01`)
2. **Connect** 버튼을 클릭합니다
3. 상태 표시등이 **초록색(Connected)** 으로 바뀌면 연결 성공입니다
4. 이전 대화 기록이 초기화되고 새 세션이 시작됩니다

### 3.2 연결 해제

- **Disconnect** 버튼을 클릭하면 연결이 해제됩니다
- 다른 장비로 전환하려면 먼저 Disconnect 후 새 장비 ID를 입력하고 다시 Connect 합니다

### 3.3 연결 실패 시

연결 실패 시 상단 패널 아래에 빨간색 오류 메시지가 표시됩니다.

**일반적인 원인:**
- Chat Gateway 서비스가 실행 중이 아닌 경우
- 네트워크 연결 문제
- 잘못된 장비 ID 형식

---

## 4. LLM 모델 선택

### 4.1 모델 드롭다운

상단 패널 오른쪽의 **Model** 드롭다운에서 사용할 LLM 모델을 선택합니다.

| 모델 | 설명 | 특징 |
|------|------|------|
| **EXAONE 3.5 (7.8B)** | LG AI Research 한국어 특화 모델 | 한국어 품질 최우수 (기본값) |
| **Qwen 2.5 (7B)** | Alibaba 다국어 모델 | 범용 성능 우수 |
| **Llama 3.1 (8B)** | Meta 오픈소스 모델 | 영어 기반, 추론 강점 |

### 4.2 모델 선택 시 유의사항

- **대화 도중에도** 모델을 변경할 수 있습니다. 다음 메시지부터 선택한 모델이 적용됩니다.
- 모델 변경 시 이전 대화 기록은 유지됩니다 (새 세션이 시작되지 않음).
- 응답 생성 중(커서 깜빡임 상태)에는 모델 변경이 비활성화됩니다.
- 선택한 모델이 서버에 설치되어 있지 않으면 오류 메시지가 반환됩니다.

### 4.3 모델 선택 권장 가이드

| 사용 목적 | 권장 모델 |
|-----------|-----------|
| 한국어 장비 문의 (일반) | EXAONE 3.5 |
| 한국어 트러블슈팅 | EXAONE 3.5 |
| 영문 매뉴얼 기반 질의 | Llama 3.1 또는 Qwen 2.5 |
| 코드/스크립트 관련 질의 | Qwen 2.5 |

---

## 5. 채팅 사용법

### 5.1 질문하기

1. 장비 연결 상태를 확인합니다 (초록색 점)
2. 입력란에 질문을 입력합니다
3. **Enter** 또는 **Send** 버튼으로 전송합니다
4. Copilot이 실시간으로 응답을 스트리밍합니다

### 5.2 효과적인 질문 방법

**구체적으로 질문하세요:**

| 비효율적 | 효과적 |
|----------|--------|
| "알람 뭐야?" | "CMP01에서 A123 알람이 발생했는데 원인이 뭔가요?" |
| "압력 이상" | "Head Zone 7에서 압력이 0.8Hz로 진동하는 원인과 조치 방법은?" |
| "레시피 알려줘" | "CMP oxide polishing 레시피에서 slurry flow rate 기준값은?" |

**활용 예시:**

- 알람 분석: `"A123 알람이 발생했습니다. 가능한 원인과 조치 방법을 알려주세요."`
- 절차 안내: `"CMP pad 교체 절차를 단계별로 알려주세요."`
- 파라미터 확인: `"Zone 7 pressure 정상 범위와 이상 판단 기준은?"`
- 비교 질의: `"Pad glazing과 pad wear의 차이점 및 구분 방법은?"`

### 5.3 대화 기록

- 같은 세션 내에서 이전 대화 맥락이 유지됩니다
- Copilot은 이전 질문/답변을 참고하여 응답하므로, 후속 질문 시 맥락을 반복할 필요가 없습니다
- 장비를 Disconnect → 다시 Connect 하면 새 세션이 시작되며 대화 기록이 초기화됩니다

---

## 6. 응답 형식

Copilot 응답은 다음 형식을 지원합니다.

### 6.1 Markdown

- **굵은 글씨**, *기울임*, 제목, 목록, 코드 블록 등이 자동으로 렌더링됩니다
- 표(Table)와 구분선도 지원됩니다

### 6.2 수식 (LaTeX / KaTeX)

공정 관련 수식이 자동으로 렌더링됩니다.

- 인라인 수식: `$v = r \times \omega$` 형태
- 블록 수식: `$$MRR = K_p \times P \times V$$` 형태

### 6.3 언어

- Copilot은 **한국어**로 응답합니다
- 기술 용어 (CMP, slurry, polishing pad 등)는 영어로 표기될 수 있습니다

---

## 7. RAG 지식 관리

RAG(Retrieval-Augmented Generation)는 Copilot이 **등록된 지식 문서를 검색하여 응답에 반영**하는 기능입니다.

### 7.1 RAG 동작 원리

채팅 시 RAG는 **자동으로 동작**합니다. 별도 조작 없이 질문을 보내면:

```
사용자 질문
  → RagService: 질문을 벡터 임베딩으로 변환
  → Qdrant: knowledge 컬렉션에서 유사 문서 Top-5 검색
  → LlmWorker: 검색 결과를 시스템 프롬프트에 주입
  → LLM: 지식 기반으로 응답 생성
```

- 검색 타임아웃: 10초 (초과 시 RAG 없이 응답)
- 유사도 기준: Cosine Similarity
- 반환 결과: 최대 5건

### 7.2 지식 등록 워크플로

지식은 **3단계 워크플로**를 거쳐 등록됩니다.

```
Draft(초안) → Approved(승인) → Indexed(인덱싱)
```

| 단계 | 설명 | API |
|------|------|-----|
| **Draft** | 지식 초안 생성, Redis에 저장 (30일 유효) | `POST /api/knowledge` |
| **Approved** | 엔지니어 검토 후 승인 (90일 유효) | `PUT /api/knowledge/{id}/status` |
| **Indexed** | 벡터 임베딩 생성 → Qdrant에 저장 (검색 가능) | `POST /api/knowledge/{id}/index` |

### 7.3 지식 등록 방법 (Knowledge Service API)

Knowledge Service API를 통해 지식을 등록합니다.

> **API 기본 URL:** `http://<서버IP>:<KnowledgeService 포트>`
> (포트는 관리자에게 확인)

#### Step 1: 지식 초안 생성

```bash
curl -X POST http://localhost:<포트>/api/knowledge \
  -H "Content-Type: application/json" \
  -d '{
    "type": "troubleshooting",
    "equipment": "CMP01",
    "symptom": "Zone 7 압력이 0.8Hz로 진동",
    "rootCause": "Pad glazing으로 인한 마찰 변화",
    "solution": "Pad conditioning 프로그램 실행 후 테스트 wafer 진행"
  }'
```

**응답 예시:**
```json
{
  "id": "a1b2c3d4-...",
  "type": "troubleshooting",
  "equipment": "CMP01",
  "status": "Draft",
  "version": 1,
  "createdAt": "2026-02-19T10:00:00+09:00"
}
```

**type 값 예시:**

| type | 용도 |
|------|------|
| `troubleshooting` | 증상 → 원인 → 해결 (트러블슈팅) |
| `procedure` | 작업 절차 (SOP) |
| `alarm_resolution` | 알람 코드별 조치 방법 |
| `maintenance` | 유지보수/PM 가이드 |

#### Step 2: 승인

```bash
curl -X PUT http://localhost:<포트>/api/knowledge/{id}/status \
  -H "Content-Type: application/json" \
  -d '{
    "status": 2,
    "approvedBy": "hong.engineer"
  }'
```

**status 코드:**

| 값 | 상태 | 설명 |
|----|------|------|
| 0 | Draft | 초안 (기본) |
| 1 | PendingReview | 검토 대기 |
| 2 | Approved | 승인 완료 |
| 3 | Rejected | 반려 |
| 4 | Archived | 보관 (비활성) |

#### Step 3: 벡터 인덱싱

승인된 지식만 인덱싱할 수 있습니다.

```bash
curl -X POST http://localhost:<포트>/api/knowledge/{id}/index
```

**응답 예시:**
```json
{
  "id": "a1b2c3d4-...",
  "indexed": true
}
```

인덱싱이 완료되면 **즉시** 채팅에서 검색 가능합니다.

#### 검토 대기 목록 조회

```bash
curl http://localhost:<포트>/api/knowledge/pending
```

### 7.4 실전 예제: CMP 트러블슈팅 지식 일괄 등록

```bash
API=http://localhost:<포트>/api/knowledge

# 지식 1: 압력 진동
ID1=$(curl -s -X POST $API \
  -H "Content-Type: application/json" \
  -d '{
    "type": "troubleshooting",
    "equipment": "CMP01",
    "symptom": "Head Zone 7 pressure oscillation at 0.5-2Hz",
    "rootCause": "Pad glazing causing friction variation",
    "solution": "1) Run pad conditioning (60s, 5lbf). 2) Verify with test wafer. 3) If persists, replace pad."
  }' | jq -r '.id')

curl -s -X PUT $API/$ID1/status \
  -H "Content-Type: application/json" \
  -d '{"status": 2, "approvedBy": "engineer"}'

curl -s -X POST $API/$ID1/index

# 지식 2: 알람 A123
ID2=$(curl -s -X POST $API \
  -H "Content-Type: application/json" \
  -d '{
    "type": "alarm_resolution",
    "equipment": "CMP01",
    "symptom": "Alarm A123 - Head pressure out of range",
    "rootCause": "Retaining ring wear or membrane leak",
    "solution": "1) Check retaining ring thickness. 2) Inspect membrane for leaks. 3) Replace if worn."
  }' | jq -r '.id')

curl -s -X PUT $API/$ID2/status \
  -H "Content-Type: application/json" \
  -d '{"status": 2, "approvedBy": "engineer"}'

curl -s -X POST $API/$ID2/index
```

### 7.5 폴더 감시 자동 수집 (File Watcher)

RagService가 특정 폴더를 감시하여, 문서 파일을 넣으면 **자동으로 벡터 스토어에 수집**됩니다.
Knowledge Service API를 사용하지 않고도 간편하게 지식을 등록할 수 있습니다.

#### 지원 파일 형식

| 확장자 | 설명 |
|--------|------|
| `.md` | Markdown 문서 |
| `.txt` | 일반 텍스트 |
| `.pdf` | PDF 문서 (텍스트 추출) |

#### 사용 방법

1. RagService 실행 디렉토리에 `knowledge-docs/` 폴더가 자동 생성됩니다
2. 해당 폴더에 `.md`, `.txt`, `.pdf` 파일을 복사합니다
3. RagService 로그에서 수집 완료 메시지를 확인합니다
4. 즉시 채팅에서 해당 문서 기반 RAG 검색이 가능합니다

#### 동작 규칙

| 동작 | 설명 |
|------|------|
| **파일 추가** | 자동으로 텍스트 추출 → 청킹 → 임베딩 → Qdrant 저장 |
| **파일 수정** | 기존 청크 삭제 후 재수집 (청크 수 변경 대응) |
| **파일 삭제** | 벡터 스토어에서 해당 문서의 모든 청크 자동 삭제 |
| **파일 이름변경** | 이전 이름 삭제 + 새 이름으로 재수집 |

#### 설정 (관리자)

RagService의 `appsettings.json`에서 설정합니다.

```json
{
  "Rag": {
    "WatchFolder": "knowledge-docs",
    "DebounceMs": 500,
    "ScanOnStartup": true
  }
}
```

| 설정 | 기본값 | 설명 |
|------|--------|------|
| `WatchFolder` | `knowledge-docs` | 감시할 폴더 경로 (`null`이면 비활성화) |
| `DebounceMs` | `500` | 중복 이벤트 방지 대기 시간 (ms) |
| `ScanOnStartup` | `true` | 서비스 시작 시 기존 파일 전체 스캔 여부 |

#### 주의사항

- 모든 문서는 **장비 공용(Equipment ID: `shared`)** 으로 수집됩니다 (장비별 구분 없음)
- 하위 폴더도 자동 감시됩니다 (재귀적)
- PDF는 텍스트 기반 추출만 지원됩니다 (스캔 이미지 PDF는 비지원)
- 파일 잠금 시 자동 재시도합니다 (최대 3회, 지수 백오프)

### 7.6 RAG 활용 팁

**지식이 잘 검색되려면:**

- **증상(Symptom)** 에 엔지니어가 실제 사용하는 표현을 포함하세요
  - 좋음: `"Zone 7 압력 진동, pressure oscillation 0.8Hz"`
  - 부족: `"이상"`
- **장비 ID(Equipment)** 를 정확히 입력하세요 (장비별 필터링에 사용)
- 원인과 해결 방법을 **구체적**으로 작성할수록 Copilot 응답 품질이 좋아집니다
- 영어와 한국어를 **혼용**하면 검색 범위가 넓어집니다

**검색이 안 되는 경우:**

| 원인 | 확인 방법 |
|------|-----------|
| 인덱싱 안 됨 | Step 3 (index) 호출 여부 확인 |
| 승인 안 됨 | status가 Approved(2)인지 확인 |
| 장비 ID 불일치 | 등록 시 equipment와 채팅 시 Equipment ID 일치 확인 |
| Qdrant 미실행 | 관리자에게 Qdrant 서비스 상태 확인 |
| 임베딩 모델 미설치 | `ollama list`에서 nomic-embed-text 확인 |

### 7.7 임베딩 구조

인덱싱 시 다음 필드가 하나의 텍스트로 결합되어 벡터로 변환됩니다:

```
"Equipment: CMP01. Symptom: Zone 7 pressure oscillation. Root Cause: Pad glazing. Solution: Run pad conditioning."
```

- 임베딩 모델: `nomic-embed-text` (768차원)
- 벡터 DB: Qdrant (`knowledge` 컬렉션)
- 유사도 측정: Cosine Similarity

---

## 8. 오류 및 문제 해결

### 8.1 연결 오류

| 증상 | 원인 | 해결 |
|------|------|------|
| "Failed to connect" | Gateway 서비스 중단 | 관리자에게 서비스 상태 확인 요청 |
| 연결 후 즉시 Disconnected | WebSocket 연결 끊김 | 새로고침(F5) 후 재연결 |
| 연결 버튼 반응 없음 | 브라우저 호환성 | Chrome/Edge 최신 버전 사용 권장 |

### 8.2 응답 오류

| 증상 | 원인 | 해결 |
|------|------|------|
| 말풍선에 "Error: ..." 표시 | LLM 서비스 오류 | 다시 질문하거나 다른 모델로 전환 |
| 응답이 매우 느림 (30초+) | CPU 추론 부하 | 잠시 대기, 반복 시 관리자 문의 |
| 커서만 깜빡이고 응답 없음 | LLM 타임아웃 | Disconnect 후 재연결, 다시 질문 |
| "model not found" 오류 | 선택한 모델 미설치 | 다른 모델로 전환, 관리자에게 모델 설치 요청 |

### 8.3 화면 오류

| 증상 | 해결 |
|------|------|
| 화면 하단에 노란색 오류 바 | **Reload** 링크 클릭 또는 F5 |
| 수식이 깨져서 보임 | 인터넷 연결 확인 (KaTeX CDN 필요) |
| 스크롤이 자동으로 안 내려감 | 수동으로 스크롤, 새 메시지 시 자동 복구 |

---

## 9. 관리자 설정 가이드

### 9.1 모델 목록 변경

WebClient의 `appsettings.json`에서 모델 목록을 관리합니다.

**파일 위치:** `src/Client/FabCopilot.WebClient/appsettings.json`

```json
{
  "Models": {
    "Default": "exaone3.5:7.8b",
    "Available": [
      { "Id": "exaone3.5:7.8b", "DisplayName": "EXAONE 3.5 (7.8B)" },
      { "Id": "qwen2.5:7b",     "DisplayName": "Qwen 2.5 (7B)" },
      { "Id": "llama3.1:8b",    "DisplayName": "Llama 3.1 (8B)" }
    ]
  }
}
```

| 필드 | 설명 |
|------|------|
| `Default` | 페이지 진입 시 기본 선택되는 모델 ID |
| `Available[].Id` | Ollama에 등록된 모델 태그 (정확히 일치해야 함) |
| `Available[].DisplayName` | 드롭다운에 표시되는 이름 |

### 9.2 새 모델 추가 절차

1. Ollama에 모델 다운로드:
   ```bash
   ollama pull <model-tag>
   ```

2. WebClient `appsettings.json`의 `Models.Available` 배열에 항목 추가

3. (선택) LlmService `appsettings.json`의 `Ollama.AvailableModels`에도 동일 항목 추가

4. WebClient 재시작

### 9.3 서비스 포트 정보

| 서비스 | 포트 | 용도 |
|--------|------|------|
| WebClient | 5010 | 사용자 웹 UI |
| ChatGateway | 5000 | WebSocket 게이트웨이 |
| KnowledgeService | 5020 | 지식 등록/승인/인덱싱 REST API |
| NATS | 4222 | 메시지 브로커 |
| Redis | 6379 | 대화 상태 저장 |
| Qdrant | 6333 | 벡터 검색 |
| Ollama | 11434 | LLM 추론 API |

### 9.4 Knowledge Service 포트 설정

Knowledge Service에 별도 포트를 할당해야 합니다 (기본 포트가 ChatGateway와 충돌).

**파일 위치:** `src/Services/FabCopilot.KnowledgeService/appsettings.json`

```json
{
  "Urls": "http://0.0.0.0:5020"
}
```

설정 후 서비스 재시작이 필요합니다.

### 9.5 설치된 모델 확인

```bash
ollama list
```

---
