# CMP 공정 매뉴얼

## 1. CMP 공정 개요

CMP(Chemical Mechanical Planarization)는 화학적 반응과 기계적 연마를 동시에 활용하여 웨이퍼 표면을 평탄화하는 공정이다.

### 1.1 공정 목적
- 층간 절연막(ILD) 평탄화
- 금속 배선(Cu, W) 평탄화
- STI(Shallow Trench Isolation) 평탄화
- 잔여 물질 제거 (over-polish)

### 1.2 Preston 방정식
CMP 연마율(MRR)은 다음 방정식으로 결정된다:

$$MRR = K_p \times P \times V$$

- $K_p$: Preston 계수 (슬러리/패드/재료 조합에 의존)
- $P$: 연마 압력 (psi)
- $V$: 상대 속도 (m/s) = 플래튼 회전속도와 캐리어 회전속도의 함수

## 2. 공정 단계

### 2.1 웨이퍼 로딩
1. FOUP에서 웨이퍼를 로봇 암으로 픽업
2. 웨이퍼를 캐리어 헤드에 진공 척킹으로 장착
3. 장착 확인 센서로 정위치 검증
4. 리테이닝 링이 웨이퍼 에지를 고정

### 2.2 연마 (Polishing)
1. 슬러리 공급 시작 (flow rate: 150~250 ml/min)
2. 플래튼 회전 시작 (60~120 rpm)
3. 캐리어 헤드 하강 및 회전 시작 (50~100 rpm)
4. Zone별 압력 인가 (1.5~6.0 psi)
5. 연마 시간 또는 EPD(End Point Detection) 신호로 종료 판단

### 2.3 린스 (Rinse)
1. 슬러리 공급 중단
2. DI water 공급 시작 (500 ml/min, 15초)
3. 저압 연마로 잔여 슬러리 제거
4. 헤드 상승

### 2.4 웨이퍼 언로딩
1. 캐리어 헤드에서 웨이퍼 분리 (blow-off + 진공 해제)
2. 로봇 암으로 클리너 모듈에 전달
3. 브러시 클리닝 + 메가소닉 세정
4. 스핀 드라이

## 3. Zone 압력 설정

### 3.1 Zone 맵
CMP 헤드는 다중 Zone으로 구성되어 각 Zone별 독립 압력 제어가 가능하다.

| Zone | 위치 | 기능 | 일반 설정 범위 |
|------|------|------|---------------|
| Zone 1 | Center | 웨이퍼 중심부 연마율 제어 | 2.0~4.0 psi |
| Zone 2 | Inner | 내부 링 영역 | 2.0~4.0 psi |
| Zone 3 | Middle | 중간 링 영역 | 2.5~4.5 psi |
| Zone 4 | Outer | 외부 링 영역 | 2.5~5.0 psi |
| Zone 5 | Edge | 에지 영역 | 3.0~6.0 psi |
| Retaining Ring | Ring | 리테이닝 링 압력 | 4.0~8.0 psi |

### 3.2 압력 설정 원칙
- **균일도(WIWNU) 목표**: < 3% (1-sigma)
- Center-to-Edge 프로파일은 Zone 1과 Zone 5의 압력 비율로 조정
- 에지 과연마 방지: Retaining Ring 압력을 Zone 5 대비 1.2~1.5배로 설정
- 새 패드 사용 시 초기 10매는 Zone 압력을 5% 낮게 설정 (break-in)

## 4. 슬러리 설정

### 4.1 슬러리 종류별 설정

| 슬러리 타입 | 대상 물질 | Flow Rate | pH | 입자 크기 |
|------------|----------|-----------|-----|----------|
| Oxide 슬러리 | SiO2 (TEOS, HDP) | 200 ml/min | 10~11 | 50~200 nm |
| Metal 슬러리 | Cu | 150 ml/min | 3~4 | 30~100 nm |
| W 슬러리 | Tungsten | 180 ml/min | 2~3 | 50~150 nm |
| STI 슬러리 | SiO2 (선택비) | 200 ml/min | 10~11 | 100~250 nm |

### 4.2 슬러리 관리
- 공급 온도: 20~25°C 유지
- 공급 라인 압력: 15~25 psi
- 슬러리 유효 기간: 개봉 후 최대 72시간
- 사용 전 최소 30분 교반 필요
- 결정화 방지를 위해 미사용 시 라인 DI water 플러시

## 5. EPD (End Point Detection)

### 5.1 EPD 방식
- **Motor Current 방식**: 토크 변화로 층 전환 감지
- **Optical 방식**: 반사율 변화로 막 두께 실시간 측정
- **시간 기반**: 고정 연마 시간 (EPD 백업용)

### 5.2 EPD 판정 기준
- Motor current 변화율 > 5%: 층 전환 감지
- Optical signal 안정화 후 over-polish 시간: 10~30초
- EPD 미감지 시 최대 연마 시간 제한: 레시피 설정 시간의 150%

## 6. 레시피 파라미터 요약

### 6.1 Oxide CMP 표준 레시피

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 93 rpm | 90~100 rpm |
| Carrier Speed | 87 rpm | 80~95 rpm |
| Zone 1~5 Pressure | 3.0 psi | 2.0~5.0 psi |
| Retaining Ring Pressure | 5.0 psi | 4.0~8.0 psi |
| Slurry Flow Rate | 200 ml/min | 150~250 ml/min |
| Polish Time | 60 sec | EPD 기반 |
| Rinse Time | 15 sec | 10~20 sec |
| Conditioner Downforce | 5 lbf | 3~7 lbf |

### 6.2 Cu CMP 표준 레시피

| 파라미터 | 값 | 허용 범위 |
|---------|-----|----------|
| Platen Speed | 80 rpm | 70~90 rpm |
| Carrier Speed | 75 rpm | 65~85 rpm |
| Zone 1~5 Pressure | 2.5 psi | 1.5~4.0 psi |
| Retaining Ring Pressure | 4.5 psi | 3.5~6.0 psi |
| Slurry Flow Rate | 150 ml/min | 120~200 ml/min |
| Polish Time | EPD + 15s | EPD 기반 |
| Rinse Time | 15 sec | 10~20 sec |
