# CMP 장비 구성 요소 가이드

## 1. 장비 전체 구성

### 1.1 장비 레이아웃 (Equipment Layout)

CMP 장비는 크게 Polishing Module, Cleaning Module, Wafer Handling System으로 구성된다. 전체 풋프린트는 약 3m × 5m이다. 전면(Front): FOUP Load Port 2~3개, 로봇. 중앙(Center): Polishing Platen 3개(Multi-Step CMP용). 후면(Rear): Cleaning Station, Spin Dryer. 상부: 배기 시스템, 유틸리티 연결.

### 1.2 주요 모듈 구성 (Main Modules)

CMP 장비의 주요 모듈 구성이다. Polishing Module: Platen(정반) + Carrier Head(연마 헤드) + Conditioner. Slurry Delivery System: 슬러리 탱크 + 펌프 + 필터 + 분배 노즐. Wafer Handling System: 로봇 암 + Load Port + 정렬기. Cleaning Module: Brush Station + Megasonic + Spin Dryer. Control System: PLC + 소프트웨어 + 센서.

## 2. Polishing Module

### 2.1 Platen (정반)

Platen은 Polishing Pad가 부착되는 회전 정반이다. 직경: 약 600~800mm(300mm 웨이퍼용). 재질: 스테인리스 스틸 또는 알루미늄(표면 연마 처리). 구동: AC 서보 모터, 속도 범위 20~150 rpm. 평탄도 기준: TIR(Total Indicator Runout) < 25 μm. 냉각: 내부 냉각수 채널로 연마 열을 제거한다(냉각수 온도 18~22°C).

### 2.2 Carrier Head (연마 헤드)

Carrier Head는 웨이퍼를 유지하고 패드에 압력을 가하는 모듈이다. Multi-Zone 구조: 5~7개 독립 Zone으로 구성되어 Zone별 압력 제어가 가능하다. 구성: Retaining Ring(웨이퍼 고정), Membrane(압력 전달), Backing Film(완충). 척킹: 진공 방식(-600 mmHg). 구동: AC 서보 모터, 속도 범위 20~120 rpm.

### 2.3 Polishing Pad 구조

Polishing Pad의 구조이다. 상층(Top Pad): 폴리우레탄, 연마 수행, Groove 패턴(동심원, XY, 나선형)으로 슬러리 분배. 하층(Sub Pad): 부직포 또는 폴리우레탄 폼, 완충 역할. 접착: PSA(감압 접착제)로 Platen에 부착. 대표 사양: IC1000/SubaIV(경질/연질 복합). Groove 깊이: 0.3~0.5mm, 폭: 0.5~1.0mm.

### 2.4 Conditioner 구조

Conditioner는 Pad 표면을 갱신하여 MRR을 안정화하는 모듈이다. Conditioner Disk: 다이아몬드 입자가 전착된 원형 디스크(직경 약 100mm). Arm: Disk를 패드 위에서 Sweep 구동(내경~외경). Downforce: 공압 실린더로 3~7 lbf 인가. 동작 방식: In-situ(연마 중) 또는 Ex-situ(연마 간). Sweep 패턴이 패드 전면을 균일하게 커버해야 한다.

## 3. Slurry Delivery System

### 3.1 슬러리 탱크 (Slurry Tank)

슬러리 탱크 구성이다. 용량: 20~50 리터(장비 옆 Day Tank 기준). 교반기(Agitator): 침전 방지를 위해 연속 교반(저속 회전). 레벨 센서: 잔량 모니터링(Low/Low-Low 알람). 온도 조절: 히터/쿨러 또는 환경 온도 의존. 라벨: 슬러리 타입, 로트 번호, 개봉 일시를 기록한다.

### 3.2 공급 펌프/필터 (Supply Pump and Filter)

슬러리 공급 시스템이다. 펌프: 다이어프램 펌프 또는 벨로우즈 펌프(입자 손상 최소화). 출력 압력: 15~25 psi. 필터: 슬러리 내 대형 입자 제거, 공극 크기 0.5~1.0 μm. 필터 차압 모니터링: < 10 psi(초과 시 교체). 펌프 다이어프램 교체 주기: 분기 1회.

### 3.3 분배 노즐 (Distribution Nozzle)

슬러리 분배 노즐이다. 위치: Platen 중심부 상방에서 패드 표면에 슬러리를 공급한다. 노즐 타입: Single Point 또는 Multi-Point(균일 분배). 유량 제어: Mass Flow Controller 또는 유량 밸브. 노즐 막힘 방지: 미사용 시 DI water 플러시. 노즐 위치가 MRR 균일도에 영향을 주므로 정확한 위치 설정이 중요하다.

## 4. Wafer Handling System

### 4.1 로봇 암 (Robot Arm)

웨이퍼 이송 로봇 구성이다. 타입: 다관절 로봇(4~6축). 핸들링: 진공 흡착 방식 또는 Edge Grip 방식. 이송 경로: Load Port → Head → Platen → Cleaner → Load Port. 위치 정확도: ± 0.5mm. 반복 정밀도: ± 0.2mm. 이송 속도: 웨이퍼 파손 방지를 위해 가감속 제어.

### 4.2 FOUP Load Port

FOUP Load Port 구성이다. 포트 수: 2~3개(입력/출력 분리 가능). FOUP(Front Opening Unified Pod): 300mm 웨이퍼 25매 수납. 도어 개폐: 자동 래치 + 도어 오프너. 매핑(Mapping): FOUP 내 웨이퍼 유무 및 위치를 광학 센서로 확인. FOUP ID 리더: 바코드 또는 RFID로 로트 정보를 읽는다.

### 4.3 웨이퍼 정렬기 (Wafer Aligner)

웨이퍼 정렬기는 Notch 또는 Flat을 기준으로 웨이퍼 방향을 정렬하는 모듈이다. 정렬 정확도: ± 0.1°. Center 보정: 웨이퍼 중심 편차를 ± 0.1mm 이내로 보정. CMP에서는 정렬이 결함 패턴 분석 시 방향 참조에 사용된다.

## 5. Cleaning Module

### 5.1 Brush Station

Brush Station은 PVA(Polyvinyl Alcohol) 브러시로 웨이퍼 표면의 슬러리 입자를 물리적으로 제거하는 모듈이다. 브러시 수: 양면(Top + Bottom) 또는 단면(Top만). 회전 속도: 100~300 rpm. 접촉 압력: 1~3 psi(과도 시 스크래치). 세정액: NH4OH 희석액 또는 전용 세정액. 브러시 교체 주기: 약 500~1000 웨이퍼 처리 후.

### 5.2 Megasonic Station

Megasonic Station은 고주파 초음파(700kHz~1MHz)를 이용하여 미세 파티클을 제거하는 모듈이다. 원리: Megasonic 에너지가 DI water에서 캐비테이션을 발생시켜 표면 파티클을 분리. 파워: 20~50W(과도 시 패턴 손상). 세정 시간: 30~60초. DI water 유량: 500~1000 ml/min.

### 5.3 Spin Dryer

Spin Dryer는 웨이퍼를 고속 회전하여 표면의 수분을 원심력으로 제거하는 모듈이다. 회전 속도: 2000~4000 rpm. 건조 시간: 30~60초. 질소(N2) 블로우: 추가 건조 효과. 건조 불량 시 Water Mark(물자국)가 발생하므로 충분한 회전과 N2 공급이 필요하다.

## 6. 제어 시스템

### 6.1 PLC/Software 구성

CMP 장비 제어 시스템 구성이다. PLC(Programmable Logic Controller): 장비 하드웨어 시퀀스 제어, I/O 모듈로 센서/액추에이터 연결. 상위 소프트웨어: 레시피 관리, EPD 신호 처리, 데이터 로깅, 알람 관리. 통신: PLC ↔ 소프트웨어 간 Ethernet/IP 또는 EtherCAT. HMI(Human Machine Interface): 터치스크린으로 운전/모니터링.

### 6.2 센서 목록 총정리

CMP 장비에 사용되는 주요 센서 목록이다.

| 센서 | 측정 대상 | 용도 |
|------|----------|------|
| 압력 센서 | Zone/Ring 압력 | 압력 제어/모니터링 |
| 유량 센서 | 슬러리/DI water | 유량 제어 |
| 온도 센서 | Platen/슬러리/모터 | 온도 모니터링 |
| 토크 센서 | 모터 전류 | EPD/과부하 감지 |
| 진공 센서 | 척킹 압력 | 웨이퍼 유지 확인 |
| 웨이퍼 센서 | 웨이퍼 유무 | 이송 확인 |

### 6.3 통신 인터페이스

CMP 장비의 외부 통신 인터페이스이다. SECS/GEM(SEMI E30/E37): 호스트(MES) 통신 표준, 레시피 다운로드, 데이터 업로드, 원격 제어. EDA(Equipment Data Acquisition, SEMI E134): 고속 장비 데이터 수집. OCAP(Out of Control Action Plan): 이상 감지 시 자동 조치 연동. 통신 연결은 장비 가동의 전제 조건이며, 통신 단절 시 장비 운전이 제한된다.
