using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-troubleshooting.md (original) survive chunking.
/// </summary>
public class CmpOriginalTroubleshootingContentTests
{
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-troubleshooting.md");

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
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-troubleshooting.md" }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // === 1. Head Zone 압력 진동 ===

    [Fact]
    public void Chunk_Contains_PressureOscillation()
        => AnyChunkContains("Pressure Oscillation").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone7()
        => AnyChunkContains("Zone 7").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone8()
        => AnyChunkContains("Zone 8").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Oscillation_0_5_2Hz()
        => AnyChunkContains("0.5~2Hz").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_WIWNU()
        => AnyChunkContains("WIWNU").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MRR_Unstable()
        => AnyChunkContains("MRR").Should().BeTrue();

    // === 2. 원인 ===

    [Fact]
    public void Chunk_Contains_PadGlazing()
        => AnyChunkContains("Pad Glazing").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RetainingRingWear()
        => AnyChunkContains("Retaining Ring 마모").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MembraneLeak()
        => AnyChunkContains("Membrane Leak").Should().BeTrue();

    // === 3. 조치 방법 ===

    [Fact]
    public void Chunk_Contains_Conditioning_60sec()
        => AnyChunkContains("60초").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Conditioning_5lbf()
        => AnyChunkContains("5 lbf").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadReplace_SOP()
        => AnyChunkContains("SOP-CMP-PAD-001").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RingThickness_2_0mm()
        => AnyChunkContains("2.0mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureHoldTest()
        => AnyChunkContains("pressure hold test").Should().BeTrue();

    // === 4. 알람 A123 ===

    [Fact]
    public void Chunk_Contains_AlarmA123()
        => AnyChunkContains("A123").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_HeadPressureOutOfRange()
        => AnyChunkContains("Head Pressure Out of Range").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Pressure_15Percent()
        => AnyChunkContains("±15%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RingThreshold_1_5mm()
        => AnyChunkContains("1.5mm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ImmediateStop()
        => AnyChunkContains("즉시 장비 정지").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PressureHold_30sec()
        => AnyChunkContains("30초").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_QualificationWafer()
        => AnyChunkContains("qualification").Should().BeTrue();

    // === 5. Slurry Flow Rate 이상 ===

    [Fact]
    public void Chunk_Contains_SlurryFlowRate()
        => AnyChunkContains("Slurry Flow Rate").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_FlowDecrease_20Percent()
        => AnyChunkContains("20%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TorqueIncrease()
        => AnyChunkContains("Torque").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Crystallization()
        => AnyChunkContains("결정화").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_LineFlush_5min()
        => AnyChunkContains("DI water 5분").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PumpPressure_15_25psi()
        => AnyChunkContains("15~25 psi").Should().BeTrue();

    // === 6. Prompt 검증 ===

    [Fact]
    public void Prompt_Contains_OscillationContent()
    {
        var prompt = BuildPromptWith("Zone 7, Zone 8 압력 진동 0.5~2Hz, Pad Glazing 원인");
        prompt.Should().Contain("Zone 7");
        prompt.Should().Contain("0.5~2Hz");
    }

    [Fact]
    public void Prompt_Contains_A123Content()
    {
        var prompt = BuildPromptWith("알람 A123: Head Pressure Out of Range, Retaining ring 두께 < 1.5mm");
        prompt.Should().Contain("A123");
        prompt.Should().Contain("1.5mm");
    }

    [Fact]
    public void Prompt_Contains_SourceFileName()
    {
        var prompt = BuildPromptWith("트러블슈팅 가이드 내용");
        prompt.Should().Contain("cmp-troubleshooting.md");
    }
}
