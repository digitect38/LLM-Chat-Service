using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-slurry-pad-replacement.md survive chunking.
/// </summary>
public class CmpSlurryPadReplacementContentTests
{
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-slurry-pad-replacement.md");

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
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-slurry-pad-replacement.md" }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // === 1. 패드 교체 판단 기준 ===

    [Fact]
    public void Chunk_Contains_SOP_CMP_PAD_001()
        => AnyChunkContains("SOP-CMP-PAD-001").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadLifetime_500Hours()
        => AnyChunkContains("500시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadThickness_1_0mm()
        => AnyChunkContains("1.0 mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MRR_15Percent()
        => AnyChunkContains("15%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WIWNU_5Percent()
        => AnyChunkContains("5%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_IC1000()
        => AnyChunkContains("IC1000").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SubaIV()
        => AnyChunkContains("SubaIV").Should().BeTrue();

    // === 2. 패드 교체 도구 ===

    [Fact]
    public void Chunk_Contains_PSA()
        => AnyChunkContains("PSA").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_IPA()
        => AnyChunkContains("IPA").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CleanWiper()
        => AnyChunkContains("클린 와이퍼").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RubberRoller()
        => AnyChunkContains("고무 롤러").Should().BeTrue();

    // === 3. 패드 교체 절차 Step 1 ===

    [Fact]
    public void Chunk_Contains_PMMode()
        => AnyChunkContains("PM 모드").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DIWater_5min()
        => AnyChunkContains("5분").Should().BeTrue();

    // === 4. 패드 교체 절차 Step 2 ===

    [Fact]
    public void Chunk_Contains_PSA_Residue()
        => AnyChunkContains("PSA 잔여물").Should().BeTrue();

    // === 5. 패드 교체 절차 Step 3 ===

    [Fact]
    public void Chunk_Contains_CenterToEdge_Roller()
        => AnyChunkContains("중심에서 바깥").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_NoBubble()
        => AnyChunkContains("기포").Should().BeTrue();

    // === 6. 패드 초기화 Step 4 ===

    [Fact]
    public void Chunk_Contains_BreakIn()
        => AnyChunkContains("break-in").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BreakIn_Downforce_7lbf()
        => AnyChunkContains("7 lbf").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BreakIn_20min()
        => AnyChunkContains("20분").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BreakIn_PlatenSpeed_60rpm()
        => AnyChunkContains("60 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BreakIn_DIWater_200ml()
        => AnyChunkContains("200 ml/min").Should().BeTrue();

    // === 7. Qualification Step 5 ===

    [Fact]
    public void Chunk_Contains_DummyWafer_5()
        => AnyChunkContains("더미 웨이퍼 5매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TestWafer_3()
        => AnyChunkContains("Test wafer 3매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_QualMRR_10Percent()
        => AnyChunkContains("± 10%").Should().BeTrue();

    // === 8. 패드 교체 후 주의사항 ===

    [Fact]
    public void Chunk_Contains_Initial10_5PercentLower()
        => AnyChunkContains("5% 낮게").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Initial50_Monitor()
        => AnyChunkContains("초기 50매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Enhanced_Conditioning_2Days()
        => AnyChunkContains("초기 2일").Should().BeTrue();

    // === 9. 슬러리 교체 ===

    [Fact]
    public void Chunk_Contains_SOP_CMP_SLR_001()
        => AnyChunkContains("SOP-CMP-SLR-001").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryLevel_10Percent()
        => AnyChunkContains("10%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Slurry_72Hours()
        => AnyChunkContains("72시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_pH_Change_0_5()
        => AnyChunkContains("±0.5").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ParticleSize_D50_20Percent()
        => AnyChunkContains("D50").Should().BeTrue();

    // === 10. 슬러리 교체 절차 ===

    [Fact]
    public void Chunk_Contains_SlurryTempStabilize_30min()
        => AnyChunkContains("30분 전").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_QuickCoupling()
        => AnyChunkContains("퀵 커플링").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Priming()
        => AnyChunkContains("프라이밍").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_FlowRate_5Percent()
        => AnyChunkContains("± 5%").Should().BeTrue();

    // === 11. 슬러리 라인 플러시 ===

    [Fact]
    public void Chunk_Contains_Flush_500ml_10min()
        => AnyChunkContains("500 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TransparentWater()
        => AnyChunkContains("투명해질 때까지").Should().BeTrue();

    // === 12. 슬러리 관리 주의사항 ===

    [Fact]
    public void Chunk_Contains_StorageTemp_15_25()
        => AnyChunkContains("15~25").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_NoMixing()
        => AnyChunkContains("혼합 금지").Should().BeTrue();

    // === 13. 컨디셔너 디스크 교체 ===

    [Fact]
    public void Chunk_Contains_ConditionerDisk_200Hours()
        => AnyChunkContains("200시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_DiamondDetachment()
        => AnyChunkContains("다이아몬드 입자 탈락").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ConditionerScrews_3to4()
        => AnyChunkContains("3~4개").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ConditionerBreakIn_5min()
        => AnyChunkContains("break-in conditioning").Should().BeTrue();

    // === 14. Retaining Ring 교체 ===

    [Fact]
    public void Chunk_Contains_RingThickness_1_5mm()
        => AnyChunkContains("1.5mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Uneven_Wear()
        => AnyChunkContains("편마모").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_AlarmA123_Repeated()
        => AnyChunkContains("A123").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Torque_15Nm()
        => AnyChunkContains("15 N·m").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Ring_4Point_Measurement()
        => AnyChunkContains("4점 측정").Should().BeTrue();

    // === 15. Prompt 검증 ===

    [Fact]
    public void Prompt_Contains_PadReplacementContent()
    {
        var prompt = BuildPromptWith("패드 교체: break-in 7 lbf, 20분, 60 rpm, DI water 200 ml/min");
        prompt.Should().Contain("7 lbf");
        prompt.Should().Contain("20분");
    }

    [Fact]
    public void Prompt_Contains_ChunkText_NotFileName()
    {
        var prompt = BuildPromptWith("슬러리 패드 교체");
        prompt.Should().NotContain("cmp-slurry-pad-replacement.md");
        prompt.Should().Contain("슬러리 패드 교체");
    }
}
