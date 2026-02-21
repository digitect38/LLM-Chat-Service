using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-general-troubleshooting.md survive chunking.
/// </summary>
public class CmpTroubleshootingContentTests
{
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-general-troubleshooting.md");

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
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-general-troubleshooting.md" }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // === 1. MRR 저하 ===

    [Fact]
    public void Chunk_Contains_MRR_Decrease_15Percent()
        => AnyChunkContains("15% 이상 감소").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadGlazing()
        => AnyChunkContains("Pad glazing").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Conditioning_Downforce_Plus1lbf()
        => AnyChunkContains("downforce +1 lbf").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryConcentrationCheck()
        => AnyChunkContains("슬러리 농도 저하").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_FlowMeterCheck()
        => AnyChunkContains("Flow meter").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TemperatureIssue()
        => AnyChunkContains("온도 이상").Should().BeTrue();

    // === 2. MRR 과다 ===

    [Fact]
    public void Chunk_Contains_MRR_Increase_15Percent()
        => AnyChunkContains("15% 이상 증가").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OverPolish()
        => AnyChunkContains("over-polish").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BreakInWafer_10()
        => AnyChunkContains("Break-in 웨이퍼 10매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Pressure_5_10Percent_Down()
        => AnyChunkContains("5~10% 하향").Should().BeTrue();

    // === 3. Center-Fast ===

    [Fact]
    public void Chunk_Contains_CenterFast()
        => AnyChunkContains("Center-Fast").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone1_Pressure_Reduce_0_3_0_5()
        => AnyChunkContains("0.3~0.5 psi 감소").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Ring_Pressure_Increase_0_5()
        => AnyChunkContains("0.5 psi 증가").Should().BeTrue();

    // === 4. Center-Slow ===

    [Fact]
    public void Chunk_Contains_CenterSlow()
        => AnyChunkContains("Center-Slow").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone1_Pressure_Increase()
        => AnyChunkContains("Zone 1 압력 0.3~0.5 psi 증가").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RetainingRing_1_5mm_Replace()
        => AnyChunkContains("1.5mm 미만 시 교체").Should().BeTrue();

    // === 5. Edge-Fast ===

    [Fact]
    public void Chunk_Contains_EdgeFast()
        => AnyChunkContains("Edge-Fast").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EdgeErosion()
        => AnyChunkContains("Edge Erosion").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Edge3to5mm()
        => AnyChunkContains("3~5mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Ring_1_0psi_Increase()
        => AnyChunkContains("1.0 psi 증가").Should().BeTrue();

    // === 6. Micro-Scratch ===

    [Fact]
    public void Chunk_Contains_MicroScratch()
        => AnyChunkContains("Micro-Scratch").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ParticleCount()
        => AnyChunkContains("particle count").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ConditionerDiskIssue()
        => AnyChunkContains("디스크 교체").Should().BeTrue();

    // === 7. Macro-Scratch ===

    [Fact]
    public void Chunk_Contains_MacroScratch()
        => AnyChunkContains("Macro-Scratch").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_GrooveDebris()
        => AnyChunkContains("groove에 이물질").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RetainingRingDamage()
        => AnyChunkContains("Ring 파손").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryDryCrystallization()
        => AnyChunkContains("결정화").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WaferChipping()
        => AnyChunkContains("칩핑").Should().BeTrue();

    // === 8. Dishing & Erosion ===

    [Fact]
    public void Chunk_Contains_CuDishing()
        => AnyChunkContains("Cu Dishing").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WideLine()
        => AnyChunkContains("Wide line").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OverPolish_5sec_Unit()
        => AnyChunkContains("5초 단위").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxideErosion()
        => AnyChunkContains("Oxide Erosion").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DensePattern()
        => AnyChunkContains("Dense pattern").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_HighSelectivitySlurry()
        => AnyChunkContains("High-selectivity").Should().BeTrue();

    // === 9. 알람 코드 ===

    [Fact]
    public void Chunk_Contains_A100_EmergencyStop()
        => AnyChunkContains("A100").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A101_Vacuum()
        => AnyChunkContains("A101").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A110_PlatenOverload()
        => AnyChunkContains("A110").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A120_SlurryFlow()
        => AnyChunkContains("A120").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A123_HeadPressure()
        => AnyChunkContains("A123").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A140_EPD()
        => AnyChunkContains("A140").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A150_Robot()
        => AnyChunkContains("A150").Should().BeTrue();

    // === 10. 알람 심각도 ===

    [Fact]
    public void Chunk_Contains_CriticalAlarm()
        => AnyChunkContains("Critical").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_HighAlarm()
        => AnyChunkContains("High").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MediumAlarm()
        => AnyChunkContains("Medium").Should().BeTrue();

    // === 11. 웨이퍼 파손 ===

    [Fact]
    public void Chunk_Contains_EmergencyStop()
        => AnyChunkContains("Emergency Stop").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_VacuumPicker()
        => AnyChunkContains("진공 피커").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DummyWafer_3()
        => AnyChunkContains("더미 웨이퍼 3매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MaxPressure_6_0psi()
        => AnyChunkContains("6.0 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_AccidentReport()
        => AnyChunkContains("사고 보고서").Should().BeTrue();

    // === 12. Prompt 검증 ===

    [Fact]
    public void Prompt_Contains_AlarmContent()
    {
        var prompt = BuildPromptWith("알람 A123: Head Pressure Out of Range, 심각도 High");
        prompt.Should().Contain("A123");
        prompt.Should().Contain("High");
    }

    [Fact]
    public void Prompt_Contains_SourceFileName()
    {
        var prompt = BuildPromptWith("CMP 트러블슈팅 내용");
        prompt.Should().Contain("cmp-general-troubleshooting.md");
    }
}
