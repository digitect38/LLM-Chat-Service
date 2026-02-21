using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-parameter-optimization.md survive chunking.
/// </summary>
public class CmpParameterOptimizationContentTests
{
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-parameter-optimization.md");

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
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-parameter-optimization.md" }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // === 1. 최적화 목표 ===

    [Fact]
    public void Chunk_Contains_MRR_Target_5Percent()
        => AnyChunkContains("± 5%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WIWNU_Target_3Percent()
        => AnyChunkContains("< 3%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WIWNU_1Sigma()
        => AnyChunkContains("1-sigma").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_49Point_Map()
        => AnyChunkContains("49점").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WTWNU_2Percent()
        => AnyChunkContains("< 2%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WTWNU_25Wafers()
        => AnyChunkContains("25매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DefectZero()
        => AnyChunkContains("Defect").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Surfscan()
        => AnyChunkContains("Surfscan").Should().BeTrue();

    // === 2. 최적화 우선순위 ===

    [Fact]
    public void Chunk_Contains_DefectZero_Priority()
        => AnyChunkContains("최우선").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WIWNU_YieldDirect()
        => AnyChunkContains("수율에 직결").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Throughput()
        => AnyChunkContains("throughput").Should().BeTrue();

    // === 3. 압력 최적화 기본 원리 ===

    [Fact]
    public void Chunk_Contains_PressureUp_MRR_Up()
        => AnyChunkContains("압력 증가").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Adjustment_0_1_0_3psi()
        => AnyChunkContains("0.1~0.3 psi").Should().BeTrue();

    // === 4. Center-to-Edge 프로파일 ===

    [Fact]
    public void Chunk_Contains_CenterFast_Zone1_Down()
        => AnyChunkContains("Zone 1 ↓").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CenterSlow_Zone1_Up()
        => AnyChunkContains("Zone 1 ↑").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EdgeFast_Zone5_Down()
        => AnyChunkContains("Zone 5 ↓").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EdgeSlow_Zone5_Up()
        => AnyChunkContains("Zone 5 ↑").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MShape()
        => AnyChunkContains("M-Shape").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WShape()
        => AnyChunkContains("W-Shape").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone3_Down_0_2psi()
        => AnyChunkContains("Zone 3 ↓ 0.2 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone3_Up_0_2psi()
        => AnyChunkContains("Zone 3 ↑ 0.2 psi").Should().BeTrue();

    // === 5. 압력 조정 규칙 ===

    [Fact]
    public void Chunk_Contains_OneVariable()
        => AnyChunkContains("하나의 변수만 변경").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_3TestWafers()
        => AnyChunkContains("최소 3매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Max20Percent_Change()
        => AnyChunkContains("20%를 초과하지").Should().BeTrue();

    // === 6. Retaining Ring 압력 최적화 ===

    [Fact]
    public void Chunk_Contains_RingRatio_1_3_1_5()
        => AnyChunkContains("1.3~1.5배").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RingStart_1_0()
        => AnyChunkContains("1.0배에서 시작").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Ring_0_5psi_Step()
        => AnyChunkContains("0.5 psi씩 증가").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Edge3mm_50A()
        => AnyChunkContains("50Å").Should().BeTrue();

    // === 7. 속도 최적화 ===

    [Fact]
    public void Chunk_Contains_PlatenCarrierRatio()
        => AnyChunkContains("1.0:0.9").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SpeedRatio_1_0_1_1()
        => AnyChunkContains("1.0:1.1").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PlatenSpeed_5rpm_Unit()
        => AnyChunkContains("5 rpm 단위").Should().BeTrue();

    // === 8. 속도별 효과 ===

    [Fact]
    public void Chunk_Contains_SpeedUp_ScratchRisk()
        => AnyChunkContains("스크래치 ↑").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SpeedDown_ScratchReduce()
        => AnyChunkContains("스크래치 ↓").Should().BeTrue();

    // === 9. 슬러리 최적화 ===

    [Fact]
    public void Chunk_Contains_LowFlow_150ml()
        => AnyChunkContains("< 150 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OptimalFlow_150_250()
        => AnyChunkContains("150~250 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_HighFlow_250ml()
        => AnyChunkContains("> 250 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_FlowSaturation()
        => AnyChunkContains("포화").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_FlowAdjust_10_20ml()
        => AnyChunkContains("10~20 ml/min").Should().BeTrue();

    // === 10. 슬러리 온도/pH ===

    [Fact]
    public void Chunk_Contains_OptimalTemp_20_25()
        => AnyChunkContains("20~25°C").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TempTolerance_2C()
        => AnyChunkContains("±2°C").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxidepH_10_11()
        => AnyChunkContains("pH 10~11").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CupH_3_4()
        => AnyChunkContains("pH 3~4").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_pHChange_0_5()
        => AnyChunkContains("pH 변화 > 0.5").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_pHMonitor_Daily()
        => AnyChunkContains("일 1회").Should().BeTrue();

    // === 11. 컨디셔닝 최적화 ===

    [Fact]
    public void Chunk_Contains_Downforce_3to7lbf()
        => AnyChunkContains("3~7 lbf").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DefaultDownforce_5lbf()
        => AnyChunkContains("5 lbf").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SweepSpeed_5to15()
        => AnyChunkContains("5~15").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_InSitu()
        => AnyChunkContains("In-situ").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ExSitu()
        => AnyChunkContains("Ex-situ").Should().BeTrue();

    // === 12. DOE ===

    [Fact]
    public void Chunk_Contains_DOE()
        => AnyChunkContains("DOE").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_L8_Orthogonal()
        => AnyChunkContains("L8").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DOE_Run1_MRR_2800()
        => AnyChunkContains("2800").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DOE_Run6_MRR_3800()
        => AnyChunkContains("3800").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DOE_Run4_WIWNU_3_2()
        => AnyChunkContains("3.2%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DOE_Conclusion_Pressure_Speed()
        => AnyChunkContains("압력과 속도가 MRR에 가장 큰 영향").Should().BeTrue();

    // === 13. SPC ===

    [Fact]
    public void Chunk_Contains_SPC()
        => AnyChunkContains("SPC").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_UCL()
        => AnyChunkContains("UCL").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_LCL()
        => AnyChunkContains("LCL").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WesternElectric()
        => AnyChunkContains("Western Electric").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_3Sigma()
        => AnyChunkContains("3-sigma").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_8Points_OneSide()
        => AnyChunkContains("연속 8점").Should().BeTrue();

    // === 14. 이상 발생 시 조치 ===

    [Fact]
    public void Chunk_Contains_Hold()
        => AnyChunkContains("격리").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Qualification_Resume()
        => AnyChunkContains("Qualification").Should().BeTrue();

    // === 15. Prompt 검증 ===

    [Fact]
    public void Prompt_Contains_OptimizationContent()
    {
        var prompt = BuildPromptWith("WIWNU 목표 < 3%, MRR 목표값 ± 5%, 49점 두께 맵");
        prompt.Should().Contain("3%");
        prompt.Should().Contain("49점");
    }

    [Fact]
    public void Prompt_Contains_SourceFileName()
    {
        var prompt = BuildPromptWith("파라미터 최적화");
        prompt.Should().Contain("cmp-parameter-optimization.md");
    }
}
