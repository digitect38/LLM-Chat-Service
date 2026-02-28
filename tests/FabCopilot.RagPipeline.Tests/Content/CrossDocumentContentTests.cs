using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Cross-document tests: verify that multi-document RAG results are correctly
/// assembled into the system prompt, and that consistent facts across documents
/// are present in chunks.
/// </summary>
public class CrossDocumentContentTests
{
    private static readonly string DocsRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs");

    private static List<string> LoadChunks(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(DocsRoot, fileName));
        return DocumentIngestor.ChunkText(text, 512, 128);
    }

    private static bool AnyChunkContains(List<string> chunks, string keyword)
        => chunks.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    // === 1. 일관된 수치 교차 검증 (여러 문서에 동일 값이 존재) ===

    [Fact]
    public void PadLifetime_500Hours_InManualAndMaintenance()
    {
        var manualChunks = LoadChunks("cmp-process-manual.md");
        var maintChunks = LoadChunks("cmp-maintenance-guide.md");
        var replaceChunks = LoadChunks("cmp-slurry-pad-replacement.md");

        // 500시간은 maintenance-guide와 slurry-pad-replacement에 모두 존재
        AnyChunkContains(maintChunks, "500시간").Should().BeTrue();
        AnyChunkContains(replaceChunks, "500시간").Should().BeTrue();
    }

    [Fact]
    public void RetainingRing_1_5mm_InTroubleshootingAndReplacement()
    {
        var troubleChunks = LoadChunks("cmp-general-troubleshooting.md");
        var replaceChunks = LoadChunks("cmp-slurry-pad-replacement.md");

        AnyChunkContains(troubleChunks, "1.5mm").Should().BeTrue();
        AnyChunkContains(replaceChunks, "1.5mm").Should().BeTrue();
    }

    [Fact]
    public void A123_InTroubleshootingAndReplacement()
    {
        var troubleChunks = LoadChunks("cmp-general-troubleshooting.md");
        var replaceChunks = LoadChunks("cmp-slurry-pad-replacement.md");

        AnyChunkContains(troubleChunks, "A123").Should().BeTrue();
        AnyChunkContains(replaceChunks, "A123").Should().BeTrue();
    }

    [Fact]
    public void Slurry_72Hours_InManualAndReplacement()
    {
        var manualChunks = LoadChunks("cmp-process-manual.md");
        var replaceChunks = LoadChunks("cmp-slurry-pad-replacement.md");

        AnyChunkContains(manualChunks, "72시간").Should().BeTrue();
        AnyChunkContains(replaceChunks, "72시간").Should().BeTrue();
    }

    [Fact]
    public void PressureHoldTest_InTroubleshootingAndMaintenance()
    {
        var maintChunks = LoadChunks("cmp-maintenance-guide.md");
        var troubleChunks = LoadChunks("cmp-general-troubleshooting.md");

        AnyChunkContains(maintChunks, "Pressure Hold Test").Should().BeTrue();
        AnyChunkContains(troubleChunks, "Pressure Hold Test").Should().BeTrue();
    }

    [Fact]
    public void WIWNU_InAllDocuments()
    {
        var docs = new[] {
            "cmp-process-manual.md",
            "cmp-maintenance-guide.md",
            "cmp-general-troubleshooting.md",
            "cmp-parameter-optimization.md"
        };

        foreach (var doc in docs)
        {
            var chunks = LoadChunks(doc);
            AnyChunkContains(chunks, "WIWNU").Should().BeTrue($"WIWNU should exist in {doc}");
        }
    }

    [Fact]
    public void MRR_InAllDocuments()
    {
        var docs = new[] {
            "cmp-process-manual.md",
            "cmp-maintenance-guide.md",
            "cmp-general-troubleshooting.md",
            "cmp-slurry-pad-replacement.md",
            "cmp-parameter-optimization.md"
        };

        foreach (var doc in docs)
        {
            var chunks = LoadChunks(doc);
            AnyChunkContains(chunks, "MRR").Should().BeTrue($"MRR should exist in {doc}");
        }
    }

    [Fact]
    public void SOP_PAD_001_InMaintenanceAndReplacement()
    {
        var maintChunks = LoadChunks("cmp-maintenance-guide.md");
        var replaceChunks = LoadChunks("cmp-slurry-pad-replacement.md");

        AnyChunkContains(maintChunks, "SOP 참조").Should().BeTrue();
        AnyChunkContains(replaceChunks, "SOP-CMP-PAD-001").Should().BeTrue();
    }

    [Fact]
    public void Glazing_InMaintenanceAndTroubleshooting()
    {
        var maintChunks = LoadChunks("cmp-maintenance-guide.md");
        var troubleChunks = LoadChunks("cmp-general-troubleshooting.md");

        AnyChunkContains(maintChunks, "glazing").Should().BeTrue();
        AnyChunkContains(troubleChunks, "Pad glazing").Should().BeTrue();
    }

    [Fact]
    public void BreakIn_InReplacementAndOptimization()
    {
        var replaceChunks = LoadChunks("cmp-slurry-pad-replacement.md");
        var manualChunks = LoadChunks("cmp-process-manual.md");

        AnyChunkContains(replaceChunks, "break-in").Should().BeTrue();
        AnyChunkContains(manualChunks, "break-in").Should().BeTrue();
    }

    // === 2. 멀티 문서 Prompt 조합 테스트 ===

    [Fact]
    public void Prompt_MultiDocument_ContainsBothSources()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "패드 교체 기준: 500시간 초과",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-slurry-pad-replacement.md" }
            },
            new()
            {
                ChunkText = "Daily PM: 30분, Weekly PM: 2시간",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-maintenance-guide.md" }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results);

        prompt.Should().NotContain("cmp-slurry-pad-replacement.md");
        prompt.Should().NotContain("cmp-maintenance-guide.md");
        prompt.Should().Contain("500시간");
        prompt.Should().Contain("Daily PM");
    }

    [Fact]
    public void Prompt_MultiDocument_ContainsDocumentNumbers()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Zone 1~5 Pressure 3.0 psi",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-process-manual.md" }
            },
            new()
            {
                ChunkText = "알람 A123: Head Pressure",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-general-troubleshooting.md" }
            },
            new()
            {
                ChunkText = "WIWNU 목표 < 3%",
                Score = 0.8f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-parameter-optimization.md" }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results);

        prompt.Should().NotContain("Document 1");
        prompt.Should().Contain("<context>");
        prompt.Should().Contain("Zone 1~5 Pressure 3.0 psi");
        prompt.Should().Contain("알람 A123: Head Pressure");
        prompt.Should().Contain("WIWNU 목표 < 3%");
    }

    [Fact]
    public void Prompt_MultiDocument_AllChunksPresent()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Platen Speed 93 rpm, Carrier Speed 87 rpm",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-process-manual.md" }
            },
            new()
            {
                ChunkText = "Pressure Hold Test: 30초간 압력 강하 < 0.3 psi",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-maintenance-guide.md" }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results);

        prompt.Should().Contain("93 rpm");
        prompt.Should().Contain("87 rpm");
        prompt.Should().Contain("0.3 psi");
    }

    // === 3. 각 문서 청크 수 검증 (chunking 정상 동작) ===

    [Fact]
    public void ProcessManual_HasMultipleChunks()
    {
        var chunks = LoadChunks("cmp-process-manual.md");
        chunks.Count.Should().BeGreaterThan(5);
    }

    [Fact]
    public void MaintenanceGuide_HasMultipleChunks()
    {
        var chunks = LoadChunks("cmp-maintenance-guide.md");
        chunks.Count.Should().BeGreaterThan(5);
    }

    [Fact]
    public void GeneralTroubleshooting_HasMultipleChunks()
    {
        var chunks = LoadChunks("cmp-general-troubleshooting.md");
        chunks.Count.Should().BeGreaterThan(5);
    }

    [Fact]
    public void SlurryPadReplacement_HasMultipleChunks()
    {
        var chunks = LoadChunks("cmp-slurry-pad-replacement.md");
        chunks.Count.Should().BeGreaterThan(5);
    }

    [Fact]
    public void ParameterOptimization_HasMultipleChunks()
    {
        var chunks = LoadChunks("cmp-parameter-optimization.md");
        chunks.Count.Should().BeGreaterThan(5);
    }

    // === 4. 청크 크기 제한 검증 ===

    [Theory]
    [InlineData("cmp-process-manual.md")]
    [InlineData("cmp-maintenance-guide.md")]
    [InlineData("cmp-general-troubleshooting.md")]
    [InlineData("cmp-slurry-pad-replacement.md")]
    [InlineData("cmp-parameter-optimization.md")]
    public void AllChunks_RespectMaxSize(string fileName)
    {
        var chunks = LoadChunks(fileName);
        foreach (var chunk in chunks)
        {
            chunk.Length.Should().BeLessThanOrEqualTo(512,
                $"chunk in {fileName} exceeds 512 chars");
        }
    }

    // === 5. 빈 청크 없음 검증 ===

    [Theory]
    [InlineData("cmp-process-manual.md")]
    [InlineData("cmp-maintenance-guide.md")]
    [InlineData("cmp-general-troubleshooting.md")]
    [InlineData("cmp-slurry-pad-replacement.md")]
    [InlineData("cmp-parameter-optimization.md")]
    public void AllChunks_NotEmpty(string fileName)
    {
        var chunks = LoadChunks(fileName);
        foreach (var chunk in chunks)
        {
            chunk.Should().NotBeNullOrWhiteSpace(
                $"chunk in {fileName} should not be empty");
        }
    }

    // === 6. 전체 텍스트 커버리지 (모든 핵심 단어가 어딘가에 존재) ===

    [Theory]
    [InlineData("cmp-process-manual.md", "Preston")]
    [InlineData("cmp-process-manual.md", "FOUP")]
    [InlineData("cmp-process-manual.md", "EPD")]
    [InlineData("cmp-maintenance-guide.md", "Daily PM")]
    [InlineData("cmp-maintenance-guide.md", "Quarterly PM")]
    [InlineData("cmp-maintenance-guide.md", "TIR")]
    [InlineData("cmp-general-troubleshooting.md", "Micro-Scratch")]
    [InlineData("cmp-general-troubleshooting.md", "Macro-Scratch")]
    [InlineData("cmp-general-troubleshooting.md", "Emergency Stop")]
    [InlineData("cmp-slurry-pad-replacement.md", "IC1000")]
    [InlineData("cmp-slurry-pad-replacement.md", "프라이밍")]
    [InlineData("cmp-slurry-pad-replacement.md", "편마모")]
    [InlineData("cmp-parameter-optimization.md", "DOE")]
    [InlineData("cmp-parameter-optimization.md", "Western Electric")]
    [InlineData("cmp-parameter-optimization.md", "M-Shape")]
    [InlineData("cmp-general-troubleshooting.md", "Zone 7")]
    [InlineData("cmp-general-troubleshooting.md", "Membrane Leak")]
    [InlineData("cmp-general-troubleshooting.md", "결정화")]
    public void KeyTerm_ExistsInChunks(string fileName, string keyword)
    {
        var chunks = LoadChunks(fileName);
        AnyChunkContains(chunks, keyword).Should().BeTrue(
            $"'{keyword}' should exist in chunks of {fileName}");
    }
}
