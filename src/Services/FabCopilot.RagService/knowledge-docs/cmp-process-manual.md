# CMP 공정 매뉴얼

## 1. CMP 공정 개요

CMP(Chemical Mechanical Planarization)는 화학적 반응과 기계적 연마를 동시에 활용하여 웨이퍼 표면을 평탄화하는 공정이다.

### 1.1 공정 목적

CMP 공정의 주요 목적이다. 층간 절연막(ILD) 평탄화로 리소그래피 DOF를 확보한다. 금속 배선(Cu, W) 평탄화로 다층 배선 구조를 형성한다. STI(Shallow Trench Isolation) 평탄화로 소자 분리를 완성한다. 잔여 물질 제거를 위한 Over-polish를 수행한다.

### 1.2 Preston 방정식 (Preston Equation)

CMP 연마율(MRR)은 Preston 방정식으로 결정된다. 공식: MRR = Kp × P × V. Kp는 Preston 계수로 슬러리/패드/재료 조합에 의존한다. P는 연마 압력(psi)이다. V는 상대 속도(m/s)로 플래튼과 캐리어 회전속도의 함수이다. 압력과 속도를 높이면 MRR이 증가하나 결함 위험도 증가한다.

## 2. 공정 단계

### 2.1 웨이퍼 로딩 (Wafer Loading)

웨이퍼 로딩 절차이다. (1) FOUP에서 로봇 암으로 웨이퍼 픽업 → (2) 캐리어 헤드에 진공 척킹으로 장착(진공 압력 -600 mmHg 이하) → (3) 장착 확인 센서로 정위치 검증 → (4) 리테이닝 링이 웨이퍼 에지를 고정. 로딩 실패 시 A151 알람이 발생한다.

### 2.2 연마 (Polishing)

연마 단계 절차이다. (1) 슬러리 공급 시작(150~250 ml/min) → (2) 플래튼 회전 시작(60~120 rpm) → (3) 캐리어 헤드 하강 및 회전(50~100 rpm) → (4) Zone별 압력 인가(1.5~6.0 psi) → (5) EPD 신호 또는 설정 시간으로 종료 판단. In-situ 컨디셔닝은 연마와 동시에 수행한다.

### 2.3 린스 (Rinse)

연마 종료 후 린스 절차이다. (1) 슬러리 공급 중단 → (2) DI water 공급 시작(500 ml/min, 15초) → (3) 저압 연마로 잔여 슬러리 제거(압력 1.0 psi 이하) → (4) 캐리어 헤드 상승. 린스가 불충분하면 슬러리 잔여물에 의한 결함이 발생한다.

### 2.4 웨이퍼 언로딩 (Wafer Unloading)

웨이퍼 언로딩 절차이다. (1) 캐리어 헤드에서 웨이퍼 분리(Blow-off + 진공 해제) → (2) 로봇 암으로 클리너 모듈에 전달 → (3) 브러시 클리닝 + 메가소닉 세정 → (4) 스핀 드라이. 웨이퍼 분리 시 Back Pressure를 인가하여 흡착을 해제한다.

## 3. Zone 압력 설정

### 3.1 Zone 맵 (Zone Map)

CMP 헤드는 다중 Zone으로 구성되어 각 Zone별 독립 압력 제어가 가능하다.

| Zone | 위치 | 기능 | 설정 범위 |
|------|------|------|----------|
| Zone 1 | Center | 중심부 MRR 제어 | 2.0~4.0 psi |
| Zone 2 | Inner | 내부 링 영역 | 2.0~4.0 psi |
| Zone 3 | Middle | 중간 링 영역 | 2.5~4.5 psi |
| Zone 4 | Outer | 외부 링 영역 | 2.5~5.0 psi |
| Zone 5 | Edge | 에지 영역 | 3.0~6.0 psi |
| Retaining Ring | Ring | 링 압력 | 4.0~8.0 psi |

### 3.2 압력 설정 원칙

Zone 압력 설정 원칙이다. 균일도(WIWNU) 목표: < 3%(1-sigma). Center-to-Edge 프로파일은 Zone 1과 Zone 5의 압력 비율로 조정한다. 에지 과연마 방지: Retaining Ring 압력을 Zone 5 대비 1.2~1.5배로 설정한다. 새 패드 사용 시 초기 10매는 Zone 압력을 5% 낮게 설정한다(Break-in).

## 4. 슬러리 설정

### 4.1 슬러리 종류별 설정

슬러리 타입별 기본 설정값이다.

| 슬러리 타입 | 대상 물질 | Flow Rate | pH | 입자 크기 |
|------------|----------|-----------|-----|----------|
| Oxide 슬러리 | SiO2 | 200 ml/min | 10~11 | 50~200 nm |
| Metal 슬러리 | Cu | 150 ml/min | 3~4 | 30~100 nm |
| W 슬러리 | Tungsten | 180 ml/min | 2~3 | 50~150 nm |
| STI 슬러리 | SiO2(선택비) | 200 ml/min | 10~11 | 100~250 nm |

### 4.2 슬러리 관리

슬러리 관리 주요 사항이다. 공급 온도: 20~25°C 유지. 공급 라인 압력: 15~25 psi. 유효 기간: 개봉 후 최대 72시간. 사용 전 최소 30분 교반 필요. 결정화 방지를 위해 미사용 시 라인 DI water 플러시. 서로 다른 로트의 슬러리를 혼합하지 않는다.

## 5. EPD (End Point Detection)

### 5.1 EPD 방식

CMP 종점 감지(EPD) 방식이다. Motor Current 방식: 토크 변화로 층 전환을 감지한다. Optical 방식: 반사율 변화로 막 두께를 실시간 측정한다. 시간 기반: 고정 연마 시간으로 제어하며 EPD 백업용으로 사용한다. 양산에서는 EPD + 시간 백업을 병용하여 안정성을 확보한다.

### 5.2 EPD 판정 기준

EPD 판정 기준값이다. Motor current 변화율 > 5%이면 층 전환 감지로 판정한다. Optical signal 안정화 후 Over-polish 시간은 10~30초이다. EPD 미감지 시 최대 연마 시간 제한은 레시피 설정 시간의 150%이다. 알람 A140(EPD 미감지) 발생 시 시간 기반으로 자동 전환된다.

## 6. 레시피 파라미터 요약

### 6.1 Oxide CMP 표준 레시피

Oxide CMP 표준 레시피 파라미터이다.

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 93 rpm | 90~100 rpm |
| Carrier Speed | 87 rpm | 80~95 rpm |
| Zone 1~5 Pressure | 3.0 psi | 2.0~5.0 psi |
| Retaining Ring | 5.0 psi | 4.0~8.0 psi |
| Slurry Flow Rate | 200 ml/min | 150~250 ml/min |
| Polish Time | 60 sec | EPD 기반 |
| Conditioner Downforce | 5 lbf | 3~7 lbf |

### 6.2 Cu CMP 표준 레시피

Cu CMP 표준 레시피 파라미터이다.

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 80 rpm | 70~90 rpm |
| Carrier Speed | 75 rpm | 65~85 rpm |
| Zone 1~5 Pressure | 2.5 psi | 1.5~4.0 psi |
| Retaining Ring | 4.5 psi | 3.5~6.0 psi |
| Slurry Flow Rate | 150 ml/min | 120~200 ml/min |
| Polish Time | EPD + 15s | EPD 기반 |
| Conditioner Downforce | 5 lbf | 3~7 lbf |

## 7. 다단계 CMP 공정 (Multi-Step CMP)

### 7.1 Cu CMP 3-Step 공정 흐름

Cu CMP는 3단계로 수행한다. Step 1(Bulk Cu Removal): Barrier 위의 과잉 Cu를 고속 제거한다. Step 2(Barrier Removal): TaN/Ta Barrier 층을 제거한다. Step 3(Buff Polish): 잔여 잔류물 제거 및 표면 마무리. 각 Step은 별도 플래튼에서 수행하며, 슬러리도 Step별로 다르다.

### 7.2 Step 1: Bulk Cu 제거

Step 1은 Barrier 층 위의 과잉 Cu를 고속으로 제거하는 단계이다. 슬러리: Cu 전용(산성, pH 3~4). 압력: 2.5~3.5 psi(높은 MRR 확보). 속도: Platen 80 rpm / Carrier 75 rpm. MRR 목표: 5000~7000 Å/min. EPD로 Barrier 층 노출 시점을 감지하여 종료한다.

### 7.3 Step 2: Barrier (TaN/Ta) 제거

Step 2는 Barrier 층(TaN/Ta)을 제거하는 단계이다. 슬러리: Barrier 전용(알칼리성, pH 9~10). 압력: 1.5~2.5 psi(낮은 압력으로 Dishing 최소화). 속도: Platen 70 rpm / Carrier 65 rpm. MRR 목표: 500~1000 Å/min. Oxide 대비 Barrier 선택비가 핵심이다.

### 7.4 Step 3: Buff Polish

Step 3는 표면 마무리 및 잔류물 제거 단계이다. 슬러리: Buff 전용 또는 DI water + 저농도 슬러리. 압력: 1.0~2.0 psi(최소 압력). 속도: Platen 60 rpm / Carrier 55 rpm. 시간: 15~30초(시간 기반). Corrosion 방지를 위해 연마 후 즉시 린스한다.

### 7.5 스텝 전환 EPD 기준

Multi-Step CMP에서 스텝 전환 판정 기준이다. Step 1 → 2 전환: Motor Current EPD로 Cu/Barrier 경계 감지(토크 변화율 > 5%). Step 2 → 3 전환: Optical EPD로 Barrier/Oxide 경계 감지 또는 시간 기반. 각 스텝에서 Over-polish 시간을 설정하여 잔여물을 완전 제거한다.

## 8. 추가 레시피

### 8.1 W CMP 표준 레시피

W(Tungsten) CMP 표준 레시피이다.

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 85 rpm | 80~95 rpm |
| Carrier Speed | 80 rpm | 70~90 rpm |
| Zone 1~5 Pressure | 3.0 psi | 2.0~4.5 psi |
| Retaining Ring | 5.5 psi | 4.0~7.0 psi |
| Slurry Flow Rate | 180 ml/min | 150~220 ml/min |
| Polish Time | EPD + 20s | EPD 기반 |

W CMP 슬러리는 산성(pH 2~3)이며 산화제(H2O2)를 포함한다. W Recess를 최소화하기 위해 Over-polish 시간을 엄격히 관리한다.

### 8.2 Barrier CMP 표준 레시피

Barrier(TaN/Ta) CMP 레시피이다.

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 70 rpm | 60~80 rpm |
| Carrier Speed | 65 rpm | 55~75 rpm |
| Zone 1~5 Pressure | 2.0 psi | 1.5~3.0 psi |
| Retaining Ring | 3.5 psi | 3.0~5.0 psi |
| Slurry Flow Rate | 150 ml/min | 120~180 ml/min |
| Polish Time | EPD + 10s | EPD 기반 |

Barrier CMP는 낮은 압력으로 Dishing과 Erosion을 최소화한다. Oxide 대비 높은 Barrier 제거 선택비가 필수이다.

### 8.3 STI CMP 표준 레시피 (Ceria 슬러리)

STI CMP는 Ceria(CeO2) 슬러리를 사용하며 SiN 스토퍼에서 자동 정지하는 높은 선택비가 특징이다.

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 90 rpm | 85~100 rpm |
| Carrier Speed | 85 rpm | 75~95 rpm |
| Zone 1~5 Pressure | 2.5 psi | 2.0~4.0 psi |
| Retaining Ring | 4.5 psi | 3.5~6.0 psi |
| Slurry Flow Rate | 200 ml/min | 150~250 ml/min |
| Polish Time | EPD | Optical EPD |

SiO2:SiN 선택비 > 30:1을 활용하여 SiN 노출 시 연마가 자연 정지한다.

## 9. Post-CMP 세정

### 9.1 세정 시퀀스 (Cleaning Sequence)

Post-CMP 세정 시퀀스이다. (1) Brush Station 1: PVA 브러시 + 알칼리 세정액(NH4OH 희석)으로 슬러리 입자 제거 → (2) Brush Station 2: PVA 브러시 + DI water로 잔여 화학물질 제거 → (3) Megasonic Station: 메가소닉 진동(1MHz)으로 미세 파티클 제거 → (4) Spin Dryer: 고속 회전(3000 rpm)으로 건조.

### 9.2 세정 파라미터 (Cleaning Parameters)

Post-CMP 세정 파라미터이다. 브러시 압력: 1~3 psi(너무 높으면 스크래치 발생). 세정액 농도: NH4OH 0.1~1.0%(pH 9~10). DI water 유량: 500~1000 ml/min. Megasonic 파워: 20~50W. Spin Dry 시간: 30~60초. Cu CMP 후에는 Citric Acid 세정을 추가하여 Cu 부식을 방지한다.

### 9.3 세정 후 품질 확인

세정 후 품질 확인 항목이다. 파티클 검사(KLA): 잔여 파티클 < 20개/웨이퍼. Water Mark(물자국) 유무: Spin Dry 효과 확인. 잔여 슬러리: 광학 현미경으로 표면 확인. 잔여 금속(Cu CMP): Rs 측정으로 필드 영역 잔여 Cu 없음 확인. 세정 불량 시 재세정 또는 Brush 교체를 검토한다.

## 10. EPD 상세

### 10.1 Motor Current EPD 파형 해석

Motor Current EPD 파형 해석 가이드이다. 초기 안정 구간: 연마 시작 후 5~10초간 토크가 안정화된다. 연마 구간: 일정한 토크로 연마가 진행된다. 전환점(Endpoint): 막질 변화로 토크가 급변한다(Cu→Barrier: 토크 증가, Oxide 평탄화: 토크 감소). 과연마 구간: 전환점 이후 설정 시간만큼 추가 연마한다.

### 10.2 Optical EPD 파형 해석

Optical EPD 파형 해석 가이드이다. 투명막(SiO2) 연마 시 간섭 패턴이 사인파 형태로 나타난다. 사인파 한 주기는 특정 두께 제거에 해당한다(파장 의존). 금속막(Cu) 연마 시 반사율이 높은 상태에서 Barrier 노출 시 급격히 변한다. STI CMP에서는 SiN 노출 시 반사율 변화로 종점을 감지한다.

### 10.3 EPD 튜닝 방법

EPD 튜닝 방법이다. 임계값 조정: Motor Current 변화율 기본 5%, 미감지 시 3%로 하향, 오감지 시 7%로 상향. 이동 평균 필터: 노이즈 감소를 위해 3~5 포인트 이동 평균 적용. Over-polish 시간: 잔류물 완전 제거와 Dishing의 Trade-off, 5초 단위로 조정. Reference wafer로 튜닝 결과를 검증한다.
