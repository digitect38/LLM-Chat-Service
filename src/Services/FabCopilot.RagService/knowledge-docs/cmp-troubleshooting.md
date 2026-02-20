# CMP 장비 트러블슈팅 가이드

## 1. Head Zone 압력 진동 (Pressure Oscillation)

### 증상
- Zone 7 또는 Zone 8에서 압력이 0.5~2Hz 주기로 진동
- Within-wafer non-uniformity (WIWNU) 증가
- 연마율(MRR) 불안정

### 원인
1. **Pad Glazing**: Polishing pad 표면이 경화되어 마찰 계수 변화
2. **Retaining Ring 마모**: Ring 마모로 wafer edge 압력 불균형
3. **Membrane Leak**: Head membrane 미세 누수로 압력 제어 불안정

### 조치 방법
1. Pad conditioning 프로그램 실행 (60초, 5 lbf downforce)
2. Test wafer로 연마율 확인
3. 개선 안 되면 pad 교체 (SOP-CMP-PAD-001 참조)
4. Retaining ring 두께 측정 (기준: > 2.0mm)
5. Membrane 누수 점검 (pressure hold test)

## 2. 알람 A123 - Head Pressure Out of Range

### 증상
- 알람 코드: A123
- Head pressure가 설정값 대비 ±15% 초과

### 원인
1. Retaining ring 과도한 마모 (두께 < 1.5mm)
2. Membrane 파손 또는 누수
3. Pressure regulator 이상
4. Air supply line 막힘

### 조치 방법
1. 즉시 장비 정지 (wafer 손상 방지)
2. Retaining ring 두께 확인 → 1.5mm 미만이면 교체
3. Membrane 육안 검사 → 찢어짐/변색 확인
4. Pressure hold test 실행 (30초간 압력 유지 확인)
5. 교체 후 qualification wafer 연마 → MRR, WIWNU 확인

## 3. Slurry Flow Rate 이상

### 증상
- Slurry flow rate가 설정값 대비 20% 이상 감소
- 연마율(MRR) 급격한 저하
- Torque 값 상승

### 원인
1. Slurry supply line 막힘 (결정화)
2. Pump 이상
3. Filter 막힘
4. Slurry tank 부족

### 조치 방법
1. Slurry tank 잔량 확인
2. Supply line flush (DI water 5분)
3. Filter 교체 (PM 주기 확인)
4. Pump pressure 확인 (정상 범위: 15~25 psi)
