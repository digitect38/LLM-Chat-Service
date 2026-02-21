using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies untested content sections from cmp-process-manual.md,
/// cmp-parameter-optimization.md, and cmp-alarm-code-reference.md.
/// </summary>
public class CmpAdvancedContentTests
{
    // --- Knowledge doc paths ---

    private static readonly string ProcessManualPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-process-manual.md");

    private static readonly string ParamOptPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-parameter-optimization.md");

    private static readonly string AlarmRefPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-alarm-code-reference.md");

    // Lazy-loaded raw text and chunks
    private static readonly Lazy<string> ProcessManualText = new(() => File.ReadAllText(ProcessManualPath));
    private static readonly Lazy<List<string>> ProcessManualChunks = new(() =>
        DocumentIngestor.ChunkText(ProcessManualText.Value, 512, 128));

    private static readonly Lazy<string> ParamOptText = new(() => File.ReadAllText(ParamOptPath));
    private static readonly Lazy<List<string>> ParamOptChunks = new(() =>
        DocumentIngestor.ChunkText(ParamOptText.Value, 512, 128));

    private static readonly Lazy<string> AlarmRefText = new(() => File.ReadAllText(AlarmRefPath));
    private static readonly Lazy<List<string>> AlarmRefChunks = new(() =>
        DocumentIngestor.ChunkText(AlarmRefText.Value, 512, 128));

    private static bool ProcessManualChunkContains(string keyword)
        => ProcessManualChunks.Value.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool ParamOptChunkContains(string keyword)
        => ParamOptChunks.Value.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool AlarmRefChunkContains(string keyword)
        => AlarmRefChunks.Value.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    // === Multi-Step CMP (cmp-process-manual.md §7) ===

    [Fact]
    public void Chunk_Contains_CuCMP_3Step()
        => ProcessManualChunkContains("3-Step").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Step1_BulkCu_MRR_5000_7000()
        => ProcessManualChunkContains("5000~7000").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Step2_Barrier_Pressure_1_5_2_5()
        => ProcessManualChunkContains("1.5~2.5 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Step3_BuffPolish_15_30sec()
        => ProcessManualChunkContains("15~30").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_STI_SiO2SiN_Selectivity_30to1()
        => ProcessManualChunkContains("30:1").Should().BeTrue();

    // === Post-CMP Cleaning (cmp-process-manual.md §9) ===

    [Fact]
    public void Chunk_Contains_PVA_Brush()
        => ProcessManualChunkContains("PVA").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Megasonic_1MHz()
        => ProcessManualChunkContains("1MHz").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SpinDry_3000rpm()
        => ProcessManualChunkContains("3000 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_NH4OH_0_1_to_1_0_Percent()
        => ProcessManualChunkContains("0.1~1.0%").Should().BeTrue();

    // === APC (cmp-parameter-optimization.md §9) ===

    [Fact]
    public void Chunk_Contains_EWMA()
        => ParamOptChunkContains("EWMA").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Lambda_0_3()
        => ParamOptChunkContains("λ = 0.3").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_AdjustmentRange_20Percent()
        => ParamOptChunkContains("± 20%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_50Wafer_LearningPeriod()
        => ParamOptChunkContains("50매").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RunToRun()
        => ParamOptChunkContains("Run-to-Run").Should().BeTrue();

    // === Pad Lifecycle (cmp-parameter-optimization.md §10.1) ===

    [Fact]
    public void Chunk_Contains_Pad_BreakIn_0_50Hours()
        => ParamOptChunkContains("0~50시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Pad_Stable_50_400Hours()
        => ParamOptChunkContains("50~400시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Pad_Aging_400_500Hours()
        => ParamOptChunkContains("400~500시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Pad_EndOfLife_500Hours()
        => ParamOptChunkContains("500시간").Should().BeTrue();

    // === Seasonal Variation (cmp-parameter-optimization.md §10.2) ===

    [Fact]
    public void Chunk_Contains_Summer()
        => ParamOptChunkContains("여름").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Winter()
        => ParamOptChunkContains("겨울").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TempVariation_5C()
        => ParamOptChunkContains("±5°C").Should().BeTrue();

    // === Alarm Code Reference (cmp-alarm-code-reference.md) ===

    [Fact]
    public void Chunk_Contains_A104_ChemicalLeak()
        => AlarmRefChunkContains("A104").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A127_MembraneLeak()
        => AlarmRefChunkContains("A127").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A300_PadLifeWarning()
        => AlarmRefChunkContains("A300").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A301_ConditionerDiskLifeWarning()
        => AlarmRefChunkContains("A301").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A302_RetainingRingLifeWarning()
        => AlarmRefChunkContains("A302").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_A303_SlurryExpiryWarning()
        => AlarmRefChunkContains("A303").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PadWarning_450Hours()
        => AlarmRefChunkContains("500시간").Should().BeTrue();
}
