# CMP 계측 및 검사 가이드

## 1. 두께 측정

### 1.1 측정 장비 (Thickness Measurement Tools)

CMP 전후 막 두께 측정에 사용하는 장비이다. Ellipsometer는 투명막(SiO2, SiN) 두께를 광학적으로 측정한다(정확도 ±1Å). 4-point probe는 금속막(Cu, W)의 면저항(Rs)을 측정하여 두께를 환산한다(Rs = ρ/t). 비접촉 Eddy Current는 금속막 두께를 비파괴 측정한다.

### 1.2 측정 포인트 맵 (Measurement Point Map)

웨이퍼 두께 맵은 측정 포인트 수에 따라 정밀도가 달라진다. 49점 맵: 양산 모니터링 표준, 센터 1점 + 동심원 4링(6+12+18+12점). 25점 맵: 일일 Qualification용, 센터 1점 + 3링. 9점 맵: 간이 확인용, 센터 1점 + 8점(십자+대각). Edge Exclusion은 3mm를 기본으로 설정한다.

### 1.3 MRR 계산 방법 (Material Removal Rate Calculation)

MRR(연마율)은 연마 전후 두께 차이를 연마 시간으로 나누어 계산한다. 공식: MRR(Å/min) = (Pre-thickness - Post-thickness) / Polish Time. 49점 측정 시 각 포인트별 MRR을 계산하고 평균 MRR과 표준편차를 구한다. 단위 환산: 1 nm = 10 Å, 1 μm = 10,000 Å.

## 2. 균일도 계산

### 2.1 WIWNU 계산 공식 (Within-Wafer Non-Uniformity)

WIWNU는 웨이퍼 내 두께 균일도를 나타내는 지표이다. 공식: WIWNU(%) = (σ / Mean) × 100. σ는 측정 포인트 두께(또는 MRR)의 표준편차이다. Mean은 전체 포인트의 평균값이다. 목표: WIWNU < 3%(1-sigma). 양산 경고 기준: WIWNU > 5%.

### 2.2 WTWNU 계산 공식 (Wafer-to-Wafer Non-Uniformity)

WTWNU는 웨이퍼 간 MRR 편차를 나타내는 지표이다. 연속 25매 처리 후 각 웨이퍼 평균 MRR로 계산한다. 공식: WTWNU(%) = (σ_wafer / Mean_wafer) × 100. 목표: WTWNU < 2%. 이 지표는 공정 안정성(Run Stability)을 평가하는 데 사용한다.

### 2.3 Range/Max-Min 계산 (Range Calculation)

Range는 측정 포인트 중 최대값과 최소값의 차이이다. 공식: Range = Max(thickness) - Min(thickness). 전체 Range와 함께 Center-Edge Range도 별도 산출한다. Center-Edge Range = |Center avg - Edge avg|. 이 값으로 프로파일 형태(Center-Fast/Slow)를 판단한다.

## 3. 결함 검사

### 3.1 KLA/Surfscan 사용법 (Defect Inspection Tool)

KLA Surfscan은 웨이퍼 표면 결함을 자동 검출하는 장비이다. 레이저 산란 방식으로 파티클, 스크래치, 피트를 감지한다. 검사 모드: Dark Field(파티클 감도 높음), Light Field(패턴 결함 감도 높음). 스캔 레시피: 해상도(픽셀 크기), 임계값(threshold), Edge Exclusion(3mm) 설정. 결과는 결함 맵과 결함 수(count)로 출력된다.

### 3.2 결함 분류 (Defect Classification)

CMP 후 발생하는 주요 결함 분류이다. Scratch(스크래치): 선형 결함, Micro(< 0.5μm)/Macro(> 0.5μm)로 구분. Particle(파티클): 점형 결함, 슬러리 잔여물 또는 외부 오염. Residue(잔여물): 막 형태의 잔류 물질, 불완전 세정. Pit(피트): 표면 함몰, 부식 또는 기포에 의해 발생.

### 3.3 결함 맵 해석 (Defect Map Interpretation)

결함 맵의 분포 패턴으로 원인을 추정한다. 방사형(Radial) 분포 → 컨디셔너 디스크 또는 패드 이상. 동심원(Concentric) 분포 → 플래튼 진동 또는 회전 이상. 랜덤(Random) 분포 → 슬러리 내 대형 입자. 에지(Edge) 집중 → Retaining Ring 이상 또는 에지 세정 부족.

### 3.4 결함 허용 기준 (Defect Acceptance Criteria)

공정별 결함 허용 기준이다. Oxide CMP: 총 결함 수 < 20개/웨이퍼(0.16μm 이상), 스크래치 0개. Cu CMP: 총 결함 수 < 30개/웨이퍼, Macro-Scratch 0개. STI CMP: 총 결함 수 < 15개/웨이퍼. 기준 초과 시 해당 로트를 Hold하고 원인 분석 후 재처리 또는 폐기 판정한다.

## 4. 표면 분석

### 4.1 AFM 표면 거칠기 (Atomic Force Microscopy)

AFM은 나노미터 수준의 표면 거칠기를 측정하는 장비이다. 측정 영역: 1×1μm ~ 10×10μm 스캔. Ra(산술 평균 거칠기): CMP 후 목표 Ra < 0.5nm. Rq(RMS 거칠기): Ra 대비 약 1.25배. 거칠기가 높으면 후속 공정(리소그래피, 증착)에 영향을 주므로 관리가 필요하다.

### 4.2 SEM 단면 분석 (Cross-Section SEM)

SEM 단면 분석은 Dishing과 Erosion을 정량적으로 측정하는 방법이다. FIB(Focused Ion Beam)로 단면을 가공한 후 SEM으로 촬영한다. Dishing: Cu 배선 중심부와 주변 산화막의 높이 차이를 측정(목표: < 500Å). Erosion: Dense pattern 영역의 산화막 두께 감소를 측정한다.

### 4.3 Profilometer 측정 (Step Height Measurement)

Profilometer는 표면 단차(Step Height)를 접촉식 또는 비접촉식으로 측정한다. CMP 전후 단차를 비교하여 평탄화율(Planarization Rate)을 산출한다. 평탄화율(%) = (1 - Step_after/Step_before) × 100. 목표: 평탄화율 > 90%. 패턴 크기별 평탄화율 차이를 확인한다.

## 5. EPD 신호 분석

### 5.1 Motor Current EPD 파형 해석

Motor Current EPD는 플래튼 또는 캐리어 모터의 전류/토크 변화로 종점을 감지한다. 연마 중 막질이 변하면 마찰 계수가 변하여 토크가 변한다. Cu → Barrier 전환 시 토크가 급격히 증가한다(Cu가 Barrier보다 연질). 종점 판정: 토크 변화율이 설정 임계값(기본 5%)을 초과하는 시점.

### 5.2 Optical EPD 파형 해석

Optical EPD는 레이저 또는 백색광의 반사율 변화로 막 두께를 실시간 측정한다. 투명막(SiO2)은 간섭 패턴(사인파)이 나타나며, 파형 주기로 두께를 계산한다. 금속막(Cu)은 반사율 급변으로 종점을 감지한다. 종점 후 Over-polish 시간(10~30초)을 추가하여 잔여 금속을 완전 제거한다.

## 6. 공정별 품질 판정 기준

### 6.1 Oxide CMP 스펙

Oxide CMP의 품질 판정 기준이다. MRR: 3500~4000 Å/min(목표 3800). WIWNU: < 3%. WTWNU: < 2%. 결함: < 20개/웨이퍼. 표면 거칠기: Ra < 0.5nm. 잔여 두께: 목표 ± 200Å. 스펙 이탈 시 해당 웨이퍼를 Hold하고 재연마 또는 폐기 판정한다.

### 6.2 Cu CMP 스펙

Cu CMP의 품질 판정 기준이다. Step 1 MRR: 5000~7000 Å/min. Step 2 MRR: 1000~2000 Å/min. Dishing: < 500Å(100μm line 기준). Erosion: < 300Å. WIWNU: < 5%(Cu는 패턴 의존성 큼). 결함: < 30개/웨이퍼. Corrosion: 없음. Post-CMP 세정 후 잔여 Cu 없음(Rs 측정 확인).

### 6.3 W CMP 스펙

W(Tungsten) CMP의 품질 판정 기준이다. MRR: 2000~3000 Å/min. WIWNU: < 4%. W Recess: < 300Å. 결함: < 20개/웨이퍼. 잔여 W: Via/Contact 외 영역에 잔여 W 없음(광학 검사). Over-polish 정도가 W Recess에 직접 영향을 주므로 EPD 정확도가 중요하다.

### 6.4 STI CMP 스펙

STI(Shallow Trench Isolation) CMP의 품질 판정 기준이다. 목표: SiN 스토퍼 노출 시 연마 중단. SiO2:SiN 선택비 > 30:1(Ceria 슬러리 사용). Oxide 잔여 두께: Active 영역 위 0 ± 200Å. WIWNU: < 4%. Dishing(Trench): < 300Å. 결함: < 15개/웨이퍼.

### 6.5 Out-of-Spec 대응 절차

품질 판정 기준을 벗어난 경우의 대응 절차이다. (1) 해당 웨이퍼/로트 즉시 Hold → (2) 재측정으로 측정 오류 배제 → (3) 이탈 항목별 원인 분석(MRR, 균일도, 결함, 토폴로지) → (4) 재연마 가능 여부 판단(잔여 두께 여유 확인) → (5) 재연마 또는 폐기 결정 → (6) 장비 파라미터 조정 후 Qualification → (7) 시정 조치 기록.
