using FabCopilot.RagService.Services.Bm25;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class Bm25IndexTests
{
    private Bm25Index CreateIndex() => new(k1: 1.2, b: 0.75);

    [Fact]
    public void AddDocument_IncreasesDocumentCount()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "CMP pad replacement procedure");

        index.DocumentCount.Should().Be(1);
    }

    [Fact]
    public void RemoveDocument_DecreasesDocumentCount()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "CMP pad replacement");
        index.AddDocument("doc2", "slurry flow rate");
        index.RemoveDocument("doc1");

        index.DocumentCount.Should().Be(1);
    }

    [Fact]
    public void RemoveByPrefix_RemovesAllMatching()
    {
        var index = CreateIndex();
        index.AddDocument("doc.md:chunk:0", "first chunk");
        index.AddDocument("doc.md:chunk:1", "second chunk");
        index.AddDocument("other.md:chunk:0", "other document");

        index.RemoveByPrefix("doc.md");

        index.DocumentCount.Should().Be(1);
    }

    [Fact]
    public void Clear_RemovesAllDocuments()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "content one");
        index.AddDocument("doc2", "content two");

        index.Clear();

        index.DocumentCount.Should().Be(0);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = CreateIndex();
        var results = index.Search("pad replacement", 10);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "CMP pad replacement");

        var results = index.Search("", 10);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_MatchingDocument_ReturnsIt()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "CMP pad replacement procedure");
        index.AddDocument("doc2", "slurry flow rate monitoring");

        var results = index.Search("pad replacement", 10);

        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be("doc1");
        results[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Search_MultipleMatches_RankedByRelevance()
    {
        var index = CreateIndex();
        // doc1 mentions "pad" once
        index.AddDocument("doc1", "The slurry is applied to the pad surface");
        // doc2 mentions "pad" multiple times — should rank higher
        index.AddDocument("doc2", "CMP pad replacement. Remove old pad and install new pad.");
        // doc3 doesn't mention "pad"
        index.AddDocument("doc3", "wafer processing and cleaning steps");

        var results = index.Search("pad", 10);

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        // doc2 should score higher due to more "pad" mentions
        results[0].DocumentId.Should().Be("doc2");
        // doc3 should not appear (no match)
        results.Should().NotContain(r => r.DocumentId == "doc3");
    }

    [Fact]
    public void Search_Korean_BigramTokenization()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "패드 교체 절차를 설명합니다");
        index.AddDocument("doc2", "슬러리 유량 모니터링");

        var results = index.Search("패드 교체", 10);

        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be("doc1");
    }

    [Fact]
    public void Search_MixedKoreanEnglish()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "CMP 장비의 pad 교체 방법");
        index.AddDocument("doc2", "wafer processing overview");

        var results = index.Search("CMP pad", 10);

        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be("doc1");
    }

    [Fact]
    public void Search_TopK_LimitsResults()
    {
        var index = CreateIndex();
        for (var i = 0; i < 20; i++)
        {
            index.AddDocument($"doc{i}", $"pad replacement document number {i}");
        }

        var results = index.Search("pad", 5);

        results.Should().HaveCount(5);
    }

    [Fact]
    public void AddDocument_Duplicate_UpdatesExisting()
    {
        var index = CreateIndex();
        index.AddDocument("doc1", "old content about slurry");
        index.AddDocument("doc1", "new content about pad replacement");

        index.DocumentCount.Should().Be(1);

        var results = index.Search("pad replacement", 10);
        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be("doc1");

        // Old content should not match
        var oldResults = index.Search("slurry", 10);
        oldResults.Should().BeEmpty();
    }

    // ─── Tokenization Tests ─────────────────────────────────────────

    [Fact]
    public void Tokenize_EnglishText_SplitsOnWhitespace()
    {
        var tokens = Bm25Index.Tokenize("CMP pad replacement");

        tokens.Should().Contain("cmp");
        tokens.Should().Contain("pad");
        tokens.Should().Contain("replacement");
    }

    [Fact]
    public void Tokenize_KoreanText_ProducesBigrams()
    {
        var tokens = Bm25Index.Tokenize("패드교체");

        // "패드교체" → bigrams: "패드", "드교", "교체"
        tokens.Should().Contain("패드");
        tokens.Should().Contain("드교");
        tokens.Should().Contain("교체");
    }

    [Fact]
    public void Tokenize_ShortWords_Filtered()
    {
        var tokens = Bm25Index.Tokenize("a b cd");

        tokens.Should().NotContain("a");
        tokens.Should().NotContain("b");
        tokens.Should().Contain("cd");
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        var tokens = Bm25Index.Tokenize("");
        tokens.Should().BeEmpty();

        var nullTokens = Bm25Index.Tokenize("   ");
        nullTokens.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_CaseNormalization()
    {
        var tokens = Bm25Index.Tokenize("CMP Pad REPLACEMENT");

        tokens.Should().Contain("cmp");
        tokens.Should().Contain("pad");
        tokens.Should().Contain("replacement");
    }
}
