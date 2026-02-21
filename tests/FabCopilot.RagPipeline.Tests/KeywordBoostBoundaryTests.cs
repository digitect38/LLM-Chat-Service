using FluentAssertions;
using FabCopilot.RagService;
using FabCopilot.VectorStore.Models;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class KeywordBoostBoundaryTests
{
    private static VectorSearchResult MakeResult(string id, float score, string text)
        => new(id, score, new Dictionary<string, object> { ["text"] = text });

    [Fact]
    public void RerankWithKeywordBoost_ZeroBoostParam_NoEffect()
    {
        var results = new[]
        {
            MakeResult("a", 0.5f, "polishing pad replacement"),
            MakeResult("b", 0.9f, "slurry flow rate")
        };

        var reranked = RagWorker.RerankWithKeywordBoost(results, ["pad"], boostPerKeyword: 0.0f);
        // With zero boost, original score order should be preserved: b (0.9) > a (0.5)
        reranked[0].Id.Should().Be("b");
    }

    [Fact]
    public void RerankWithKeywordBoost_MissingTextPayload_NoBoost()
    {
        var resultNoText = new VectorSearchResult("no-text", 0.5f, new Dictionary<string, object>());
        var resultWithText = MakeResult("has-text", 0.4f, "pad replacement guide");

        var reranked = RagWorker.RerankWithKeywordBoost(
            new[] { resultNoText, resultWithText }, ["pad"]);

        // "has-text" gets boosted by 0.10 → 0.50, "no-text" stays 0.50 → tied, stable sort
        reranked.Should().HaveCount(2);
    }

    [Fact]
    public void RerankWithKeywordBoost_EmptyTextPayload_NoBoost()
    {
        var resultEmpty = MakeResult("empty", 0.8f, "");
        var resultText = MakeResult("full", 0.7f, "polishing pad");

        var reranked = RagWorker.RerankWithKeywordBoost(
            new[] { resultEmpty, resultText }, ["pad"]);

        // "empty" has no keyword matches, stays 0.8. "full" gets boost → 0.8
        reranked[0].Id.Should().Be("empty"); // tied but empty was first
    }

    [Fact]
    public void RerankWithKeywordBoost_SubstringMatch_Counts()
    {
        var results = new[]
        {
            MakeResult("a", 0.5f, "polishing pad replacement procedure"),
            MakeResult("b", 0.5f, "slurry flow measurement")
        };

        var reranked = RagWorker.RerankWithKeywordBoost(results, ["pad"]);
        // "a" contains "pad" → boosted to 0.60, "b" doesn't → stays 0.50
        reranked[0].Id.Should().Be("a");
    }

    [Fact]
    public void RerankWithKeywordBoost_KoreanKeywords_Matching()
    {
        var results = new[]
        {
            MakeResult("ko", 0.5f, "패드 교체 절차를 설명합니다"),
            MakeResult("en", 0.5f, "slurry flow rate configuration")
        };

        var reranked = RagWorker.RerankWithKeywordBoost(results, ["패드"]);
        reranked[0].Id.Should().Be("ko");
    }

    [Fact]
    public void RerankWithKeywordBoost_LargeResultSet_100Items()
    {
        var results = Enumerable.Range(0, 100)
            .Select(i => MakeResult($"doc-{i}", 0.5f, i % 2 == 0 ? "contains pad keyword" : "no match here"))
            .ToArray();

        var reranked = RagWorker.RerankWithKeywordBoost(results, ["pad"]);
        reranked.Should().HaveCount(100);
        // Even-indexed docs should appear first (they have keyword matches)
        reranked[0].Id.Should().StartWith("doc-");
    }

    [Fact]
    public void RerankWithKeywordBoost_AllSameBaseScore_OrderByBoost()
    {
        var results = new[]
        {
            MakeResult("zero", 0.70f, "no keywords here"),
            MakeResult("one", 0.70f, "pad is here"),
            MakeResult("two", 0.70f, "pad and slurry are here")
        };

        var reranked = RagWorker.RerankWithKeywordBoost(results, ["pad", "slurry"]);
        // "two" matches 2 keywords (boost 0.20), "one" matches 1 (boost 0.10), "zero" matches 0
        reranked[0].Id.Should().Be("two");
        reranked[1].Id.Should().Be("one");
        reranked[2].Id.Should().Be("zero");
    }

    [Fact]
    public void RerankWithKeywordBoost_BoostCapApplied_ThreeKeywords()
    {
        var results = new[]
        {
            MakeResult("all-match", 0.50f, "pad slurry conditioner replacement"),
            MakeResult("no-match", 0.69f, "unrelated text about something else")
        };

        // 3 keywords × 0.10 = 0.30, but capped at 0.20 → effective boost = 0.20
        var reranked = RagWorker.RerankWithKeywordBoost(results, ["pad", "slurry", "conditioner"]);
        // all-match: 0.50 + 0.20 = 0.70, no-match: 0.69 + 0.00 = 0.69
        reranked[0].Id.Should().Be("all-match");
    }
}
