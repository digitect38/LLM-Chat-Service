using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class BuildSystemPromptRagWarningTests
{
    [Fact]
    public void BuildSystemPrompt_EmptyRagResults_ContainsNoReferenceDocumentsWarning()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>());

        prompt.Should().Contain("NO REFERENCE DOCUMENTS AVAILABLE");
    }

    [Fact]
    public void BuildSystemPrompt_NullRagResults_ContainsNoReferenceDocumentsWarning()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, null!);

        prompt.Should().Contain("NO REFERENCE DOCUMENTS AVAILABLE");
    }

    [Fact]
    public void BuildSystemPrompt_WithResults_ContainsMandatoryUse_AndNoWarning()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "CMP polishing pad lifetime is 500 hours.",
                Score = 0.85f
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("REFERENCE CONTEXT");
        prompt.Should().NotContain("NO REFERENCE DOCUMENTS AVAILABLE");
    }

    [Fact]
    public void BuildSystemPrompt_EmptyRagResults_ContainsGuessProhibition()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>());

        prompt.Should().Contain("추측");
    }
}
