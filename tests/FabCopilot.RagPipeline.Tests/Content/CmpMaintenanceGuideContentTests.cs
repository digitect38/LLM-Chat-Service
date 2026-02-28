using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-maintenance-guide.md survive chunking.
/// </summary>
public class CmpMaintenanceGuideContentTests
{
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-maintenance-guide.md");

    private static readonly Lazy<string> RawText = new(() => File.ReadAllText(DocPath));
    private static readonly Lazy<List<string>> Chunks = new(() =>
        DocumentIngestor.ChunkText(RawText.Value, 512, 128));

    private static bool AnyChunkContains(string keyword)
        => Chunks.Value.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string BuildPromptWith(string chunkText)
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = chunkText,
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-maintenance-guide.md" }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // === 1. 유지보수 분류 ===

    [Fact]
    public void Chunk_Contains_DailyPM()
        => AnyChunkContains("Daily PM").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WeeklyPM()
        => AnyChunkContains("Weekly PM").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MonthlyPM()
        => AnyChunkContains("Monthly PM").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_QuarterlyPM()
        => AnyChunkContains("Quarterly PM").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_AnnualPM()
        => AnyChunkContains("Annual PM").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DailyPM_30Min()
        => AnyChunkContains("30분").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WeeklyPM_2Hours()
        => AnyChunkContains("2시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MonthlyPM_4to6Hours()
        => AnyChunkContains("4~6시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_QuarterlyPM_8to12Hours()
        => AnyChunkContains("8~12시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_AnnualPM_2to3Days()
        => AnyChunkContains("2~3일").Should().BeTrue();

    // === 2. Daily PM 점검 항목 ===

    [Fact]
    public void Chunk_Contains_SlurryTankLevel_30Percent()
        => AnyChunkContains("30%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DIWaterPressure_40_60psi()
        => AnyChunkContains("40~60 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_VacuumPressure_600mmHg()
        => AnyChunkContains("-600 mmHg").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DailyQual_MRR_10Percent()
        => AnyChunkContains("± 10%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DailyQual_WIWNU_5Percent()
        => AnyChunkContains("< 5%").Should().BeTrue();

    // === 3. Weekly PM 점검 항목 ===

    [Fact]
    public void Chunk_Contains_RetainingRing_2_0mm()
        => AnyChunkContains("2.0 mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryFilter_10psi()
        => AnyChunkContains("10 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureHoldTest()
        => AnyChunkContains("Pressure Hold Test").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureHold_3_0psi()
        => AnyChunkContains("3.0 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureHold_30sec()
        => AnyChunkContains("30초").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureHold_0_3psiDrop()
        => AnyChunkContains("0.3 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RobotTeaching_0_5mm()
        => AnyChunkContains("±0.5 mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PlatenTempSensor_1C()
        => AnyChunkContains("±1°C").Should().BeTrue();

    // === 4. Monthly PM ===

    [Fact]
    public void Chunk_Contains_PadReplaceSOP()
        => AnyChunkContains("SOP 참조").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureRegulator_0_1psi()
        => AnyChunkContains("±0.1 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EPDSensorCalibration()
        => AnyChunkContains("EPD 센서 교정").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadLifetime_500Hours()
        => AnyChunkContains("500시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadThickness_1_0mm()
        => AnyChunkContains("1.0mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MRR_Degradation_15Percent()
        => AnyChunkContains("15%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Glazing()
        => AnyChunkContains("glazing").Should().BeTrue();

    // === 5. Quarterly PM ===

    [Fact]
    public void Chunk_Contains_CarrierHeadOverhaul()
        => AnyChunkContains("오버홀").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PlatenBearing()
        => AnyChunkContains("베어링").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryPumpDiaphragm()
        => AnyChunkContains("다이어프램").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PlatenFlatness_25um()
        => AnyChunkContains("25 μm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TIR()
        => AnyChunkContains("TIR").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Overhaul_Torque_15Nm()
        => AnyChunkContains("15 N·m").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Overhaul_IPA()
        => AnyChunkContains("IPA").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BackingFilm()
        => AnyChunkContains("backing film").Should().BeTrue();

    // === 6. Annual PM ===

    [Fact]
    public void Chunk_Contains_PlatenMotorBearing()
        => AnyChunkContains("플래튼 모터").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RobotOverhaul()
        => AnyChunkContains("로봇 전체 오버홀").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SoftwareUpdate()
        => AnyChunkContains("소프트웨어 업데이트").Should().BeTrue();

    // === 7. 소모품 수명 관리 ===

    [Fact]
    public void Chunk_Contains_PadStock_3()
        => AnyChunkContains("3매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RetainingRingThreshold_1_5mm()
        => AnyChunkContains("1.5mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ConditionerDisk_6Months()
        => AnyChunkContains("6개월").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryFilter_Weekly()
        => AnyChunkContains("주 1회").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_FilterStock_10()
        => AnyChunkContains("10개").Should().BeTrue();

    // === 8. 기록 관리 ===

    [Fact]
    public void Chunk_Contains_RecordRetention_3Years()
        => AnyChunkContains("3년").Should().BeTrue();

    // === 9. Prompt 검증 ===

    [Fact]
    public void Prompt_Contains_PMSchedule()
    {
        var prompt = BuildPromptWith("Daily PM 매일 30분, Weekly PM 2시간, Monthly PM 4~6시간");
        prompt.Should().Contain("Daily PM");
        prompt.Should().Contain("30분");
    }

    [Fact]
    public void Prompt_Contains_ChunkText_NotFileName()
    {
        var prompt = BuildPromptWith("유지보수 내용");
        prompt.Should().NotContain("cmp-maintenance-guide.md");
        prompt.Should().Contain("유지보수 내용");
    }

    [Fact]
    public void Prompt_Contains_PressureHoldContent()
    {
        var prompt = BuildPromptWith("Pressure Hold Test: 30초간 압력 강하 < 0.3 psi");
        prompt.Should().Contain("0.3 psi");
        prompt.Should().Contain("30초");
    }
}
