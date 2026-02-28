# Fab Copilot v4.0 보완안 — 비판적 검토 및 선별 수용

## 검토 원칙

제안된 5개 항목을 다음 기준으로 평가한다:
- 기존 v4.0 문서와의 중복/충돌 여부
- 실제 구현 시 비용 대비 효과
- Phase 단계에 맞는 시점 적절성
- 반도체 Fab 현장 현실과의 정합성

---

## 1항. Fine-Grained RBAC + LLM DLP — 검토 결과: 부분 수용

### 수용하는 부분

**LLM 입출력 DLP**는 타당하고 기존 문서에 빠져 있던 관점이다.

On-prem 환경이라도 LLM 서빙 서버와 Embedding 서버 사이의 내부 통신에서
설비 ID나 공정 파라미터가 로그에 평문으로 남을 수 있고, 향후 클라우드
하이브리드 구성 시 외부 유출 경로가 생긴다. Input 마스킹과 Output
Guardrail 개념은 보안 장에 추가할 가치가 있다.

### 수용하지 않는 부분

**Fine-Grained RBAC (Level 1/2/3)**: 이 제안은 기존 v4.0의 11.2절과
실질적으로 동일한 내용을 다른 체계로 재정의하는 것이다.

기존 v4.0에는 이미 5개 역할(Operator / Engineer / Equipment Owner /
Data Analyst / Admin)이 Layer 1/Layer 2 각각에 대해 권한이 정의되어 있다.
제안의 Level 1/2/3은 이를 3단계로 단순화한 것인데, 오히려 퇴보에 가깝다.

| 제안 Level | 기존 v4.0 대응 | 차이점 |
|-----------|---------------|--------|
| Level 1 (Admin) | Admin | 동일 |
| Level 2 (Engineer) | Engineer + Equipment Owner | 기존이 더 세분화됨 |
| Level 3 (Operator) | Operator + Data Analyst | 기존이 더 세분화됨 |

특히 "담당 라인(Area) 기반 접근"이라는 개념은 기존의 "equipment_id 기반
접근 + Equipment Owner 역할"과 동일한 목적을 더 거친 단위로 달성하려는
것이다. equipment_id 단위가 Area 단위보다 정밀하므로 기존 설계가 우월하다.

### 최종 반영

- LLM DLP (Input Filter / Output Guardrail): **수용** → 11장에 11.5절로 추가
- RBAC Level 재정의: **기각** → 기존 5-Role 체계 유지

---

## 2항. Active Feedback + Retraining Pipeline — 검토 결과: 대부분 기각 (기존 중복)

### 기존 문서와의 중복 분석

이 제안의 거의 모든 내용이 v4.0에 이미 존재한다:

| 제안 항목 | 기존 v4.0 위치 | 상태 |
|----------|---------------|------|
| "정확함/부정확함" 피드백 버튼 | 9.4.1 "Inline feedback: Like/Dislike + comment per response" | **이미 존재** |
| 부정확 시 올바른 정보 입력 | 8.3.2 Passive 수집 Path 3 "Inaccurate feedback → Prompt for actual cause input" | **이미 존재** |
| 분기별 Embedding 재색인 | 4.3.2 Dual-Index 전환 아키텍처 + 12.5 Runbook | **이미 존재** |
| 신규 알람 패턴 → Expert KB 반영 | 8.3.2 Path 2 "Action History Auto-Linking" + 8.3.4 Lifecycle | **이미 존재** |

### 수용할 수 있는 추가 관점

단 하나, **"위험함" 피드백 등급**은 기존에 없는 관점이다.
"부정확"과 "위험함"은 다르다 — "위험함"은 해당 조치를 수행했을 때
장비 손상이나 안전사고로 이어질 수 있다는 의미이므로, 별도 에스컬레이션
경로가 필요하다.

### 최종 반영

- Active Feedback UI: **기각** → 기존 9.4.1 + 8.3.2에 이미 존재
- Retraining Pipeline: **기각** → 기존 4.3.2 + 8.3.4에 이미 존재
- "위험함" 피드백 등급 + 에스컬레이션: **수용** → 9.4.1절 피드백에 추가

---

## 3항. Multi-Stage Progressive Response + HA Failover — 검토 결과: 부분 수용

### Progressive Response: 수용 (개선된 형태로)

이 아이디어의 핵심은 좋다. 사용자가 3분을 빈 화면으로 기다리는 것이 아니라
단계적으로 결과를 받는 것은 UX에 결정적이다.

그러나 제안의 시간 기준에 수정이 필요하다:

| 제안 | 문제점 | 수정안 |
|------|--------|--------|
| T+5s: 알람 + 기본 상태 | 이것은 Layer 2 이상 감지 시나리오에만 해당. 일반 RAG 질의에는 "알람"이 없음 | Layer별 분리: L1은 검색 중간결과, L2는 장비 상태 |
| T+30s: 매뉴얼 요약 + 1차 진단 | 30초는 Layer 1 파이프라인에서 Re-ranking까지 완료 가능한 시점이므로 적절 | 유지. 다만 "요약"이 아닌 "검색된 Top-3 chunk 미리보기"가 현실적 |
| T+180s: 3-Tier 융합 최종 결과 | 이것은 Cross-Layer 시나리오에만 해당. 단순 RAG은 이전 단계에서 이미 완료 | Layer/시나리오별 분기 |

### HA Failover: 부분 수용

**Ollama 이중화 + Health-check**: On-prem 환경에서 LLM 서빙 서버의
단일 장애점(SPOF)은 실제 운영 리스크다. Health-check 모듈 추가는 타당하다.

**경량 백업 모델(SLM) 전환**: 개념은 좋으나, SLM으로 전환하면 응답 품질이
급격히 저하된다. "기본 응답 보장"이라고 했지만, 반도체 장비 진단에서
품질이 떨어지는 응답은 "없는 것보다 위험할 수 있다." 잘못된 조치를
수행하면 장비 손상으로 이어지기 때문이다.

따라서 SLM Failover는 다음 조건부로만 수용:
- SLM 응답에 명시적 품질 경고 표시: "백업 모델로 생성된 응답입니다.
  정밀도가 낮을 수 있으므로 반드시 매뉴얼을 직접 확인하세요."
- SLM은 Layer 1 RAG 단순 검색 결과만 반환 (LLM 생성 없이 chunk 원문 제시)
- Layer 2 예지 진단은 SLM 전환 불가 (잘못된 예지 결과는 오히려 위험)

### 최종 반영

- Multi-Stage Progressive Response: **수용** (Layer별 분기 형태로 수정) → 10장에 추가
- LLM 서버 이중화 + Health-check: **수용** → 2.5절에 추가
- SLM Failover: **조건부 수용** (품질 경고 + Layer 1 chunk 원문만) → 2.5절에 추가

---

## 4항. Multimodal Data Bridge (Phase 3 준비) — 검토 결과: 부분 수용

### Media_Link 필드: 수용

이벤트 로그에 `media_link` 필드를 미리 추가해두는 것은 스키마 차원의
선행 투자로 비용이 거의 없다. Phase 3에서 멀티모달 기능을 추가할 때
데이터 마이그레이션 부담을 줄일 수 있다.

### Cross-Reference Schema (매뉴얼 구절 ↔ 부품 사진 매핑): 시기상조

매뉴얼 텍스트와 실제 부품 사진을 매핑하는 테이블을 지금 구축하는 것은
시기상조다. Phase 3의 핵심 작업이 바로 이것이며, Phase 2 단계에서
스키마만 정의해놓으면 실제 매핑 데이터가 없는 빈 테이블만 남게 된다.

또한 "실제 설비 부품 사진"을 체계적으로 수집하는 프로세스 자체가
Phase 3의 범위이므로, 스키마 선정의가 실질적 가치를 주지 않는다.

### 최종 반영

- media_link 필드: **수용** → 6.1.1절 데이터 유형에 추가
- Cross-Reference Schema: **기각** → Phase 3 범위로 유보

---

## 5항. 현장 특화 로컬라이제이션 (Domain Dictionary) — 검토 결과: 기각 (완전 중복)

이 제안은 기존 v4.0의 다음 두 곳과 완전히 동일하다:

| 제안 내용 | 기존 v4.0 위치 | 비고 |
|----------|---------------|------|
| "PM, BM, Chamber O/H" 등 공정 약어 변환 | 3.5 기술 동의어 사전 관리 | **완전 동일** |
| Query Expansion에서 은어를 기술 용어로 치환 | 4.2.1 Query Router + 5.1 Step 2 "synonym expansion" | **완전 동일** |

제안에서 추가로 언급한 "울렁임", "튐" 같은 현장 은어는 기존 동의어 사전의
확장 범위에 해당하며, 별도 항목을 신설할 필요 없이 3.5절의 사전 관리
프로세스("피드백 기반 즉시 추가")에서 자연스럽게 처리된다.

### 최종 반영

- **전체 기각** → 기존 3.5절 + 4.2.1절로 충분히 커버됨

---

## 수용 항목 요약

| # | 제안 항목 | 판정 | 반영 위치 | 사유 |
|---|----------|------|----------|------|
| 1a | Fine-Grained RBAC | 기각 | - | 기존 5-Role이 더 정밀 |
| 1b | LLM DLP (Input/Output) | **수용** | 11.5절 신설 | 기존에 없던 관점, 보안상 필요 |
| 2a | Active Feedback UI | 기각 | - | 기존 9.4.1 + 8.3.2와 중복 |
| 2b | Retraining Pipeline | 기각 | - | 기존 4.3.2 + 8.3.4와 중복 |
| 2c | "위험함" 피드백 등급 | **수용** | 9.4.1절 확장 | 안전 에스컬레이션 관점 |
| 3a | Progressive Response | **수용** (수정) | 10.3절 신설 | UX 개선 효과 큼, Layer별 분기 필요 |
| 3b | LLM 서버 이중화 + Health-check | **수용** | 2.5절 확장 | SPOF 해소 |
| 3c | SLM Failover | **조건부 수용** | 2.5절 확장 | 품질 경고 필수, L2 진단은 불가 |
| 4a | media_link 필드 | **수용** | 6.1.1절 확장 | 저비용 선행 투자 |
| 4b | Cross-Reference Schema | 기각 | - | Phase 3 범위, 시기상조 |
| 5 | Domain Dictionary | 기각 | - | 기존 3.5 + 4.2.1과 완전 중복 |
