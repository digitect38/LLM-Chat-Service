# CMP 장비 유지보수 가이드

## 1. 유지보수 개요

CMP 장비의 안정적 운영과 공정 품질 유지를 위해 정기적인 점검 및 유지보수가 필수적이다.

### 1.1 유지보수 분류

유지보수 분류 및 주기이다.

| 분류 | 주기 | 소요 시간 | 장비 중단 |
|------|------|----------|----------|
| Daily PM | 매일(교대 전) | 30분 | 불필요 |
| Weekly PM | 매주 월요일 | 2시간 | 필요 |
| Monthly PM | 매월 첫째 주 | 4~6시간 | 필요 |
| Quarterly PM | 분기 1회 | 8~12시간 | 필요 |
| Annual PM | 연 1회 | 2~3일 | 필요 |

## 2. Daily PM (일일 점검)

### 2.1 점검 항목

일일 점검 항목이다.

| # | 항목 | 기준 | 조치 |
|---|------|------|------|
| 1 | 슬러리 잔량 | > 30% | 보충 |
| 2 | DI water 압력 | 40~60 psi | 설비팀 연락 |
| 3 | Vacuum 압력 | < -600 mmHg | 펌프 점검 |
| 4 | 패드 상태 | 육안 | 교체 검토 |
| 5 | 컨디셔너 | 육안 | 교체 검토 |
| 6 | 슬러리 누수 | 육안 | 즉시 조치 |
| 7 | 알람 이력 | 전일 로그 | 원인 분석 |

### 2.2 일일 Qualification

일일 Qualification 절차이다. Test wafer 1매 연마 후 MRR 및 WIWNU 측정. MRR 허용 범위: 목표값 ± 10%. WIWNU 허용 범위: < 5%. 기준 초과 시 Pad conditioning 후 재측정. 2회 연속 불합격 시 PM 수행 후 재검증.

## 3. Weekly PM (주간 점검)

### 3.1 점검 항목

주간 점검 항목이다.

| # | 항목 | 기준 | 조치 |
|---|------|------|------|
| 1 | Retaining Ring 두께 | > 2.0 mm | 교체 예약 |
| 2 | Membrane 상태 | Hold Test | 교체 |
| 3 | 슬러리 필터 차압 | < 10 psi | 교체 |
| 4 | 컨디셔너 Arm | Sweep 정상 | 캘리브레이션 |
| 5 | 온도 센서 | ±1°C | 교정 |
| 6 | 로봇 티칭 | ±0.5 mm | 재티칭 |
| 7 | 배수 라인 | 막힘 없음 | 플러시 |

### 3.2 Pressure Hold Test 절차

Pressure Hold Test 절차이다. (1) 캐리어 헤드를 Test Station에 위치 → (2) 각 Zone에 설정 압력(3.0 psi) 인가 → (3) 공급 밸브 차단 → (4) 30초간 압력 변화 모니터링 → (5) 합격: 30초간 압력 강하 < 0.3 psi → (6) 불합격 시 Membrane 교체.

## 4. Monthly PM (월간 점검)

### 4.1 점검 항목

월간 점검 항목이다.

| # | 항목 | 기준 | 조치 |
|---|------|------|------|
| 1 | 패드 교체 | 수명/MRR 저하 | SOP 참조 |
| 2 | 슬러리 라인 플러시 | DI water 10분 | 결정화 방지 |
| 3 | Pressure Regulator | ±0.1 psi | 교정/교체 |
| 4 | EPD 센서 교정 | Reference wafer | 교정 |
| 5 | 배기 시스템 | 유속 확인 | 필터 교체 |
| 6 | 전기 배선 | 커넥터 상태 | 교체 |
| 7 | 로그 백업 | 아카이브 | 월 1회 |

### 4.2 패드 교체 기준

패드 교체 판단 기준이다. 사용 시간 > 500시간(Oxide Pad 기준). 패드 두께 < 1.0mm(초기 대비 50% 마모). MRR 저하 > 15%(신품 대비). WIWNU 악화 > 5%. 패드 표면 심한 Glazing 또는 Groove 마모. 1개 이상 해당 시 교체한다.

## 5. Quarterly PM (분기 점검)

### 5.1 점검 항목

분기 점검 항목이다. 캐리어 헤드 오버홀(Membrane, Retaining Ring, Backing Film 전체 교체). 플래튼 베어링 그리스 교체. 슬러리 펌프 다이어프램 교체. 모든 밸브 시트 점검 및 교체. 플래튼 평탄도 측정(허용: < 25 μm TIR). 전체 센서 교정(압력, 온도, 유량, 토크).

### 5.2 캐리어 헤드 오버홀 절차

캐리어 헤드 오버홀 절차이다. (1) 헤드 분리 → (2) Retaining Ring 분리, 두께 측정 기록 → (3) Membrane 분리, 상태 확인 기록 → (4) Backing Film 제거 → (5) 헤드 본체 세정(IPA + DI water) → (6) Air Channel 통기 확인(각 Zone별) → (7) 신품 Backing Film 부착 → (8) 신품 Membrane 장착 → (9) 신품 Retaining Ring 장착(토크 15 N·m) → (10) Pressure Hold Test → (11) Qualification.

## 6. Annual PM (연간 점검)

### 6.1 점검 항목

연간 점검 항목이다. 플래튼 모터 베어링 교체. 캐리어 모터 베어링 교체. 모든 공압/유압 호스 교체. 전기 케이블 절연 저항 측정. 로봇 전체 오버홀. 소프트웨어 업데이트 적용. 장비 전체 캘리브레이션. Annual PM 완료 후 전체 Qualification을 수행한다.

## 7. 소모품 수명 관리

### 7.1 소모품 수명 기준

소모품 수명 기준 및 관리이다.

| 소모품 | 수명 기준 | 교체 주기 | 최소 재고 |
|--------|----------|----------|----------|
| Polishing Pad | 500시간 | 월 1~2회 | 3매 |
| Retaining Ring | 두께 < 1.5mm | 분기 1회 | 2개 |
| Membrane | Hold 불합격 | 분기 1회 | 2개 |
| Conditioner Disk | 다이아몬드 탈락 | 6개월 | 1개 |
| Slurry Filter | 차압 > 10 psi | 주 1회 | 10개 |
| Backing Film | 오버홀 시 | 분기 1회 | 5매 |

## 8. 유지보수 기록 관리

### 8.1 기록 항목

PM 기록 항목이다. PM 일시, 수행자, 소요 시간. 교체 부품 목록 및 시리얼 번호. 측정값(MRR, WIWNU, 부품 치수). 이상 발견 사항 및 조치 내용. 다음 PM 예정일.

### 8.2 이력 보관

PM 이력 보관 기준이다. 전자 기록: 장비 로그 시스템에 입력. 종이 기록: PM 체크리스트 서명 후 파일링. 보관 기간: 최소 3년. 이력은 장비 성능 트렌드 분석 및 소모품 수명 예측에 활용한다.

## 9. PM 전 안전 절차

### 9.1 LOTO (Lock-Out/Tag-Out) 개요

LOTO는 PM 작업 시 작업자의 안전을 보장하기 위한 에너지 차단 절차이다. 대상 에너지: 전기(메인 전원), 공압(CDA), 화학(슬러리 공급), 진공. PM 등급별 LOTO 범위: Daily PM은 LOTO 불필요(장비 운전 상태 점검). Weekly PM 이상은 해당 모듈 에너지 차단. Quarterly/Annual PM은 전체 에너지 차단.

### 9.2 CMP 장비 LOTO 절차

CMP 장비 LOTO 절차이다. (1) 장비 정지 및 Idle 상태 확인 → (2) 메인 차단기 OFF → (3) CDA 공급 밸브 차단 → (4) 슬러리 공급 밸브 차단 → (5) 잔류 에너지 확인(콘덴서 방전 대기 5분, 잔압 해소) → (6) 개인 잠금 장치(자물쇠) 설치 → (7) 태그 부착(작업자 이름, 일시, 작업 내용) → (8) 시동 시도로 차단 확인. 작업 완료 후 역순 해제.

## 10. PM 스케줄 관리

### 10.1 생산 일정 연동

PM 스케줄과 생산 일정의 연동 관리이다. PM은 생산 계획과 조율하여 장비 가동률(Utilization)을 최대화한다. Weekly PM: 생산량이 적은 월요일 오전에 배치. Monthly PM: 월간 생산 계획에 PM Window를 사전 확보. 긴급 PM(Breakdown): 발생 즉시 수행하되, 진행 중 로트의 처리 완료 후 시작.

### 10.2 PM 지연 시 리스크 평가

PM 지연 시 리스크 평가 기준이다. Daily PM 미수행: MRR 편차 증가 위험(Low). Weekly PM 1주 지연: 소모품 이상 미감지 위험(Medium). Monthly PM 2주 지연: 패드 수명 초과 사용 위험(High). Quarterly PM 지연: 헤드 오버홀 미실시로 Membrane/Ring 열화 위험(Critical). PM 지연 판단은 엔지니어가 리스크 레벨에 따라 결정한다.

## 11. 예비 부품 관리

### 11.1 Critical Spare Parts 목록

Critical Spare Parts(CSP)는 재고 부족 시 장비 가동 중단을 초래하는 부품이다. CSP 목록: Polishing Pad, Retaining Ring, Membrane, Conditioner Disk, Backing Film, Slurry Filter, Pressure Regulator, 진공 펌프 키트, 로봇 벨트/그리퍼. CSP는 항시 최소 재고를 유지한다.

### 11.2 발주 기준 (Min/Max 재고)

예비 부품 Min/Max 재고 기준이다.

| 부품 | Min | Max | 리드타임 |
|------|-----|-----|---------|
| Polishing Pad | 3매 | 10매 | 2주 |
| Retaining Ring | 2개 | 6개 | 4주 |
| Membrane | 2개 | 6개 | 4주 |
| Conditioner Disk | 1개 | 3개 | 6주 |
| Slurry Filter | 10개 | 30개 | 1주 |

Min 재고 도달 시 즉시 발주한다. 리드타임이 긴 부품은 여유 재고를 확보한다.

## 12. Post-PM Qualification

### 12.1 PM 유형별 Qual 항목

PM 유형별 Qualification 항목이다. Daily PM: Test wafer 1매(MRR, WIWNU). Weekly PM: Test wafer 3매(MRR, WIWNU, 결함). Monthly PM: Test wafer 5매(MRR, WIWNU, WTWNU, 결함, 에지 프로파일). Quarterly PM: 25매 연속(MRR, WIWNU, WTWNU, 결함, Dishing, 캘리브레이션 확인). Annual PM: Full Qualification(전 항목 + DOE 확인).

### 12.2 합격/불합격 기준 상세

Post-PM Qualification 합격/불합격 기준이다. MRR: 목표값 ± 10%(Daily~Weekly), ± 5%(Monthly 이상). WIWNU: < 5%(Daily~Weekly), < 3%(Monthly 이상). WTWNU: < 3%(Monthly 이상, 5매 기준). 결함: 스크래치 0개(전 PM 유형). 파티클: < 30개(Weekly), < 20개(Monthly 이상). 1항목이라도 불합격 시 원인 분석 후 재수행.
