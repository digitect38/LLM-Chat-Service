# CMP 소모품 Qualification 절차

## 1. Qualification 개요

### 1.1 목적 및 유형 (Purpose and Types)

CMP 소모품 Qualification은 신규 또는 교체된 소모품이 공정 품질 기준을 충족하는지 검증하는 절차이다. 유형: IQ(Incoming Quality, 입고 검사) — 물리적 스펙 확인. PQ(Performance Qualification) — 실제 연마 성능 검증. 대상 소모품: Polishing Pad, 슬러리, Conditioner Disk, Retaining Ring.

### 1.2 합격/불합격 기준 요약

소모품 Qualification 공통 합격/불합격 기준이다.

| 항목 | 합격 기준 | 불합격 시 조치 |
|------|----------|---------------|
| MRR | 목표값 ± 10% | 파라미터 조정 또는 반품 |
| WIWNU | < 5% | 패드/헤드 점검 |
| 결함(Scratch) | 0개 | 소모품 교체 또는 반품 |
| 결함(Particle) | < 20개/wf | 세정 강화 또는 소모품 교체 |

## 2. Polishing Pad Qualification

### 2.1 입고 검사 (Incoming Inspection)

패드 입고 시 검사 항목이다. 두께: 마이크로미터로 5점 측정, 스펙 ± 0.05mm 이내. 경도(Shore D): 경도계로 3점 측정, Oxide 패드 기준 50~60D. Groove 깊이: 깊이 게이지로 측정, 스펙 ± 0.05mm. 외관: 변색, 오염, 기포, 이물질 없음. 유효 기간: 제조일 기준 12개월 이내.

### 2.2 Break-in 절차 (Pad Break-in)

패드 부착 후 Break-in 절차이다. (1) 컨디셔너 Downforce 7 lbf로 20분간 컨디셔닝(DI water 공급) → (2) 패드 표면 균일한 거칠기 형성 확인 → (3) 슬러리 공급 후 더미 웨이퍼 5매 처리. Break-in이 불충분하면 초기 MRR이 불안정하고 WIWNU가 높게 나타난다.

### 2.3 성능 검증 (Performance Qualification)

패드 성능 검증 절차이다. Test wafer 5매를 연속 연마한 후 각 웨이퍼의 MRR과 WIWNU를 측정한다. 5매 평균 MRR이 목표값 ± 10% 이내여야 한다. 5매 모든 WIWNU가 5% 미만이어야 한다. 결함 검사(KLA): 5매 모두 스크래치 0개. 1매라도 불합격 시 추가 5매 재검증.

### 2.4 합격 기준

패드 Qualification 합격 기준 상세이다. MRR: 목표값 ± 10%(예: Oxide 3800 ± 380 Å/min). WIWNU: < 5%(양산 기준 < 3%보다 완화). 스크래치: 5매 전수 0개. 파티클: < 30개/웨이퍼(Break-in 직후 기준 완화). MRR 안정성: 5매 WTWNU < 5%. 합격 시 양산 투입 승인, 기록 보관.

## 3. 슬러리 Qualification

### 3.1 입고 검사 (Incoming Inspection)

슬러리 입고 시 검사 항목이다. pH: pH meter로 측정, 타입별 스펙 ± 0.3 이내(예: Oxide pH 10.5 ± 0.3). Particle Size: Particle analyzer로 D50 측정, 스펙 ± 15%. 농도(Solid Content): 비중계 또는 건조 중량법. 외관: 변색, 침전, 이물질 없음. 유효 기간: 제조일 기준 6개월 이내.

### 3.2 성능 검증 (Performance Qualification)

슬러리 성능 검증 절차이다. 기존 사용 중인 슬러리(레퍼런스)와 신규 로트 슬러리의 MRR을 비교한다. Test wafer 3매를 신규 슬러리로 연마하여 MRR 측정. 레퍼런스 MRR 대비 ± 10% 이내 확인. 결함 검사: 스크래치 0개, 파티클 < 20개/웨이퍼.

### 3.3 합격 기준

슬러리 Qualification 합격 기준이다. MRR: 레퍼런스 대비 ± 10%. pH: 타입별 스펙 이내. Particle Size D50: 스펙 ± 15%. 결함: 스크래치 0개, 파티클 < 20개/웨이퍼. 모든 항목 합격 시 양산 사용 승인. 1항목이라도 불합격 시 해당 로트 사용 보류.

### 3.4 로트 혼합 금지 규정

슬러리 로트 혼합 금지 규정이다. 서로 다른 제조 로트의 슬러리를 동일 탱크에 혼합하지 않는다. 로트 간 미세한 조성 차이로 입자 응집, pH 변동, MRR 편차가 발생할 수 있다. 탱크 교체 시 기존 로트를 완전히 소진하거나 배출한 후 신규 로트를 투입한다. 로트 번호를 탱크 라벨에 명확히 기록한다.

## 4. Conditioner Disk Qualification

### 4.1 입고 검사

컨디셔너 디스크 입고 시 검사 항목이다. 다이아몬드 입자 분포: 현미경으로 균일 분포 확인(탈락 부위 없음). 디스크 평탄도: 정반 위에서 흔들림 없음 확인. 외관: 오염, 녹, 변형 없음. 로트 번호 및 사양서 대조: 입자 크기, 밀도, 타입 확인.

### 4.2 성능 검증 (Pad Cut Rate Test)

컨디셔너 디스크 성능 검증 절차이다. Break-in Conditioning 5분 실행 후 패드 Cut Rate를 측정한다. Cut Rate: 컨디셔닝 전후 패드 두께 차이 / 컨디셔닝 시간. 기준: Cut Rate가 레퍼런스 디스크 대비 ± 20% 이내. 동시에 패드 표면에 비정상 스크래치가 없는지 확인한다.

### 4.3 합격 기준

컨디셔너 디스크 Qualification 합격 기준이다. Pad Cut Rate: 레퍼런스 대비 ± 20%. 패드 표면 스크래치: 비정상 패턴 없음. 디스크 장착 후 Arm Sweep 동작 정상. Test wafer 연마 시 MRR 정상 범위, 스크래치 0개. 합격 시 사용 시작 일시를 기록하고 수명 카운터를 초기화한다.

## 5. Retaining Ring Qualification

### 5.1 입고 검사

Retaining Ring 입고 시 검사 항목이다. 두께: 마이크로미터로 4점 측정, 균일도 확인(편차 < 0.1mm). 내경/외경: 스펙 ± 0.1mm 이내. 표면 상태: 버(burr), 스크래치, 변형 없음. 재질: PPS(Polyphenylene Sulfide) 또는 동등 재질 확인.

### 5.2 성능 검증

Retaining Ring 성능 검증 절차이다. Ring 장착 후 Pressure Hold Test 실행(전 Zone 합격 확인). Test wafer 3매 연마하여 에지 프로파일 확인. 에지 3mm 영역 두께 편차 < 100Å. WIWNU < 5%. 에지 과연마 또는 미연마 패턴이 없어야 한다.

### 5.3 합격 기준

Retaining Ring Qualification 합격 기준이다. Pressure Hold Test: 전 Zone 합격(30초간 압력 강하 < 0.3 psi). MRR: 목표값 ± 10%. WIWNU: < 5%. 에지 프로파일: 에지 3mm 편차 < 100Å. 결함: 스크래치 0개. 합격 시 장착 일시와 초기 두께를 기록한다.

## 6. Qualification 기록 관리

### 6.1 기록 항목 및 보관

Qualification 기록 관리 요구사항이다. 기록 항목: 소모품 종류/로트 번호, 검사 일시/수행자, 입고 검사 결과(측정값), 성능 검증 결과(MRR/WIWNU/결함), 합격/불합격 판정 및 승인자. 기록 보관: 전자 시스템에 입력(장비 로그), 종이 기록은 서명 후 파일링, 보관 기간 최소 2년. 불합격 이력은 공급업체 품질 평가에 반영한다.
