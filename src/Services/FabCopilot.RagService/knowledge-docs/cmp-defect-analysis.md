# CMP 결함 분석 가이드

## 1. 결함 분류

### 1.1 스크래치 유형 (Scratch Types)

CMP 공정에서 발생하는 스크래치 유형이다. Micro-Scratch: 폭 < 0.5μm, 현미경으로만 관찰, 슬러리 입자 또는 패드 이물질이 원인. Macro-Scratch: 폭 > 0.5μm, 육안 관찰 가능, 이물질 끼임 또는 Ring 파손이 원인. Chatter Mark: 등간격 반복 스크래치, 진동에 의해 발생. Arc Scratch: 원호 형태, 웨이퍼 회전과 관련된 원인.

### 1.2 잔여물 유형 (Residue Types)

CMP 후 잔여물 유형이다. Slurry Residue: 세정 부족으로 슬러리 입자가 잔류, 파티클 형태로 검출. Organic Residue: 유기물 오염, 패드/슬러리 성분 잔류. Metal Residue: Cu CMP 후 필드 영역에 잔류 Cu, Rs 측정으로 확인. Post-CMP 세정 강화 또는 세정 화학물질 농도 조정으로 개선한다.

### 1.3 부식/변색 (Cu Corrosion, Staining)

Cu CMP 후 발생하는 부식/변색 유형이다. Cu Corrosion: Cu 표면이 산화/용해되어 변색 또는 피트 발생. 원인: 슬러리 잔류(산성), 세정 지연, DI water 노출 시간 과다. Staining(변색): Cu 표면의 얼룩, 세정 후 건조 불균일이 원인. 방지: 연마 후 즉시 린스, Post-CMP 세정 시간 단축, BTA(Benzotriazole) 방식제 적용.

### 1.4 토폴로지 결함 (Topology Defects)

CMP 후 토폴로지(형상) 결함이다. Dishing: Cu 배선이 주변 Oxide보다 오목하게 연마됨, Wide line에서 심함. Erosion: Dense pattern 영역의 Oxide가 과도하게 연마됨. Recess: W Plug가 주변보다 낮게 연마됨. 이들은 후속 공정(리소그래피 DOF, 배선 저항)에 영향을 준다.

## 2. 결함 패턴 분석

### 2.1 방사형 패턴 → Pad/Conditioner

결함이 웨이퍼 중심에서 방사형(Radial)으로 분포하는 경우이다. 주요 원인: 컨디셔너 디스크의 다이아몬드 탈락 → 패드에 스크래치 생성, 패드 Groove에 이물질 끼임, 컨디셔너 Sweep 패턴 이상. 점검 순서: (1) 컨디셔너 디스크 표면 검사 → (2) 패드 표면 검사 → (3) 컨디셔너 교체 또는 패드 교체.

### 2.2 동심원 패턴 → Platen/진동

결함이 동심원(Concentric) 패턴으로 분포하는 경우이다. 주요 원인: 플래튼 진동(베어링 마모), 플래튼 표면 평탄도 이상(TIR > 25μm), 캐리어 헤드 진동. 점검 순서: (1) 플래튼 진동 측정 → (2) 플래튼 평탄도 확인 → (3) 베어링 상태 점검 → (4) 헤드 회전 밸런스 확인.

### 2.3 랜덤 분포 → Slurry 입자

결함이 웨이퍼 전면에 랜덤(Random)하게 분포하는 경우이다. 주요 원인: 슬러리 내 대형 입자(응집체), 슬러리 필터 파손/효율 저하, 외부 오염(클린룸 파티클). 점검 순서: (1) 슬러리 Particle Count 측정 → (2) 필터 교체 → (3) 슬러리 교체 → (4) 클린룸 환경 확인.

### 2.4 에지 집중 → Retaining Ring

결함이 웨이퍼 에지(Edge) 영역에 집중되는 경우이다. 주요 원인: Retaining Ring 마모(불균일 마모/편마모), Ring 표면 파손 또는 이물질, Ring 압력 설정 부적절. 점검 순서: (1) Ring 두께 4점 측정(편마모 확인) → (2) Ring 표면 검사 → (3) Ring 압력 확인 → (4) Ring 교체.

### 2.5 특정 영역 → Membrane/Backing Film

결함이 특정 Zone 영역에 집중되는 경우이다. 주요 원인: Membrane 불균일(특정 Zone 눌림/팽창), Backing Film 박리 또는 기포, 헤드 Air Channel 막힘. 점검 순서: (1) 해당 Zone Pressure Hold Test → (2) Membrane 육안 검사 → (3) Backing Film 상태 확인 → (4) 교체 후 재검증.

## 3. 결함별 상세 분석

### 3.1 Cu Dishing 분석 (패턴 밀도/선폭 의존성)

Cu Dishing은 패턴 밀도와 선폭에 의존한다. Wide Line(> 10μm): Dishing 크게 발생(슬러리가 Cu 표면에 직접 접촉). Narrow Line(< 1μm): Dishing 적음(패드 강성으로 보호). Dense Pattern: Erosion 동반. Isolated Pattern: Dishing 우세. 개선: Over-polish 시간 최소화, 저압 연마, High Selectivity 슬러리 사용.

### 3.2 Oxide Erosion 분석

Oxide Erosion은 Dense 패턴 영역에서 발생한다. 패턴 밀도가 높을수록(> 50%) Erosion이 심하다. 원인: 패드가 Dense 영역에서 더 많이 접촉하여 국부적 압력 상승. 측정: SEM 단면 분석으로 Dense/Isolated 영역 두께 차이 측정. 개선: 하드 패드 사용(패드 컴플라이언스 감소), Ceria 슬러리(STI), Over-polish 최소화.

### 3.3 Cu Corrosion 분석 (pH, 세정시간)

Cu Corrosion은 연마 후 Cu 표면이 화학적으로 부식되는 현상이다. 원인: 산성 슬러리(pH 3~4) 잔류, 린스/세정 지연(연마 후 > 30초 방치), DI water 장시간 노출, 세정액 pH 부적절. 분석: SEM으로 부식 형태(Pit, Void) 관찰, EDX로 산화물 확인. 방지: 연마 후 즉시 린스, 30초 이내 세정 시작, BTA 방식제 적용.

### 3.4 Post-CMP Residue 분석

Post-CMP 세정 후에도 잔여물이 남는 문제이다. 슬러리 잔여물: SiO2/Al2O3 입자가 표면에 부착, 브러시 압력 부족 또는 세정액 농도 저하가 원인. Organic Film: 유기 첨가제 잔류, IPA 린스 추가로 제거. Metal Residue: 필드 영역 잔류 Cu, 추가 Buff Polish 또는 세정 강화 필요. 분석 도구: SEM/EDX, TOF-SIMS.

## 4. 결함 개선 사례

### 4.1 스크래치 Zero 달성 사례

Micro-Scratch 반복 발생을 Zero로 개선한 사례이다. 문제: 슬러리 교체 후 Micro-Scratch 발생률 3%. 분석: 슬러리 Particle Size 분포에서 D99 > 500nm 대형 입자 검출. 원인: 슬러리 필터(0.5μm) 효율 저하. 조치: 필터 교체 주기 단축(주 1회 → 주 2회), 2단 필터 적용(1.0μm + 0.5μm). 결과: 스크래치 0% 달성 및 6개월 유지.

### 4.2 Dishing 개선 사례

Cu Dishing이 스펙(500Å)을 초과한 문제를 개선한 사례이다. 문제: 100μm Wide Line에서 Dishing 800Å. 분석: Over-polish 시간 30초로 과다. 조치: (1) EPD 튜닝으로 Over-polish 15초로 단축 → (2) Step 2 압력 2.5 → 2.0 psi로 하향 → (3) High Selectivity Barrier 슬러리로 변경. 결과: Dishing 350Å로 스펙 이내 달성.

### 4.3 균일도 개선 사례

WIWNU가 7%로 스펙(3%)을 초과한 문제를 개선한 사례이다. 문제: Center-Fast 프로파일, 센터부 MRR이 에지 대비 20% 높음. 분석: Zone 1 압력 3.5 psi로 과다, Retaining Ring 두께 1.8mm(마모). 조치: (1) Zone 1 압력 3.5 → 3.0 psi → (2) Ring 압력 4.5 → 5.5 psi → (3) Ring 교체(두께 < 2.0mm). 결과: WIWNU 2.8% 달성.

## 5. 결함 보고

### 5.1 8D 보고서 양식 (8D Report Format)

8D 보고서는 체계적 문제 해결을 위한 8단계 문서이다. D1: 팀 구성. D2: 문제 정의(What/Where/When/How many). D3: 긴급 조치(Containment). D4: 근본 원인 분석(Root Cause). D5: 영구 대책 수립(Permanent Corrective Action). D6: 대책 실행 및 검증. D7: 재발 방지(Preventive Action). D8: 팀 인정 및 종료.

### 5.2 Fishbone 분석 (Cause-and-Effect Diagram)

Fishbone(Ishikawa) 다이어그램은 결함 원인을 체계적으로 분류하는 도구이다. CMP 결함의 6대 원인 범주: Man(작업자 실수), Machine(장비 이상), Material(슬러리/패드), Method(레시피/절차), Measurement(계측 오류), Environment(온습도/클린룸). 각 범주에서 구체적 원인을 도출한 후 검증 실험으로 근본 원인을 특정한다.

### 5.3 CAPA (시정/예방 조치)

CAPA(Corrective and Preventive Action)는 결함의 재발을 방지하기 위한 체계이다. Corrective Action(시정 조치): 발생한 문제의 근본 원인을 제거하는 조치. Preventive Action(예방 조치): 잠재적 문제가 발생하지 않도록 사전 예방하는 조치. CAPA 절차: 문제 식별 → 원인 분석 → 조치 계획 → 실행 → 효과 검증 → 표준화(SOP 반영). CAPA 기록은 최소 3년 보관한다.
