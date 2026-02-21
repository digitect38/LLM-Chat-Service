# CMP 지식문서 단위 테스트 결과 보고서

**테스트 일시**: 2026-02-21
**테스트 도구**: xUnit + FluentAssertions
**검증 방법**: `DocumentIngestor.ChunkMarkdown(512, 128)` — 프로덕션 동일 청킹 + `ExtractParentContext()` 섹션 검증
**검증 수준**: 3단계 (문서 → 장(Chapter) → 절(Section) → 내용(Line))

## 검증 로직

```csharp
// 1. 프로덕션 동일 ChunkMarkdown 사용
var chunks = DocumentIngestor.ChunkMarkdown(rawText, 512, 128);

// 2. 이중 키워드로 정확한 청크 식별
var chunk = chunks.FirstOrDefault(c => c.Contains(kw1) && c.Contains(kw2));

// 3. ExtractParentContext로 장/절 계층 검증
var ctx = DocumentIngestor.ExtractParentContext(chunk);
ctx.Should().Contain(expectedChapter);  // 장 확인
ctx.Should().Contain(expectedSection);  // 절 확인
```

---

## 테스트 요약

| # | 문서 | 테스트 | 통과 | 실패 | 점수 |
|---|------|--------|------|------|------|
| 1 | cmp-alarm-code-reference.md | 37 | 37 | 0 | **100%** |
| 2 | cmp-calibration-procedures.md | 26 | 26 | 0 | **100%** |
| 3 | cmp-consumable-qualification.md | 24 | 24 | 0 | **100%** |
| 4 | cmp-defect-analysis.md | 21 | 21 | 0 | **100%** |
| 5 | cmp-equipment-overview.md | 20 | 20 | 0 | **100%** |
| 6 | cmp-metrology-inspection.md | 26 | 26 | 0 | **100%** |
| 7 | cmp-safety-procedures.md | 22 | 22 | 0 | **100%** |
| | **합계** | **176** | **176** | **0** | **100%** |

---

## 1. cmp-alarm-code-reference.md (37 tests — PASS 37)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. 알람 분류 체계 | 1.1 심각도 분류 | Critical \| 적색 \| 즉시 정지 | `Critical`, `즉시 정지` | PASS |
| 2 | 1. 알람 분류 체계 | 1.1 심각도 분류 | High \| 황색 \| 30분 이내 | `High`, `30분 이내` | PASS |
| 3 | 1. 알람 분류 체계 | 1.2 알람 번호 체계 | A100~A109는 Critical 알람 | `A100~A109`, `Critical` | PASS |
| 4 | 2. Critical 알람 | A100 Emergency Stop | 비상 정지 버튼, 안전 인터록 작동 | `Emergency Stop`, `인터록` | PASS |
| 5 | 2. Critical 알람 | A101 Vacuum Failure | 진공 척킹 압력 -400 mmHg 미만 | `Vacuum Failure`, `-400 mmHg` | PASS |
| 6 | 2. Critical 알람 | A101 Vacuum Failure | 정상 기준 -600 mmHg 이하 | `-600 mmHg`, `정상` | PASS |
| 7 | 2. Critical 알람 | A102 Wafer Out of Position | 리테이닝 링 이탈, 두께 < 1.5mm | `Wafer Out of Position`, `1.5mm` | PASS |
| 8 | 2. Critical 알람 | A103 Interlock Violation | 안전 도어 개방, 커버 미장착 | `Interlock Violation`, `도어` | PASS |
| 9 | 2. Critical 알람 | A104 Chemical Leak | 슬러리/세정액 누출, MSDS | `Chemical Leak`, `MSDS` | PASS |
| 10 | 3. Platen/Motor 알람 | A110 Platen Overload | 토크 정상 범위의 150% 초과 | `Platen Overload`, `150%` | PASS |
| 11 | 3. Platen/Motor 알람 | A111 Speed Deviation | 설정값 대비 ±5 rpm 이상 | `Platen Speed Deviation`, `±5 rpm` | PASS |
| 12 | 3. Platen/Motor 알람 | A112 Carrier Speed | 캐리어 헤드 ± 3 rpm 이내 정상 | `Carrier Speed Deviation`, `± 3 rpm` | PASS |
| 13 | 3. Platen/Motor 알람 | A113 Motor Overcurrent | 정격의 120% 초과 | `Motor Overcurrent`, `120%` | PASS |
| 14 | 4. Head/Pressure 알람 | A120 Slurry Flow | 설정값 ±20% 이상, 필터 차압 < 10 psi | `Slurry Flow Abnormal`, `±20%` + `10 psi` | PASS |
| 15 | 4. Head/Pressure 알람 | A121 Slurry Pressure | 정상 범위 15~25 psi | `Slurry Pressure Abnormal`, `15~25 psi` | PASS |
| 16 | 4. Head/Pressure 알람 | A122 DI Water Flow | 공급 압력 기준 40~60 psi | `DI Water Flow Abnormal`, `40~60 psi` | PASS |
| 17 | 4. Head/Pressure 알람 | A123 Head Pressure | 설정값 ±15% 이상 | `Head Pressure Out of Range`, `±15%` | PASS |
| 18 | 4. Head/Pressure 알람 | A125 Retaining Ring | 링 두께 4점 측정 기준 > 2.0mm | `Retaining Ring Pressure`, `2.0mm` | PASS |
| 19 | 4. Head/Pressure 알람 | A127 Membrane Leak | 30초간 압력 강하 0.3 psi 초과 | `Membrane Leak`, `0.3 psi` + `30초` | PASS |
| 20 | 5. 온도 알람 | A130 Platen Temp | 정상 20~35°C, 냉각수 18~22°C | `Platen Temperature Abnormal`, `20~35°C` + `18~22°C` | PASS |
| 21 | 5. 온도 알람 | A131 Slurry Temp | 정상 범위 20~25°C | `Slurry Temperature Abnormal`, `20~25°C` | PASS |
| 22 | 5. 온도 알람 | A132 Motor Temp | 80°C 초과 시 모터 권선 소손 위험 | `Motor Temperature Abnormal`, `80°C` | PASS |
| 23 | 6. EPD 알람 | A140 EPD Not Detected | EPD 신호 미감지, 시간 기반 백업 | `EPD Not Detected`, `시간 기반` | PASS |
| 24 | 6. EPD 알람 | A141 EPD Sensor Failure | 센서 고장, 케이블 불량 | `EPD Sensor Failure`, `케이블` | PASS |
| 25 | 6. EPD 알람 | A142 Abnormal Pattern | EPD 신호 파형 기대 패턴과 상이 | `EPD Abnormal Pattern`, `파형` | PASS |
| 26 | 7. 로봇/이송 알람 | A150 Robot Motion Error | 정상 경로 이탈, 타임아웃 | `Robot Motion Error`, `타임아웃` | PASS |
| 27 | 7. 로봇/이송 알람 | A152 FOUP Door Error | FOUP 도어 개폐 비정상, 래치 | `FOUP Door Error`, `래치` | PASS |
| 28 | 7. 로봇/이송 알람 | A153 Collision Detected | 충돌 센서 작동, 파손 점검 | `Collision Detected`, `충돌 센서` | PASS |
| 29 | 8. 컨디셔너 알람 | A200 Conditioner Error | 비정상 동작, MRR 저하 위험 | `Conditioner Error`, `MRR` | PASS |
| 30 | 8. 컨디셔너 알람 | A201 Pressure Abnormal | Downforce 설정값 ±20% | `Conditioner Pressure Abnormal`, `±20%` | PASS |
| 31 | 8. 컨디셔너 알람 | A202 Arm Position Error | Sweep 위치 설정 범위 이탈 | `Arm Position Error`, `Sweep` | PASS |
| 32 | 9. 유틸리티 알람 | A210 DI Water Supply | 정상 범위 40~60 psi | `DI Water Supply Abnormal`, `40~60 psi` | PASS |
| 33 | 9. 유틸리티 알람 | A211 CDA Pressure | 정상 범위 60~80 psi | `CDA Pressure Abnormal`, `60~80 psi` | PASS |
| 34 | 9. 유틸리티 알람 | A212 Exhaust Flow | 기준 0.3~0.5 m/s face velocity | `Exhaust Flow Abnormal`, `0.3~0.5 m/s` | PASS |
| 35 | 9. 유틸리티 알람 | A213 Cooling Water | 냉각수 유량 설정값 ±20% | `Cooling Water Flow Abnormal`, `±20%` | PASS |
| 36 | 10. 소모품 수명 | A300 Pad Life | 기본 500시간의 90% | `Pad Life Warning`, `500시간` + `90%` | PASS |
| 37 | 10. 소모품 수명 | A301 Conditioner Disk | 기본 200시간 | `Conditioner Disk Life`, `200시간` | PASS |
| 38 | 10. 소모품 수명 | A302 Retaining Ring | 두께 > 2.0mm, 1.5mm 미만 즉시 교체 | `Retaining Ring Life`, `2.0mm` + `1.5mm` | PASS |
| 39 | 10. 소모품 수명 | A303 Slurry Expiry | 기본 72시간, pH/입자 크기 측정 | `Slurry Expiry Warning`, `72시간` | PASS |
| 40 | — Prompt 통합 | — | Emergency Stop 프롬프트 포함 | `Emergency Stop`, `FileName` | PASS |
| 41 | — Prompt 통합 | — | Membrane Leak 0.3 psi, 30초 | `0.3 psi`, `30초` | PASS |

---

## 2. cmp-calibration-procedures.md (26 tests — PASS 26)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. 캘리브레이션 개요 | 1.1 목적 및 주기 | 압력 \| Monthly \| ± 0.1 psi | `Monthly`, `± 0.1 psi` | PASS |
| 2 | 1. 캘리브레이션 개요 | 1.1 목적 및 주기 | 유량 \| Monthly \| ± 5% | `Monthly`, `± 5%` | PASS |
| 3 | 1. 캘리브레이션 개요 | 1.1 목적 및 주기 | 온도 \| Quarterly \| ± 1°C | `Quarterly`, `± 1°C` | PASS |
| 4 | 1. 캘리브레이션 개요 | 1.1 목적 및 주기 | 속도(RPM) \| Quarterly \| ± 1 rpm | `Quarterly`, `± 1 rpm` | PASS |
| 5 | 1. 캘리브레이션 개요 | 1.2 필요 장비 | NIST 추적 가능 압력 게이지 ± 0.05 psi | `NIST`, `± 0.05 psi` | PASS |
| 6 | 1. 캘리브레이션 개요 | 1.2 필요 장비 | 교정된 RTD 센서 ± 0.1°C | `RTD`, `± 0.1°C` | PASS |
| 7 | 2. 압력 캘리브레이션 | 2.1 Head Zone | 1.0, 2.0, 3.0, 4.0, 5.0 psi 단계별 | `1.0, 2.0, 3.0, 4.0, 5.0 psi` | PASS |
| 8 | 2. 압력 캘리브레이션 | 2.2 Retaining Ring | 4.0~8.0 psi, 에지 균일도 영향 | `4.0~8.0 psi`, `에지 균일도` | PASS |
| 9 | 2. 압력 캘리브레이션 | 2.3 Back Pressure | -5 ~ +10 psi (진공~양압) | `-5 ~ +10 psi`, `진공` | PASS |
| 10 | 2. 압력 캘리브레이션 | 2.4 Pressure Hold Test | 30초간 압력 변화, 합격 < 0.3 psi | `30초`, `0.3 psi` | PASS |
| 11 | 3. 유량 캘리브레이션 | 3.1 슬러리 유량계 | 중량법, 60초간 배출량 수집 | `중량법`, `60초` | PASS |
| 12 | 3. 유량 캘리브레이션 | 3.2 DI Water | 200, 500, 1000 ml/min 3개 포인트 | `200, 500, 1000 ml/min` | PASS |
| 13 | 4. 온도 캘리브레이션 | 4.1 플래튼 온도 | 온도 안정화 대기 10분 | `안정화 대기`, `10분` | PASS |
| 14 | 4. 온도 캘리브레이션 | 4.2 슬러리 온도 | ±2°C → MRR ±5% 변동 | `±2°C`, `±5%` | PASS |
| 15 | 5. 속도 캘리브레이션 | 5.1 플래튼 RPM | 60, 80, 100, 120 rpm 단계별 | `60, 80, 100, 120 rpm` | PASS |
| 16 | 5. 속도 캘리브레이션 | 5.2 캐리어 RPM | 50, 75, 100 rpm 3개 포인트 | `50, 75, 100 rpm` | PASS |
| 17 | 5. 속도 캘리브레이션 | 5.3 컨디셔너 Arm | Sweep 범위 편차 ± 5mm | `Sweep`, `± 5mm` | PASS |
| 18 | 6. 센서 캘리브레이션 | 6.1 EPD 센서 | Reference wafer (알려진 막 두께) | `Reference wafer`, `EPD` | PASS |
| 19 | 6. 센서 캘리브레이션 | 6.2 토크 센서 | 무부하 상태 0 ± 0.1 N·m | `토크 센서`, `0.1 N·m` | PASS |
| 20 | 6. 센서 캘리브레이션 | 6.3 Vacuum 센서 | -200, -400, -600 mmHg, ± 20 mmHg | `-200, -400, -600 mmHg`, `± 20 mmHg` | PASS |
| 21 | 7. 로봇 캘리브레이션 | 7.1 티칭 절차 | 수동 Teach 모드, 좌표 X, Y, Z, θ | `Teach`, `X, Y, Z` | PASS |
| 22 | 7. 로봇 캘리브레이션 | 7.2 위치 정확도 | 반복 정밀도 < 0.2mm, 정확도 < ± 0.5mm | `± 0.5mm`, `0.2mm` | PASS |
| 23 | 8. 캘리브레이션 기록 | 8.1 PASS/FAIL 기준 | Zone 압력 ± 0.1 psi, 레귤레이터 조정 | `PASS`, `레귤레이터 조정` | PASS |
| 24 | 8. 캘리브레이션 기록 | 8.2 보관 기간 | 최소 3년, 교정 이력 | `3년`, `교정 이력` | PASS |
| 25 | — Prompt 통합 | — | 캘리브레이션 정보 프롬프트 포함 | `± 0.1 psi`, `FileName` | PASS |

---

## 3. cmp-consumable-qualification.md (24 tests — PASS 24)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. Qualification 개요 | 1.1 목적 및 유형 | IQ(Incoming Quality), PQ(Performance) | `IQ`, `PQ` | PASS |
| 2 | 1. Qualification 개요 | 1.2 합격/불합격 | MRR 목표값 ± 10%, 반품 | `± 10%`, `반품` | PASS |
| 3 | 1. Qualification 개요 | 1.2 합격/불합격 | WIWNU < 5% | `< 5%`, `WIWNU` | PASS |
| 4 | 1. Qualification 개요 | 1.2 합격/불합격 | 결함(Scratch) 0개 | `Scratch`, `0개` | PASS |
| 5 | 2. Polishing Pad Qual | 2.1 입고 검사 | 두께 5점 측정, ± 0.05mm | `5점 측정`, `± 0.05mm` | PASS |
| 6 | 2. Polishing Pad Qual | 2.1 입고 검사 | 경도 Shore D 50~60D | `Shore D`, `50~60D` | PASS |
| 7 | 2. Polishing Pad Qual | 2.1 입고 검사 | 유효 기간 12개월 | `12개월`, `유효` | PASS |
| 8 | 2. Polishing Pad Qual | 2.2 Break-in | Downforce 7 lbf, 20분, 더미 5매 | `7 lbf`, `20분` + `더미 웨이퍼 5매` | PASS |
| 9 | 2. Polishing Pad Qual | 2.3 성능 검증 | Test wafer 5매, MRR ± 10% | `Test wafer 5매`, `± 10%` | PASS |
| 10 | 2. Polishing Pad Qual | 2.4 합격 기준 | Oxide 3800 ± 380 Å/min | `3800`, `380` | PASS |
| 11 | 3. 슬러리 Qual | 3.1 입고 검사 | pH 10.5 ± 0.3 | `pH 10.5`, `± 0.3` | PASS |
| 12 | 3. 슬러리 Qual | 3.1 입고 검사 | D50 측정, ± 15% | `D50`, `± 15%` | PASS |
| 13 | 3. 슬러리 Qual | 3.1 입고 검사 | 유효 기간 6개월 | `6개월`, `유효` | PASS |
| 14 | 3. 슬러리 Qual | 3.4 혼합 금지 | 다른 로트 슬러리 혼합 금지 | `혼합`, `로트` | PASS |
| 15 | 4. Conditioner Disk Qual | 4.1 입고 검사 | 다이아몬드 입자 현미경 확인 | `다이아몬드`, `현미경` | PASS |
| 16 | 4. Conditioner Disk Qual | 4.2 성능 검증 | Cut Rate 레퍼런스 ± 20% | `Cut Rate`, `± 20%` | PASS |
| 17 | 5. Retaining Ring Qual | 5.1 입고 검사 | 두께 4점 측정, 편차 < 0.1mm | `4점 측정`, `0.1mm` | PASS |
| 18 | 5. Retaining Ring Qual | 5.1 입고 검사 | PPS (Polyphenylene Sulfide) | `PPS`, `Polyphenylene Sulfide` | PASS |
| 19 | 5. Retaining Ring Qual | 5.2 성능 검증 | 에지 3mm 영역 두께 편차 < 100Å | `에지 3mm`, `100Å` | PASS |
| 20 | 5. Retaining Ring Qual | 5.3 합격 기준 | Pressure Hold Test 30초, < 0.3 psi | `Pressure Hold Test`, `0.3 psi` | PASS |
| 21 | 6. 기록 관리 | 6.1 보관 기간 | 최소 2년 | `2년`, `보관` | PASS |
| 22 | — Prompt 통합 | — | Qualification 정보 프롬프트 포함 | `± 10%`, `FileName` | PASS |

---

## 4. cmp-defect-analysis.md (21 tests — PASS 21)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. 결함 분류 | 1.1 스크래치 유형 | Micro-Scratch 폭 < 0.5μm, 현미경 | `Micro-Scratch`, `0.5μm` | PASS |
| 2 | 1. 결함 분류 | 1.1 스크래치 유형 | Chatter Mark 등간격 반복, 진동 | `Chatter Mark`, `진동` | PASS |
| 3 | 1. 결함 분류 | 1.3 부식/변색 | BTA(Benzotriazole) 방식제 | `BTA`, `Benzotriazole` | PASS |
| 4 | 1. 결함 분류 | 1.4 토폴로지 | Dishing: Cu 배선 오목하게 연마 | `Dishing`, `오목하게` | PASS |
| 5 | 2. 결함 패턴 분석 | 2.1 방사형 | 컨디셔너 다이아몬드 탈락 | `방사형`, `다이아몬드 탈락` | PASS |
| 6 | 2. 결함 패턴 분석 | 2.2 동심원 | 플래튼 진동, TIR > 25μm | `동심원`, `25μm` | PASS |
| 7 | 2. 결함 패턴 분석 | 2.3 랜덤 | 슬러리 대형 입자(응집체) | `랜덤`, `응집체` | PASS |
| 8 | 2. 결함 패턴 분석 | 2.4 에지 집중 | Retaining Ring 편마모 | `에지`, `편마모` | PASS |
| 9 | 2. 결함 패턴 분석 | 2.5 Zone별 | Membrane 불균일, Backing Film 박리 | `Membrane`, `Backing Film` | PASS |
| 10 | 3. 결함별 상세 분석 | 3.1 Cu Dishing | Wide Line > 10μm → Dishing 크게 | `Wide Line`, `10μm` | PASS |
| 11 | 3. 결함별 상세 분석 | 3.2 Oxide Erosion | 패턴 밀도 > 50% → Erosion | `Erosion`, `50%` | PASS |
| 12 | 3. 결함별 상세 분석 | 3.3 Cu Corrosion | 린스 지연 > 30초, BTA 방식제 | `30초`, `BTA` | PASS |
| 13 | 3. 결함별 상세 분석 | 3.4 Post-CMP Residue | SEM/EDX, TOF-SIMS 분석 | `TOF-SIMS`, `SEM` | PASS |
| 14 | 4. 결함 개선 사례 | 4.1 Scratch Zero | 2단 필터(1.0+0.5μm) → 스크래치 0% | `2단 필터`, `0%` | PASS |
| 15 | 4. 결함 개선 사례 | 4.2 Dishing 개선 | 800Å → 350Å, EPD 튜닝 | `800Å`, `350Å` | PASS |
| 16 | 4. 결함 개선 사례 | 4.3 균일도 개선 | Zone 1 압력 3.0 psi → WIWNU 2.8% | `3.0 psi`, `2.8%` | PASS |
| 17 | 5. 결함 보고 | 5.1 8D 보고서 | 체계적 문제 해결 8단계 문서 | `8D`, `8단계` | PASS |
| 18 | 5. 결함 보고 | 5.2 Fishbone | Ishikawa, 6대 원인 범주 Man/Machine | `Fishbone`, `Man` | PASS |
| 19 | 5. 결함 보고 | 5.3 CAPA | CAPA 기록 최소 3년 보관 | `CAPA`, `3년` | PASS |
| 20 | — Prompt 통합 | — | 결함 패턴 정보 프롬프트 포함 | `방사형`, `FileName` | PASS |

---

## 5. cmp-equipment-overview.md (20 tests — PASS 20)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. 장비 전체 구성 | 1.1 장비 레이아웃 | 풋프린트 3m × 5m, Platen 3개 | `3m × 5m`, `Platen 3개` | PASS |
| 2 | 1. 장비 전체 구성 | 1.1 장비 레이아웃 | FOUP Load Port 2~3개 | `FOUP Load Port`, `2~3개` | PASS |
| 3 | 2. Polishing Module | 2.1 Platen | 직경 600~800mm, TIR < 25 μm | `600~800mm`, `25 μm` | PASS |
| 4 | 2. Polishing Module | 2.1 Platen | 속도 20~150 rpm, 냉각수 18~22°C | `20~150 rpm`, `18~22°C` | PASS |
| 5 | 2. Polishing Module | 2.2 Carrier Head | 5~7개 Zone, 진공 -600 mmHg | `5~7개`, `-600 mmHg` | PASS |
| 6 | 2. Polishing Module | 2.3 Polishing Pad | IC1000, Groove 0.3~0.5mm | `IC1000`, `0.3~0.5mm` | PASS |
| 7 | 2. Polishing Module | 2.4 Conditioner | 다이아몬드 100mm 디스크, 3~7 lbf | `100mm`, `3~7 lbf` | PASS |
| 8 | 3. Slurry Delivery | 3.1 슬러리 탱크 | 20~50 리터, 교반기 | `20~50 리터`, `교반기` | PASS |
| 9 | 3. Slurry Delivery | 3.2 공급 펌프 | 15~25 psi, 필터 0.5~1.0 μm, 차압 < 10 psi | `15~25 psi`, `0.5~1.0 μm` + `10 psi` | PASS |
| 10 | 4. Wafer Handling | 4.1 로봇 암 | 위치 정확도 ± 0.5mm, 반복 ± 0.2mm | `± 0.5mm`, `± 0.2mm` | PASS |
| 11 | 4. Wafer Handling | 4.2 FOUP Load Port | 300mm 웨이퍼 25매 수납 | `25매`, `FOUP` | PASS |
| 12 | 4. Wafer Handling | 4.3 웨이퍼 정렬기 | 정렬 정확도 ± 0.1° | `± 0.1°`, `정렬` | PASS |
| 13 | 5. Cleaning Module | 5.1 Brush Station | PVA 브러시, 100~300 rpm | `PVA`, `100~300 rpm` | PASS |
| 14 | 5. Cleaning Module | 5.2 Megasonic | 700kHz~1MHz, 20~50W | `700kHz~1MHz`, `20~50W` | PASS |
| 15 | 5. Cleaning Module | 5.3 Spin Dryer | 2000~4000 rpm, N2 블로우 | `2000~4000 rpm`, `N2` | PASS |
| 16 | 6. 제어 시스템 | 6.1 PLC/Software | PLC, EtherCAT 통신 | `PLC`, `EtherCAT` | PASS |
| 17 | 6. 제어 시스템 | 6.3 통신 인터페이스 | SECS/GEM, EDA(SEMI E134) | `SECS/GEM`, `EDA` | PASS |
| 18 | — Prompt 통합 | — | 장비 개요 프롬프트 포함 | `Polishing Module`, `FileName` | PASS |

---

## 6. cmp-metrology-inspection.md (26 tests — PASS 26)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. 두께 측정 | 1.1 측정 장비 | Ellipsometer SiO2/SiN ±1Å | `Ellipsometer`, `±1Å` | PASS |
| 2 | 1. 두께 측정 | 1.1 측정 장비 | 4-point probe 금속막 Rs | `4-point probe`, `Rs` | PASS |
| 3 | 1. 두께 측정 | 1.2 측정 포인트 | 49점 맵, Edge Exclusion 3mm | `49점 맵`, `3mm` | PASS |
| 4 | 1. 두께 측정 | 1.2 측정 포인트 | 25점 맵 일일 Qualification | `25점 맵`, `Qualification` | PASS |
| 5 | 1. 두께 측정 | 1.3 MRR 계산 | MRR(Å/min) = (Pre - Post) / Time | `Å/min`, `Pre-thickness` | PASS |
| 6 | 2. 균일도 계산 | 2.1 WIWNU | WIWNU(%) = (σ / Mean) × 100, < 3% | `WIWNU`, `< 3%` | PASS |
| 7 | 2. 균일도 계산 | 2.2 WTWNU | WTWNU 목표 < 2% | `WTWNU`, `< 2%` | PASS |
| 8 | 2. 균일도 계산 | 2.3 Center-Edge Range | \|Center avg - Edge avg\| | `Center-Edge Range`, `Center avg` | PASS |
| 9 | 3. 결함 검사 | 3.1 Surfscan | KLA Dark Field/Light Field | `KLA`, `Dark Field` | PASS |
| 10 | 3. 결함 검사 | 3.4 결함 허용 기준 | Oxide CMP < 20개/웨이퍼, 스크래치 0 | `< 20개`, `스크래치 0개` | PASS |
| 11 | 3. 결함 검사 | 3.4 결함 허용 기준 | Cu CMP < 30개/웨이퍼 | `< 30개`, `Cu CMP` | PASS |
| 12 | 3. 결함 검사 | 3.4 결함 허용 기준 | STI CMP < 15개/웨이퍼 | `< 15개`, `STI` | PASS |
| 13 | 4. 표면 분석 | 4.1 AFM | Ra < 0.5nm | `AFM`, `0.5nm` | PASS |
| 14 | 4. 표면 분석 | 4.2 SEM 단면 | FIB 단면 가공, Dishing < 500Å | `FIB`, `500Å` | PASS |
| 15 | 4. 표면 분석 | 4.3 Profilometer | 평탄화율 > 90% | `Profilometer`, `90%` | PASS |
| 16 | 5. EPD 신호 분석 | 5.1 Motor Current | 토크 변화율 임계값 5% | `Motor Current EPD`, `5%` | PASS |
| 17 | 5. EPD 신호 분석 | 5.2 Optical EPD | Over-polish 10~30초 | `Optical EPD`, `10~30초` | PASS |
| 18 | 6. 품질 판정 기준 | 6.1 Oxide CMP | MRR 3800, WIWNU < 3% | `3800`, `< 3%` | PASS |
| 19 | 6. 품질 판정 기준 | 6.2 Cu CMP | MRR 5000~7000, Dishing < 500Å | `5000~7000`, `500Å` | PASS |
| 20 | 6. 품질 판정 기준 | 6.3 W CMP | MRR 2000~3000, W Recess < 300Å | `2000~3000`, `300Å` | PASS |
| 21 | 6. 품질 판정 기준 | 6.4 STI CMP | SiO2:SiN 선택비 > 30:1, Ceria | `30:1`, `Ceria` | PASS |
| 22 | 6. 품질 판정 기준 | 6.5 Out-of-Spec | 해당 웨이퍼/로트 즉시 Hold | `Hold`, `재측정` | PASS |
| 23 | — Prompt 통합 | — | WIWNU 정보 프롬프트 포함 | `WIWNU`, `FileName` | PASS |

---

## 7. cmp-safety-procedures.md (22 tests — PASS 22)

| # | 장(Chapter) | 절(Section) | 기대 내용 (Expected Line) | 검증 키워드 | 결과 |
|---|------------|-------------|--------------------------|------------|------|
| 1 | 1. 일반 안전 수칙 | 1.1 PPE | Bunny Suit, 니트릴 장갑 | `Bunny Suit`, `니트릴` | PASS |
| 2 | 1. 일반 안전 수칙 | 1.2 클린룸 안전 | 1인 작업 금지, 2인 1조 | `2인 1조`, `1인 작업 금지` | PASS |
| 3 | 1. 일반 안전 수칙 | 1.3 작업 전 안전 | MSDS 비치, 비상 샤워/세안기 | `MSDS`, `비상 샤워` | PASS |
| 4 | 2. 화학물질 안전 | 2.1 슬러리 취급 | Oxide pH 10~11, Cu pH 3~4 | `pH 10~11`, `pH 3~4` | PASS |
| 5 | 2. 화학물질 안전 | 2.1 슬러리 취급 | 피부 접촉 시 15분 세척 | `15분`, `세척` | PASS |
| 6 | 2. 화학물질 안전 | 2.2 세정 화학물질 | HF(불산) 극독성, 칼슘 글루코네이트 겔 | `HF`, `칼슘 글루코네이트` | PASS |
| 7 | 2. 화학물질 안전 | 2.3 유출 대응 | 소량 < 500 ml, 흡착재 | `500 ml`, `흡착재` | PASS |
| 8 | 2. 화학물질 안전 | 2.4 폐액 처리 | 80% 차면 환경안전팀 수거 요청 | `80%`, `수거` | PASS |
| 9 | 3. 전기 안전 | 3.1 LOTO | Lock-Out/Tag-Out, 자물쇠 설치 | `Lock-Out/Tag-Out`, `자물쇠` | PASS |
| 10 | 3. 전기 안전 | 3.2 전기 배선 점검 | 절연 공구, 콘덴서 방전 5분 | `절연 공구`, `5분` | PASS |
| 11 | 4. 기계 안전 | 4.1 회전 부품 | Platen 60~120 rpm, Carrier 50~100 rpm | `60~120 rpm`, `50~100 rpm` | PASS |
| 12 | 4. 기계 안전 | 4.3 중량물 | 캐리어 헤드 15~25 kg, 20 kg 이상 2인 | `15~25 kg`, `20 kg` | PASS |
| 13 | 5. 비상 대응 | 5.1 Emergency Stop | E-Stop 전면/후면 각 1개 이상 | `E-Stop`, `전면` | PASS |
| 14 | 5. 비상 대응 | 5.2 화재 | CO2 소화기, 전기 화재 물 사용 금지 | `CO2 소화기`, `물 사용 금지` | PASS |
| 15 | 5. 비상 대응 | 5.3 지진 | E-Stop 즉시, 슬러리 라인 누출 확인 | `지진`, `슬러리 라인` | PASS |
| 16 | 5. 비상 대응 | 5.4 정전 | DI water 플러시 | `정전`, `DI water 플러시` | PASS |
| 17 | 5. 비상 대응 | 5.5 사고 보고 | 24시간 이내 보고서, Near Miss 보고 | `24시간`, `Near Miss` | PASS |
| 18 | 6. 안전 교육 | 6.1 필수 교육 | LOTO 교육 연 1회, 비상 훈련 연 2회 | `LOTO 교육`, `연 2회` | PASS |
| 19 | 6. 안전 교육 | 6.2 교육 갱신 주기 | 정기 교육 분기 6시간, 기록 3년 보관 | `6시간`, `3년` | PASS |
| 20 | — Prompt 통합 | — | PPE 정보 프롬프트 포함 | `PPE`, `FileName` | PASS |
| 21 | — Prompt 통합 | — | LOTO 상세 프롬프트 포함 | `LOTO` | PASS |

---

## 검증 방법론 설명

### 기존 방식 (v1 — 폐기)
```
ChunkText(512, 128) → AnyChunkContains("keyword")
```
- 문서 구조(장/절) 무시
- 키워드 하나만 확인 → 오탐 가능
- 프로덕션과 다른 청킹 방식 사용

### 신규 방식 (v2 — 현재)
```
ChunkMarkdown(512, 128) → FindChunk(kw1, kw2) → AssertContext(chapter, section)
```
- **프로덕션 동일 청킹**: `ChunkMarkdown()` 사용 (프로덕션 IngestTextAsync와 동일)
- **이중 키워드 검색**: 두 개의 키워드로 정확한 청크 식별
- **3단계 계층 검증**:
  1. 청크 존재 확인 (FindChunk)
  2. 장(Chapter) 맥락 확인 (ExtractParentContext → chapter)
  3. 절(Section) 맥락 확인 (ExtractParentContext → section)
- **프롬프트 통합 테스트**: BuildSystemPrompt에 청크가 올바르게 포함되는지 검증

### 테스트 실행 명령

```bash
dotnet test tests/FabCopilot.RagPipeline.Tests/ \
  --filter "FullyQualifiedName~Content.CmpAlarmCodeReference|FullyQualifiedName~Content.CmpCalibrationProcedures|FullyQualifiedName~Content.CmpConsumableQualification|FullyQualifiedName~Content.CmpDefectAnalysis|FullyQualifiedName~Content.CmpEquipmentOverview|FullyQualifiedName~Content.CmpMetrologyInspection|FullyQualifiedName~Content.CmpSafetyProcedures"
```

**결과**: 176 tests — **ALL PASS (100%)**
