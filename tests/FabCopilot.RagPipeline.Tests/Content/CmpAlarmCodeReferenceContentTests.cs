using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-alarm-code-reference.md survive markdown chunking
/// with correct document → chapter → section → line mapping.
/// Uses ChunkMarkdown (matching production ingestion) instead of ChunkText.
/// </summary>
public class CmpAlarmCodeReferenceContentTests
{
    private const string FileName = "cmp-alarm-code-reference.md";

    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", FileName);

    private static readonly Lazy<string> RawText = new(() => File.ReadAllText(DocPath));
    private static readonly Lazy<List<string>> MdChunks = new(() =>
        DocumentIngestor.ChunkMarkdown(RawText.Value, 512, 128));

    /// <summary>Find a chunk containing both keywords.</summary>
    private static string? FindChunk(string kw1, string kw2)
        => MdChunks.Value.FirstOrDefault(c =>
            c.Contains(kw1, StringComparison.OrdinalIgnoreCase) &&
            c.Contains(kw2, StringComparison.OrdinalIgnoreCase));

    /// <summary>Find a chunk containing a single keyword.</summary>
    private static string? FindChunk(string kw)
        => MdChunks.Value.FirstOrDefault(c =>
            c.Contains(kw, StringComparison.OrdinalIgnoreCase));

    /// <summary>Assert chunk is in expected chapter and section via header prefix.</summary>
    private static void AssertContext(string chunk, string expectedChapter, string? expectedSection = null)
    {
        var ctx = DocumentIngestor.ExtractParentContext(chunk);
        ctx.Should().NotBeNull($"chunk should have section header prefix");
        ctx!.Should().Contain(expectedChapter, $"chunk should be in chapter '{expectedChapter}'");
        if (expectedSection is not null)
            ctx.Should().Contain(expectedSection, $"chunk should be in section '{expectedSection}'");
    }

    private static string BuildPromptWith(string chunkText)
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = chunkText,
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = FileName }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // ═══════════════════════════════════════════════════════════════
    // §1. 알람 분류 체계 — Chapter: "1. 알람 분류 체계"
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 1 | Sec: 1.1 | Line: "Critical | 적색 | 즉시 정지"
    public void S1_1_SeverityLevel_Critical_InCorrectSection()
    {
        var chunk = FindChunk("Critical", "즉시 정지");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Critical, 즉시 정지'");
        AssertContext(chunk!, "알람 분류 체계", "심각도 분류");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 1 | Sec: 1.1 | Line: "High | 황색 | 현 웨이퍼 후 정지 | 30분 이내"
    public void S1_1_SeverityLevel_High_30Min()
    {
        var chunk = FindChunk("High", "30분 이내");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'High, 30분 이내'");
        AssertContext(chunk!, "알람 분류 체계", "심각도 분류");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 1 | Sec: 1.2 | Line: "A100~A109는 Critical 알람"
    public void S1_2_NumberingSystem_A100_Critical()
    {
        var chunk = FindChunk("A100~A109", "Critical");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain 'A100~A109 Critical'");
        AssertContext(chunk!, "알람 분류 체계");
    }

    // ═══════════════════════════════════════════════════════════════
    // §2. Critical 알람 (A100~A109) — Chapter: "2. Critical 알람"
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 2 | Sec: A100 | Line: "비상 정지 버튼이 눌렸거나 안전 인터록이 작동"
    public void S2_A100_EmergencyStop_Interlock()
    {
        var chunk = FindChunk("Emergency Stop", "인터록");
        chunk.Should().NotBeNull($"{FileName} > §A100 should contain 'Emergency Stop, 인터록'");
        AssertContext(chunk!, "Critical 알람", "A100");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 2 | Sec: A101 | Line: "진공 척킹 압력이 -400 mmHg 미만"
    public void S2_A101_VacuumFailure_400mmHg()
    {
        var chunk = FindChunk("Vacuum Failure", "-400 mmHg");
        chunk.Should().NotBeNull($"{FileName} > §A101 should contain 'Vacuum Failure, -400 mmHg'");
        AssertContext(chunk!, "Critical 알람", "A101");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 2 | Sec: A101 | Line: "기준: 진공 압력 -600 mmHg 이하 정상"
    public void S2_A101_NormalVacuum_600mmHg()
    {
        var chunk = FindChunk("-600 mmHg", "정상");
        chunk.Should().NotBeNull($"{FileName} > §A101 should contain '-600 mmHg 정상'");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 2 | Sec: A102 | Line: "리테이닝 링 밖으로 이탈...두께 < 1.5mm"
    public void S2_A102_WaferOutOfPosition_RingThickness()
    {
        var chunk = FindChunk("Wafer Out of Position", "1.5mm");
        chunk.Should().NotBeNull($"{FileName} > §A102 should contain 'Wafer Out of Position, 1.5mm'");
        AssertContext(chunk!, "Critical 알람", "A102");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 2 | Sec: A103 | Line: "안전 도어 개방, 커버 미장착"
    public void S2_A103_InterlockViolation()
    {
        var chunk = FindChunk("Interlock Violation", "도어");
        chunk.Should().NotBeNull($"{FileName} > §A103 should contain 'Interlock Violation, 도어'");
        AssertContext(chunk!, "Critical 알람", "A103");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 2 | Sec: A104 | Line: "슬러리 또는 세정액 누출...MSDS"
    public void S2_A104_ChemicalLeak_MSDS()
    {
        var chunk = FindChunk("Chemical Leak", "MSDS");
        chunk.Should().NotBeNull($"{FileName} > §A104 should contain 'Chemical Leak, MSDS'");
        AssertContext(chunk!, "Critical 알람", "A104");
    }

    // ═══════════════════════════════════════════════════════════════
    // §3. Platen/Motor 알람 (A110~A119)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 3 | Sec: A110 | Line: "토크가 정상 범위의 150%를 초과"
    public void S3_A110_PlatenOverload_150Percent()
    {
        var chunk = FindChunk("Platen Overload", "150%");
        chunk.Should().NotBeNull($"{FileName} > §A110 should contain 'Platen Overload, 150%'");
        AssertContext(chunk!, "Platen/Motor", "A110");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 3 | Sec: A111 | Line: "설정값 대비 ±5 rpm 이상 벗어난"
    public void S3_A111_PlatenSpeedDeviation_5rpm()
    {
        var chunk = FindChunk("Platen Speed Deviation", "±5 rpm");
        chunk.Should().NotBeNull($"{FileName} > §A111 should contain 'Platen Speed Deviation, ±5 rpm'");
        AssertContext(chunk!, "Platen/Motor", "A111");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 3 | Sec: A112 | Line: "캐리어 헤드 회전 속도...± 3 rpm 이내 정상"
    public void S3_A112_CarrierSpeedDeviation_3rpm()
    {
        var chunk = FindChunk("Carrier Speed Deviation", "± 3 rpm");
        chunk.Should().NotBeNull($"{FileName} > §A112 should contain 'Carrier Speed Deviation, ± 3 rpm'");
        AssertContext(chunk!, "Platen/Motor", "A112");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 3 | Sec: A113 | Line: "정격의 120%를 초과"
    public void S3_A113_MotorOvercurrent_120Percent()
    {
        var chunk = FindChunk("Motor Overcurrent", "120%");
        chunk.Should().NotBeNull($"{FileName} > §A113 should contain 'Motor Overcurrent, 120%'");
        AssertContext(chunk!, "Platen/Motor", "A113");
    }

    // ═══════════════════════════════════════════════════════════════
    // §4. Head/Pressure 알람 (A120~A129)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 4 | Sec: A120 | Line: "설정값 대비 ±20% 이상...필터 차압(< 10 psi)"
    public void S4_A120_SlurryFlowAbnormal_20Percent_10psi()
    {
        var chunk = FindChunk("Slurry Flow Abnormal", "±20%");
        chunk.Should().NotBeNull($"{FileName} > §A120 should contain 'Slurry Flow Abnormal, ±20%'");
        AssertContext(chunk!, "Head/Pressure", "A120");
        chunk!.Should().Contain("10 psi", "filter differential pressure threshold should be preserved");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 4 | Sec: A121 | Line: "정상 범위(15~25 psi)"
    public void S4_A121_SlurryPressure_15_25psi()
    {
        var chunk = FindChunk("Slurry Pressure Abnormal", "15~25 psi");
        chunk.Should().NotBeNull($"{FileName} > §A121 should contain 'Slurry Pressure, 15~25 psi'");
        AssertContext(chunk!, "Head/Pressure", "A121");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 4 | Sec: A122 | Line: "공급 압력 확인(기준: 40~60 psi)"
    public void S4_A122_DIWaterFlow_40_60psi()
    {
        var chunk = FindChunk("DI Water Flow Abnormal", "40~60 psi");
        chunk.Should().NotBeNull($"{FileName} > §A122 should contain 'DI Water Flow, 40~60 psi'");
        AssertContext(chunk!, "Head/Pressure", "A122");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 4 | Sec: A123 | Line: "설정값 대비 ±15% 이상...리테이닝 링 < 1.5mm"
    public void S4_A123_HeadPressure_15Percent_Ring1_5mm()
    {
        var chunk = FindChunk("Head Pressure Out of Range", "±15%");
        chunk.Should().NotBeNull($"{FileName} > §A123 should contain 'Head Pressure, ±15%'");
        AssertContext(chunk!, "Head/Pressure", "A123");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 4 | Sec: A125 | Line: "링 두께 4점 측정(기준 > 2.0mm)"
    public void S4_A125_RetainingRingPressure_2_0mm()
    {
        var chunk = FindChunk("Retaining Ring Pressure", "2.0mm");
        chunk.Should().NotBeNull($"{FileName} > §A125 should contain 'Retaining Ring Pressure, 2.0mm'");
        AssertContext(chunk!, "Head/Pressure", "A125");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 4 | Sec: A127 | Line: "30초간 압력 강하가 0.3 psi를 초과"
    public void S4_A127_MembraneLeak_30sec_0_3psi()
    {
        var chunk = FindChunk("Membrane Leak", "0.3 psi");
        chunk.Should().NotBeNull($"{FileName} > §A127 should contain 'Membrane Leak, 0.3 psi'");
        AssertContext(chunk!, "Head/Pressure", "A127");
        chunk!.Should().Contain("30초", "30-second test duration should be preserved");
    }

    // ═══════════════════════════════════════════════════════════════
    // §5. 온도 알람 (A130~A139)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 5 | Sec: A130 | Line: "정상 범위(20~35°C)...냉각수 온도(18~22°C)"
    public void S5_A130_PlatenTemp_20_35C_Coolant_18_22C()
    {
        var chunk = FindChunk("Platen Temperature Abnormal", "20~35°C");
        chunk.Should().NotBeNull($"{FileName} > §A130 should contain 'Platen Temp, 20~35°C'");
        AssertContext(chunk!, "온도 알람", "A130");
        chunk!.Should().Contain("18~22°C", "coolant temperature range should be preserved");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 5 | Sec: A131 | Line: "정상 범위(20~25°C)"
    public void S5_A131_SlurryTemp_20_25C()
    {
        var chunk = FindChunk("Slurry Temperature Abnormal", "20~25°C");
        chunk.Should().NotBeNull($"{FileName} > §A131 should contain 'Slurry Temp, 20~25°C'");
        AssertContext(chunk!, "온도 알람", "A131");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 5 | Sec: A132 | Line: "80°C를 초과...모터 권선 소손 위험"
    public void S5_A132_MotorTemp_80C()
    {
        var chunk = FindChunk("Motor Temperature Abnormal", "80°C");
        chunk.Should().NotBeNull($"{FileName} > §A132 should contain 'Motor Temp, 80°C'");
        AssertContext(chunk!, "온도 알람", "A132");
    }

    // ═══════════════════════════════════════════════════════════════
    // §6. EPD 알람 (A140~A149)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 6 | Sec: A140 | Line: "EPD 신호가 감지되지 않은 상태"
    public void S6_A140_EPDNotDetected_TimeBasedBackup()
    {
        var chunk = FindChunk("EPD Not Detected", "시간 기반");
        chunk.Should().NotBeNull($"{FileName} > §A140 should contain 'EPD Not Detected, 시간 기반'");
        AssertContext(chunk!, "EPD 알람", "A140");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 6 | Sec: A141 | Line: "센서 자체 고장 또는 케이블 불량"
    public void S6_A141_EPDSensorFailure_Cable()
    {
        var chunk = FindChunk("EPD Sensor Failure", "케이블");
        chunk.Should().NotBeNull($"{FileName} > §A141 should contain 'EPD Sensor Failure, 케이블'");
        AssertContext(chunk!, "EPD 알람", "A141");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 6 | Sec: A142 | Line: "EPD 신호 파형이 기대 패턴과 크게 다른"
    public void S6_A142_EPDAbnormalPattern()
    {
        var chunk = FindChunk("EPD Abnormal Pattern", "파형");
        chunk.Should().NotBeNull($"{FileName} > §A142 should contain 'EPD Abnormal Pattern, 파형'");
        AssertContext(chunk!, "EPD 알람", "A142");
    }

    // ═══════════════════════════════════════════════════════════════
    // §7. 로봇/이송 알람 (A150~A159)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 7 | Sec: A150 | Line: "정상 경로를 벗어나거나 타임아웃"
    public void S7_A150_RobotMotionError_Timeout()
    {
        var chunk = FindChunk("Robot Motion Error", "타임아웃");
        chunk.Should().NotBeNull($"{FileName} > §A150 should contain 'Robot Motion Error, 타임아웃'");
        AssertContext(chunk!, "로봇/이송", "A150");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 7 | Sec: A152 | Line: "FOUP 도어의 개폐 동작이 비정상"
    public void S7_A152_FOUPDoorError()
    {
        var chunk = FindChunk("FOUP Door Error", "래치");
        chunk.Should().NotBeNull($"{FileName} > §A152 should contain 'FOUP Door Error, 래치'");
        AssertContext(chunk!, "로봇/이송", "A152");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 7 | Sec: A153 | Line: "충돌 센서가 작동...파손 여부 점검"
    public void S7_A153_CollisionDetected()
    {
        var chunk = FindChunk("Collision Detected", "충돌 센서");
        chunk.Should().NotBeNull($"{FileName} > §A153 should contain 'Collision Detected, 충돌 센서'");
        AssertContext(chunk!, "로봇/이송", "A153");
    }

    // ═══════════════════════════════════════════════════════════════
    // §8. 컨디셔너 알람 (A200~A209)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 8 | Sec: A200 | Line: "컨디셔너 동작 중 비정상...MRR 저하 위험"
    public void S8_A200_ConditionerError_MRR()
    {
        var chunk = FindChunk("Conditioner Error", "MRR");
        chunk.Should().NotBeNull($"{FileName} > §A200 should contain 'Conditioner Error, MRR'");
        AssertContext(chunk!, "컨디셔너 알람", "A200");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 8 | Sec: A201 | Line: "Downforce가 설정값 대비 ±20%"
    public void S8_A201_ConditionerPressure_20Percent()
    {
        var chunk = FindChunk("Conditioner Pressure Abnormal", "±20%");
        chunk.Should().NotBeNull($"{FileName} > §A201 should contain 'Conditioner Pressure, ±20%'");
        AssertContext(chunk!, "컨디셔너 알람", "A201");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 8 | Sec: A202 | Line: "Sweep 위치가 설정 범위를 벗어난"
    public void S8_A202_ArmPositionError_Sweep()
    {
        var chunk = FindChunk("Arm Position Error", "Sweep");
        chunk.Should().NotBeNull($"{FileName} > §A202 should contain 'Arm Position Error, Sweep'");
        AssertContext(chunk!, "컨디셔너 알람", "A202");
    }

    // ═══════════════════════════════════════════════════════════════
    // §9. 유틸리티 알람 (A210~A229)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 9 | Sec: A210 | Line: "정상 범위(40~60 psi)"
    public void S9_A210_DIWaterSupply_40_60psi()
    {
        var chunk = FindChunk("DI Water Supply Abnormal", "40~60 psi");
        chunk.Should().NotBeNull($"{FileName} > §A210 should contain 'DI Water Supply, 40~60 psi'");
        AssertContext(chunk!, "유틸리티 알람", "A210");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 9 | Sec: A211 | Line: "정상 범위(60~80 psi)"
    public void S9_A211_CDAPressure_60_80psi()
    {
        var chunk = FindChunk("CDA Pressure Abnormal", "60~80 psi");
        chunk.Should().NotBeNull($"{FileName} > §A211 should contain 'CDA Pressure, 60~80 psi'");
        AssertContext(chunk!, "유틸리티 알람", "A211");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 9 | Sec: A212 | Line: "기준(0.3~0.5 m/s face velocity)"
    public void S9_A212_ExhaustFlow_0_3_0_5ms()
    {
        var chunk = FindChunk("Exhaust Flow Abnormal", "0.3~0.5 m/s");
        chunk.Should().NotBeNull($"{FileName} > §A212 should contain 'Exhaust Flow, 0.3~0.5 m/s'");
        AssertContext(chunk!, "유틸리티 알람", "A212");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 9 | Sec: A213 | Line: "냉각수(PCW) 유량이 설정값 대비 ±20%"
    public void S9_A213_CoolingWaterFlow_20Percent()
    {
        var chunk = FindChunk("Cooling Water Flow Abnormal", "±20%");
        chunk.Should().NotBeNull($"{FileName} > §A213 should contain 'Cooling Water Flow, ±20%'");
        AssertContext(chunk!, "유틸리티 알람", "A213");
    }

    // ═══════════════════════════════════════════════════════════════
    // §10. 소모품 수명 알람 (A300~A309)
    // ═══════════════════════════════════════════════════════════════

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 10 | Sec: A300 | Line: "설정 한계(기본 500시간)의 90%"
    public void S10_A300_PadLife_500Hours_90Percent()
    {
        var chunk = FindChunk("Pad Life Warning", "500시간");
        chunk.Should().NotBeNull($"{FileName} > §A300 should contain 'Pad Life, 500시간'");
        AssertContext(chunk!, "소모품 수명", "A300");
        chunk!.Should().Contain("90%", "90% threshold should be preserved");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 10 | Sec: A301 | Line: "설정 한계(기본 200시간)의 90%"
    public void S10_A301_ConditionerDiskLife_200Hours()
    {
        var chunk = FindChunk("Conditioner Disk Life", "200시간");
        chunk.Should().NotBeNull($"{FileName} > §A301 should contain 'Conditioner Disk Life, 200시간'");
        AssertContext(chunk!, "소모품 수명", "A301");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 10 | Sec: A302 | Line: "두께 4점 측정(기준 > 2.0mm)...1.5mm 미만 시 즉시 교체"
    public void S10_A302_RetainingRingLife_2_0mm_1_5mm()
    {
        var chunk = FindChunk("Retaining Ring Life", "2.0mm");
        chunk.Should().NotBeNull($"{FileName} > §A302 should contain 'Retaining Ring Life, 2.0mm'");
        AssertContext(chunk!, "소모품 수명", "A302");
        chunk!.Should().Contain("1.5mm", "immediate replacement threshold 1.5mm should be preserved");
    }

    [Fact] // Doc: cmp-alarm-code-reference.md | Ch: 10 | Sec: A303 | Line: "설정 한계(기본 72시간)...pH 및 입자 크기 측정"
    public void S10_A303_SlurryExpiry_72Hours()
    {
        var chunk = FindChunk("Slurry Expiry Warning", "72시간");
        chunk.Should().NotBeNull($"{FileName} > §A303 should contain 'Slurry Expiry, 72시간'");
        AssertContext(chunk!, "소모품 수명", "A303");
    }

    // ═══════════════════════════════════════════════════════════════
    // Prompt integration tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Prompt_Contains_AlarmCodeAndFileName()
    {
        var prompt = BuildPromptWith("A100 Emergency Stop 비상 정지, 즉시 장비 정지 및 안전 확보");
        prompt.Should().Contain("Emergency Stop");
        prompt.Should().Contain(FileName);
    }

    [Fact]
    public void Prompt_Contains_PressureAlarmDetail()
    {
        var prompt = BuildPromptWith("A127 Membrane Leak: 30초간 압력 강하 0.3 psi 초과");
        prompt.Should().Contain("0.3 psi");
        prompt.Should().Contain("30초");
    }
}
