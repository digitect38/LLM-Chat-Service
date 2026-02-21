using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class SynonymDictionaryTests
{
    private static SynonymDictionary CreateTestDictionary()
    {
        return SynonymDictionary.FromGroups(
        [
            ["패드", "polishing pad", "pad", "연마패드"],
            ["슬러리", "slurry", "연마액"],
            ["시기", "기준", "시간", "수명", "주기"],
            ["원인", "이유", "문제", "발생"],
        ]);
    }

    [Fact]
    public void GetSynonyms_KnownTerm_ReturnsWholeGroup()
    {
        var dict = CreateTestDictionary();

        var synonyms = dict.GetSynonyms("패드");

        synonyms.Should().Contain("패드");
        synonyms.Should().Contain("polishing pad");
        synonyms.Should().Contain("pad");
        synonyms.Should().Contain("연마패드");
    }

    [Fact]
    public void GetSynonyms_CaseInsensitive()
    {
        var dict = CreateTestDictionary();

        var synonyms = dict.GetSynonyms("PAD");

        synonyms.Should().Contain("pad");
        synonyms.Should().Contain("패드");
    }

    [Fact]
    public void GetSynonyms_EnglishTerm_ReturnsSameGroup()
    {
        var dict = CreateTestDictionary();

        var synonyms = dict.GetSynonyms("slurry");

        synonyms.Should().Contain("슬러리");
        synonyms.Should().Contain("slurry");
        synonyms.Should().Contain("연마액");
    }

    [Fact]
    public void GetSynonyms_UnknownTerm_ReturnsEmpty()
    {
        var dict = CreateTestDictionary();

        var synonyms = dict.GetSynonyms("unknown_term_xyz");

        synonyms.Should().BeEmpty();
    }

    [Fact]
    public void ExpandAll_ExpandsMultipleKeywords()
    {
        var dict = CreateTestDictionary();

        var expanded = dict.ExpandAll(["패드", "슬러리"]);

        expanded.Should().Contain("패드");
        expanded.Should().Contain("polishing pad");
        expanded.Should().Contain("pad");
        expanded.Should().Contain("슬러리");
        expanded.Should().Contain("slurry");
        expanded.Should().Contain("연마액");
    }

    [Fact]
    public void ExpandAll_MixOfKnownAndUnknown()
    {
        var dict = CreateTestDictionary();

        var expanded = dict.ExpandAll(["패드", "장비"]);

        expanded.Should().Contain("패드");
        expanded.Should().Contain("pad");
        expanded.Should().Contain("장비"); // unknown, stays as-is
    }

    [Fact]
    public void ExpandAll_KoreanExpansion_시기()
    {
        var dict = CreateTestDictionary();

        var expanded = dict.ExpandAll(["시기"]);

        expanded.Should().Contain("시기");
        expanded.Should().Contain("기준");
        expanded.Should().Contain("시간");
        expanded.Should().Contain("수명");
        expanded.Should().Contain("주기");
    }

    [Fact]
    public void GroupCount_ReturnsCorrectCount()
    {
        var dict = CreateTestDictionary();

        dict.GroupCount.Should().Be(4);
    }

    [Fact]
    public void LoadFromFile_NonExistentFile_ReturnsEmptyDictionary()
    {
        var dict = SynonymDictionary.LoadFromFile("/nonexistent/path.json");

        dict.GroupCount.Should().Be(0);
        dict.GetSynonyms("패드").Should().BeEmpty();
    }

    [Fact]
    public void FromGroups_SingleTermGroup_IsIgnored()
    {
        var dict = SynonymDictionary.FromGroups([["alone"]]);

        dict.GroupCount.Should().Be(0);
        dict.GetSynonyms("alone").Should().BeEmpty();
    }

    [Fact]
    public void ExpandAll_EmptyInput_ReturnsEmpty()
    {
        var dict = CreateTestDictionary();

        var expanded = dict.ExpandAll([]);

        expanded.Should().BeEmpty();
    }

    [Fact]
    public void GetSynonyms_ReverseMapping_Works()
    {
        // Looking up "연마패드" should still return the whole pad group
        var dict = CreateTestDictionary();

        var synonyms = dict.GetSynonyms("연마패드");

        synonyms.Should().Contain("패드");
        synonyms.Should().Contain("polishing pad");
        synonyms.Should().Contain("pad");
    }
}
