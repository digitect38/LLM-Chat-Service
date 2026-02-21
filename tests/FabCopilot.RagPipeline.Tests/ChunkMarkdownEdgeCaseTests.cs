using FluentAssertions;
using FabCopilot.RagService.Services;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class ChunkMarkdownEdgeCaseTests
{
    [Fact]
    public void ChunkMarkdown_EmptyText_ReturnsEmpty()
    {
        var chunks = DocumentIngestor.ChunkMarkdown("", 512, 128);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkMarkdown_NoHeaders_TreatedAsSingleSection()
    {
        var text = "This is plain text without any markdown headers. Just regular content.";
        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        chunks.Should().HaveCount(1);
        chunks[0].Should().Contain("plain text");
    }

    [Fact]
    public void ChunkMarkdown_DeepNesting_6Levels_BuildsFullPath()
    {
        var text = """
            # Level 1
            ## Level 2
            ### Level 3
            #### Level 4
            ##### Level 5
            ###### Level 6
            Deep nested content here.
            """;

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        // The deepest chunk should contain all 6 levels in its header path
        chunks.Should().Contain(c => c.Contains("Level 6") && c.Contains("Level 1"));
    }

    [Fact]
    public void ChunkMarkdown_HeaderOnly_NoBody_SkipsSection()
    {
        var text = """
            # Header One
            Some body text here.
            # Header Two
            # Header Three
            More body text here.
            """;

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        // Header Two has no body (only whitespace before next header), should be skipped
        chunks.Should().NotContain(c => c.Contains("Header Two") && !c.Contains("Header One") && !c.Contains("Header Three"));
    }

    [Fact]
    public void ChunkMarkdown_VeryLargeSection_SubChunked()
    {
        var largeBody = new string('A', 2000);
        var text = $"# Big Section\n{largeBody}";

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        // 2000 chars should be sub-chunked into multiple chunks
        chunks.Count.Should().BeGreaterThan(1);
        // All chunks should start with the header path prefix
        chunks.Should().OnlyContain(c => c.StartsWith("[# Big Section]"));
    }

    [Fact]
    public void ChunkMarkdown_PreambleBeforeFirstHeader_Preserved()
    {
        var text = """
            This is preamble text before any header.
            It should be preserved as its own chunk.
            # First Header
            Body of first header.
            """;

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        chunks.Should().Contain(c => c.Contains("preamble text"));
    }

    [Fact]
    public void ChunkMarkdown_SiblingHeaders_ResetsDeeperLevels()
    {
        var text = """
            ## Section A
            ### Sub A
            Content under Sub A.
            ## Section B
            Content under Section B only.
            """;

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        // Section B should NOT contain "Sub A" in its header path
        var sectionBChunk = chunks.FirstOrDefault(c => c.Contains("Section B"));
        sectionBChunk.Should().NotBeNull();
        sectionBChunk.Should().NotContain("Sub A");
    }

    [Fact]
    public void SplitMarkdownSections_ReturnsCorrectCount()
    {
        var text = """
            Preamble text.
            # Header 1
            Body 1.
            ## Header 2
            Body 2.
            ### Header 3
            Body 3.
            """;

        var sections = DocumentIngestor.SplitMarkdownSections(text);
        // 1 preamble + 3 headers = 4 sections
        sections.Should().HaveCount(4);
    }

    [Fact]
    public void ChunkMarkdown_AllChunksFromHeadedDoc_HavePrefix()
    {
        var text = """
            # Main
            ## Sub1
            Content for sub 1.
            ## Sub2
            Content for sub 2.
            """;

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        // All chunks from a document with headers should start with '[' bracket
        chunks.Should().OnlyContain(c => c.StartsWith("["));
    }

    [Fact]
    public void ChunkMarkdown_LongHeaderPath_ReducesEffectiveSize()
    {
        // Create a header path that takes ~200 chars
        var longHeader = new string('X', 180);
        var body = new string('B', 400);
        var text = $"# {longHeader}\n{body}";

        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);
        // The effective chunk size is reduced by the header prefix length,
        // so body may be sub-chunked even though it's < 512 chars
        chunks.Should().HaveCountGreaterThanOrEqualTo(1);
        // Every chunk should contain the header prefix
        chunks.Should().OnlyContain(c => c.Contains(longHeader));
    }
}
