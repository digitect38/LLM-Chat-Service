using System.Text.Json;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class LlmWorkerPromptTests
{
    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsMandatoryUse()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "CMP polishing pad lifetime is 500 hours.", Score = 0.85f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("MANDATORY USE");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_DoesNotContainPermissiveLanguage()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Test content", Score = 0.9f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        // The old permissive prompt allowed ignoring RAG context
        prompt.Should().NotContain("you may ignore it");
        prompt.Should().NotContain("무시해도 됩니다");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsCitationInstructions()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Document content here.", Score = 0.9f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("파일명]에 따르면");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsDocumentNumbers()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "First document.", Score = 0.9f },
            new() { ChunkText = "Second document.", Score = 0.8f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("Document 1");
        prompt.Should().Contain("Document 2");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsNoRelevantDocsFallback()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Some content.", Score = 0.5f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("참고 문서에 관련 정보가 없어");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsNeverContradictRule()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Important data.", Score = 0.85f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("NEVER contradict");
    }

    [Fact]
    public void BuildSystemPrompt_WithoutRagResults_DoesNotContainRagSection()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>());

        prompt.Should().NotContain("REFERENCE DOCUMENTS");
        prompt.Should().NotContain("MANDATORY USE");
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysContainsKoreanRule()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>());

        prompt.Should().Contain("Korean");
        prompt.Should().Contain("한국어");
    }

    [Fact]
    public void BuildSystemPrompt_WithContext_IncludesEquipmentInfo()
    {
        var context = new EquipmentContext
        {
            Module = "Head1",
            Recipe = "OX_CMP_01"
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, new List<RetrievalResult>());

        prompt.Should().Contain("Head1");
        prompt.Should().Contain("OX_CMP_01");
    }

    [Fact]
    public void BuildSystemPrompt_WithJsonElementMetadata_ExtractsFileName()
    {
        // Simulate NATS JSON deserialization: metadata values arrive as JsonElement, not string
        var json = JsonSerializer.Deserialize<JsonElement>("\"cmp-troubleshooting.md\"");
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "CMP pad lifetime guide.",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = json }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("cmp-troubleshooting.md");
    }

    [Fact]
    public void BuildSystemPrompt_WithStringMetadata_ExtractsFileName()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Some content.",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "my-doc.txt" }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("my-doc.txt");
    }
}
