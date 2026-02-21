using FluentAssertions;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class BuildSystemPromptGateTests
{
    private static List<RetrievalResult> MakeRagResults(float score = 0.9f, string chunkText = "sample text")
        => [new RetrievalResult
        {
            DocumentId = "doc-1",
            ChunkText = chunkText,
            Score = score,
            Metadata = new Dictionary<string, object> { ["file_name"] = "test-doc.md" }
        }];

    [Fact]
    public void BuildSystemPrompt_LowConfidence_ContainsGateAWarning()
    {
        var results = MakeRagResults(0.3f);
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: false);
        prompt.Should().Contain("GATE A WARNING");
    }

    [Fact]
    public void BuildSystemPrompt_LowConfidence_ShowsLowConfidenceLabel()
    {
        var results = MakeRagResults(0.3f);
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: false);
        prompt.Should().Contain("LOW CONFIDENCE");
    }

    [Fact]
    public void BuildSystemPrompt_LowConfidence_StillIncludesDocuments()
    {
        var results = MakeRagResults(0.3f, "패드 교체 절차를 설명합니다");
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: false);
        prompt.Should().Contain("패드 교체 절차를 설명합니다");
    }

    [Fact]
    public void BuildSystemPrompt_HighConfidence_NoGateAWarning()
    {
        var results = MakeRagResults(0.9f);
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: true);
        prompt.Should().NotContain("GATE A WARNING");
    }

    [Fact]
    public void BuildSystemPrompt_WithContext_ProcessState()
    {
        var context = new EquipmentContext { ProcessState = "Running" };
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, [], isConfident: true);
        prompt.Should().Contain("Process State: Running");
    }

    [Fact]
    public void BuildSystemPrompt_WithContext_RecentAlarms()
    {
        var context = new EquipmentContext { RecentAlarms = ["A100"] };
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, [], isConfident: true);
        prompt.Should().Contain("A100");
    }

    [Fact]
    public void BuildSystemPrompt_WithContext_AllFieldsNull_NoContextLine()
    {
        var context = new EquipmentContext();
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, [], isConfident: true);
        // With all fields null, no "Current context" should appear
        prompt.Should().NotContain("Current context");
    }

    [Fact]
    public void BuildSystemPrompt_ScoreFormattedTo3Decimals()
    {
        var results = MakeRagResults(0.8567f);
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: true);
        prompt.Should().Contain("0.857");
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysContainsEquipmentId()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-XYZ-999", null, [], isConfident: true);
        prompt.Should().Contain("CMP-XYZ-999");
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysContainsFormattingRules()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [], isConfident: true);
        prompt.Should().Contain("LaTeX");
        prompt.Should().Contain("FORMATTING RULES");
    }
}
