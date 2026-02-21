using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Content assertions for 6 previously uncovered CMP knowledge documents:
/// alarm-code-reference, calibration-procedures, equipment-overview,
/// safety-procedures, defect-analysis, metrology-inspection.
/// </summary>
public class CmpComprehensiveContentTests
{
    private static readonly string DocsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs");

    private static Lazy<List<string>> LoadChunksLazy(string fileName)
        => new(() =>
        {
            var text = File.ReadAllText(Path.Combine(DocsDir, fileName));
            return DocumentIngestor.ChunkText(text, 512, 128);
        });

    private static bool AnyChunkContains(List<string> chunks, string keyword)
        => chunks.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    // ─── cmp-alarm-code-reference.md ────────────────────────────────────

    private static readonly Lazy<List<string>> AlarmChunks =
        LoadChunksLazy("cmp-alarm-code-reference.md");

    [Fact]
    public void AlarmRef_Contains_A100_EmergencyStop()
        => AnyChunkContains(AlarmChunks.Value, "A100").Should().BeTrue();

    [Fact]
    public void AlarmRef_Contains_A101_VacuumFailure()
        => AnyChunkContains(AlarmChunks.Value, "Vacuum").Should().BeTrue();

    [Fact]
    public void AlarmRef_Contains_A104_ChemicalLeak()
        => AnyChunkContains(AlarmChunks.Value, "Chemical Leak").Should().BeTrue();

    [Fact]
    public void AlarmRef_Contains_A110_PlatenOverload()
        => AnyChunkContains(AlarmChunks.Value, "Platen Overload").Should().BeTrue();

    [Fact]
    public void AlarmRef_Contains_A300_ConsumableLife()
        => AnyChunkContains(AlarmChunks.Value, "A300").Should().BeTrue();

    [Fact]
    public void AlarmRef_Contains_SeverityClassification()
        => AnyChunkContains(AlarmChunks.Value, "Critical").Should().BeTrue();

    // ─── cmp-calibration-procedures.md ──────────────────────────────────

    private static readonly Lazy<List<string>> CalibrationChunks =
        LoadChunksLazy("cmp-calibration-procedures.md");

    [Fact]
    public void Calibration_Contains_Pressure_0_1psi()
        => AnyChunkContains(CalibrationChunks.Value, "0.1 psi").Should().BeTrue();

    [Fact]
    public void Calibration_Contains_Temperature_1C()
        => AnyChunkContains(CalibrationChunks.Value, "1°C").Should().BeTrue();

    [Fact]
    public void Calibration_Contains_Speed_1rpm()
        => AnyChunkContains(CalibrationChunks.Value, "1 rpm").Should().BeTrue();

    [Fact]
    public void Calibration_Contains_PressureHoldTest()
        => AnyChunkContains(CalibrationChunks.Value, "Pressure Hold Test").Should().BeTrue();

    [Fact]
    public void Calibration_Contains_RobotTeaching()
        => AnyChunkContains(CalibrationChunks.Value, "Robot Teaching").Should().BeTrue();

    // ─── cmp-equipment-overview.md ──────────────────────────────────────

    private static readonly Lazy<List<string>> EquipmentChunks =
        LoadChunksLazy("cmp-equipment-overview.md");

    [Fact]
    public void Equipment_Contains_Platen_600_800mm()
        => AnyChunkContains(EquipmentChunks.Value, "600~800mm").Should().BeTrue();

    [Fact]
    public void Equipment_Contains_SpeedRange_20_150rpm()
        => AnyChunkContains(EquipmentChunks.Value, "20~150 rpm").Should().BeTrue();

    [Fact]
    public void Equipment_Contains_Vacuum_600mmHg()
        => AnyChunkContains(EquipmentChunks.Value, "-600 mmHg").Should().BeTrue();

    [Fact]
    public void Equipment_Contains_TIR_25um()
        => AnyChunkContains(EquipmentChunks.Value, "25 μm").Should().BeTrue();

    [Fact]
    public void Equipment_Contains_RobotAccuracy()
        => AnyChunkContains(EquipmentChunks.Value, "0.5mm").Should().BeTrue();

    // ─── cmp-safety-procedures.md ───────────────────────────────────────

    private static readonly Lazy<List<string>> SafetyChunks =
        LoadChunksLazy("cmp-safety-procedures.md");

    [Fact]
    public void Safety_Contains_PPE()
        => AnyChunkContains(SafetyChunks.Value, "PPE").Should().BeTrue();

    [Fact]
    public void Safety_Contains_LOTO()
        => AnyChunkContains(SafetyChunks.Value, "Lock-Out").Should().BeTrue();

    [Fact]
    public void Safety_Contains_ChemicalSpill()
        => AnyChunkContains(SafetyChunks.Value, "Chemical Spill").Should().BeTrue();

    [Fact]
    public void Safety_Contains_EmergencyStop()
        => AnyChunkContains(SafetyChunks.Value, "E-Stop").Should().BeTrue();

    [Fact]
    public void Safety_Contains_HF_Exposure()
        => AnyChunkContains(SafetyChunks.Value, "HF").Should().BeTrue();

    // ─── cmp-defect-analysis.md ─────────────────────────────────────────

    private static readonly Lazy<List<string>> DefectChunks =
        LoadChunksLazy("cmp-defect-analysis.md");

    [Fact]
    public void DefectAnalysis_Contains_Dishing()
        => AnyChunkContains(DefectChunks.Value, "Dishing").Should().BeTrue();

    [Fact]
    public void DefectAnalysis_Contains_Erosion()
        => AnyChunkContains(DefectChunks.Value, "Erosion").Should().BeTrue();

    [Fact]
    public void DefectAnalysis_Contains_ScratchClassification()
    {
        AnyChunkContains(DefectChunks.Value, "Micro-Scratch").Should().BeTrue();
        AnyChunkContains(DefectChunks.Value, "Macro-Scratch").Should().BeTrue();
    }

    // ─── cmp-metrology-inspection.md ────────────────────────────────────

    private static readonly Lazy<List<string>> MetrologyChunks =
        LoadChunksLazy("cmp-metrology-inspection.md");

    [Fact]
    public void Metrology_Contains_Ellipsometer()
        => AnyChunkContains(MetrologyChunks.Value, "Ellipsometer").Should().BeTrue();

    [Fact]
    public void Metrology_Contains_49PointMap()
        => AnyChunkContains(MetrologyChunks.Value, "49").Should().BeTrue();
}
