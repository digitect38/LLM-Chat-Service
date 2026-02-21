using FabCopilot.RagService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class KeywordExtractionTests
{
    [Fact]
    public void ExtractKeywords_KoreanParticleRemoval_ExtractsBaseWord()
    {
        // "패드를" → particle "를" removed → "패드" extracted
        var keywords = RagWorker.ExtractKeywords("패드를");

        keywords.Should().Contain("패드");
    }

    [Fact]
    public void ExtractKeywords_ShortWords_FilteredOut()
    {
        // Single-character words (length < 2) should be excluded
        var keywords = RagWorker.ExtractKeywords("a b cd ef");

        keywords.Should().NotContain("a");
        keywords.Should().NotContain("b");
        keywords.Should().Contain("cd");
        keywords.Should().Contain("ef");
    }

    [Fact]
    public void ExtractKeywords_DuplicateKeywords_Deduplicated()
    {
        var keywords = RagWorker.ExtractKeywords("CMP CMP CMP");

        keywords.Should().HaveCount(1);
        keywords.Should().Contain("CMP");
    }

    [Fact]
    public void ExtractKeywords_EnglishKeywords_PreservedAsIs()
    {
        // English keywords with length >= 2 are kept
        var keywords = RagWorker.ExtractKeywords("CMP pad");

        keywords.Should().Contain("CMP");
        keywords.Should().Contain("pad");
    }

    [Fact]
    public void ExpandKeywords_KnownKeyword_ExpandsWithRelatedTerms()
    {
        // "시기" maps to ["기준", "시간", "수명", "주기"] in fallback map
        var keywords = new List<string> { "시기" };

        var expanded = RagWorker.ExpandKeywords(keywords);

        expanded.Should().Contain("시기");
        expanded.Should().Contain("기준");
        expanded.Should().Contain("시간");
        expanded.Should().Contain("수명");
        expanded.Should().Contain("주기");
    }

    [Fact]
    public void ExpandKeywords_UnknownKeyword_StaysAsIs()
    {
        var keywords = new List<string> { "연마" };

        var expanded = RagWorker.ExpandKeywords(keywords);

        expanded.Should().HaveCount(1);
        expanded.Should().Contain("연마");
    }
}
