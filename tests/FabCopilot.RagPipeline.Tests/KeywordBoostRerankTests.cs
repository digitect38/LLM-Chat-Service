using FabCopilot.RagService;
using FabCopilot.VectorStore.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class KeywordBoostRerankTests
{
    private static VectorSearchResult MakeResult(string id, float score, string text)
        => new(id, score, new Dictionary<string, object> { ["text"] = text });

    [Fact]
    public void RerankWithKeywordBoost_KeywordMatchingChunk_RanksHigher()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult("no-match", 0.80f, "unrelated content here"),
            MakeResult("match", 0.75f, "CMP pad replacement guide")
        };
        var keywords = new List<string> { "CMP", "pad" };

        var reranked = RagWorker.RerankWithKeywordBoost(results, keywords);

        // "match" (0.75 + 0.20 boost) = 0.95 should beat "no-match" (0.80 + 0) = 0.80
        reranked[0].Id.Should().Be("match");
        reranked[1].Id.Should().Be("no-match");
    }

    [Fact]
    public void RerankWithKeywordBoost_BoostCap_CannotExceedPointTwo()
    {
        // 5 keywords all matching × 0.10 = 0.50, but capped at 0.20
        var results = new List<VectorSearchResult>
        {
            MakeResult("many-kw", 0.50f, "aa bb cc dd ee"),
            MakeResult("high-score", 0.80f, "no keywords here")
        };
        var keywords = new List<string> { "aa", "bb", "cc", "dd", "ee" };

        var reranked = RagWorker.RerankWithKeywordBoost(results, keywords);

        // "many-kw" boosted = 0.50 + 0.20 (capped) = 0.70, "high-score" = 0.80
        reranked[0].Id.Should().Be("high-score");
        reranked[1].Id.Should().Be("many-kw");
    }

    [Fact]
    public void RerankWithKeywordBoost_NoKeywords_OriginalScoreOrder()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult("low", 0.30f, "some text"),
            MakeResult("high", 0.90f, "other text")
        };
        var keywords = new List<string>();

        var reranked = RagWorker.RerankWithKeywordBoost(results, keywords);

        reranked[0].Id.Should().Be("high");
        reranked[1].Id.Should().Be("low");
    }

    [Fact]
    public void RerankWithKeywordBoost_CaseInsensitiveMatching()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult("upper", 0.60f, "CMP pad text"),
            MakeResult("none", 0.60f, "unrelated content")
        };
        var keywords = new List<string> { "cmp" };

        var reranked = RagWorker.RerankWithKeywordBoost(results, keywords);

        // "upper" gets boost from case-insensitive match of "cmp" in "CMP pad text"
        reranked[0].Id.Should().Be("upper");
    }

    [Fact]
    public void RerankWithKeywordBoost_EmptyResults_ReturnsEmpty()
    {
        var results = new List<VectorSearchResult>();
        var keywords = new List<string> { "CMP" };

        var reranked = RagWorker.RerankWithKeywordBoost(results, keywords);

        reranked.Should().BeEmpty();
    }

    [Fact]
    public void RerankWithKeywordBoost_LowScoreWithManyMatches_CannotOvertakeHighScore()
    {
        // Low-score chunk (0.50) with 5 keyword matches → boost capped at 0.20 → 0.70
        // High-score chunk (0.80) with 0 keyword matches → 0.80
        var results = new List<VectorSearchResult>
        {
            MakeResult("low-many", 0.50f, "aa bb cc dd ee"),
            MakeResult("high-none", 0.80f, "no matching keywords")
        };
        var keywords = new List<string> { "aa", "bb", "cc", "dd", "ee" };

        var reranked = RagWorker.RerankWithKeywordBoost(results, keywords);

        reranked[0].Id.Should().Be("high-none");
        reranked[1].Id.Should().Be("low-many");
    }
}
