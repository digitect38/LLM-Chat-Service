using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class GraphContextPromptTests
{
    [Fact]
    public void BuildSystemPrompt_WithGraphContext_IncludesKnowledgeGraphSection()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "CMP polishing pad lifetime is 500 hours.",
                Score = 0.85f,
                GraphContext = "[관련 지식 그래프]\n\n엔티티:\n- polishing pad [Component]\n\n관계:\n- polishing pad -[UsedIn]-> CMP"
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("[KNOWLEDGE GRAPH CONTEXT]");
        prompt.Should().Contain("polishing pad [Component]");
        prompt.Should().Contain("polishing pad -[UsedIn]-> CMP");
    }

    [Fact]
    public void BuildSystemPrompt_WithoutGraphContext_DoesNotIncludeKnowledgeGraphSection()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "CMP polishing pad lifetime is 500 hours.",
                Score = 0.85f,
                GraphContext = null
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("[KNOWLEDGE GRAPH CONTEXT]");
    }

    [Fact]
    public void BuildSystemPrompt_WithEmptyGraphContext_DoesNotIncludeKnowledgeGraphSection()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Some content.",
                Score = 0.85f,
                GraphContext = "   "
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("[KNOWLEDGE GRAPH CONTEXT]");
    }

    [Fact]
    public void BuildSystemPrompt_DeduplicatesGraphContexts()
    {
        var sharedGraphContext = "[관련 지식 그래프]\n\n엔티티:\n- CMP [Equipment]";

        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "First chunk.",
                Score = 0.9f,
                GraphContext = sharedGraphContext
            },
            new()
            {
                ChunkText = "Second chunk.",
                Score = 0.8f,
                GraphContext = sharedGraphContext
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        // The graph context should appear only once (deduplicated)
        var count = CountOccurrences(prompt, "CMP [Equipment]");
        count.Should().Be(1);
    }

    [Fact]
    public void BuildSystemPrompt_MultipleDistinctGraphContexts_IncludesAll()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "First chunk.",
                Score = 0.9f,
                GraphContext = "엔티티:\n- polishing pad [Component]"
            },
            new()
            {
                ChunkText = "Second chunk.",
                Score = 0.8f,
                GraphContext = "엔티티:\n- scratch [Symptom]"
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("[KNOWLEDGE GRAPH CONTEXT]");
        prompt.Should().Contain("polishing pad [Component]");
        prompt.Should().Contain("scratch [Symptom]");
    }

    [Fact]
    public void BuildSystemPrompt_MixedGraphContext_OnlyIncludesNonEmpty()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "First chunk.",
                Score = 0.9f,
                GraphContext = "엔티티:\n- CMP [Equipment]"
            },
            new()
            {
                ChunkText = "Second chunk.",
                Score = 0.8f,
                GraphContext = null
            },
            new()
            {
                ChunkText = "Third chunk.",
                Score = 0.7f,
                GraphContext = ""
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("[KNOWLEDGE GRAPH CONTEXT]");
        prompt.Should().Contain("CMP [Equipment]");
    }

    [Fact]
    public void BuildSystemPrompt_NoRagResults_NoGraphContext()
    {
        var ragResults = new List<RetrievalResult>();

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("[KNOWLEDGE GRAPH CONTEXT]");
    }

    [Fact]
    public void BuildSystemPrompt_LowConfidence_WithGraphContext_StillIncludesGraphSection()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Low confidence content.",
                Score = 0.3f,
                GraphContext = "엔티티:\n- pad_wear [Cause]"
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults, isConfident: false);

        prompt.Should().Contain("[LOW CONFIDENCE WARNING]");
        prompt.Should().Contain("[KNOWLEDGE GRAPH CONTEXT]");
        prompt.Should().Contain("pad_wear [Cause]");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
