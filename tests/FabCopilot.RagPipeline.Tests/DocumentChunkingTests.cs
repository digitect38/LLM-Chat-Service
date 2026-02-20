using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class DocumentChunkingTests
{
    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var text = "Short text.";

        var chunks = DocumentIngestor.ChunkText(text, 512, 128);

        chunks.Should().HaveCount(1);
        chunks[0].Should().Be(text);
    }

    [Fact]
    public void ChunkText_EmptyText_ReturnsEmpty()
    {
        var chunks = DocumentIngestor.ChunkText("", 512, 128);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_NullText_ReturnsEmpty()
    {
        var chunks = DocumentIngestor.ChunkText(null!, 512, 128);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_SplitsAtSentenceBoundary()
    {
        // Build a text with multiple sentences where a period falls in the second half
        var sentence1 = new string('A', 300) + ". ";
        var sentence2 = new string('B', 300) + ". ";
        var text = sentence1 + sentence2;

        var chunks = DocumentIngestor.ChunkText(text, 512, 128);

        // The first chunk should end at a sentence boundary (including the period)
        chunks.Should().HaveCountGreaterThan(1);
        chunks[0].Should().EndWith(".");
    }

    [Fact]
    public void ChunkText_ProtectsDecimalNumbers()
    {
        // "3.14" should not be treated as a sentence boundary
        var text = "The value is 3.14 which is important. " + new string('X', 500);

        var chunks = DocumentIngestor.ChunkText(text, 512, 128);

        // The first chunk should not split at "3." — it should split at "important."
        chunks[0].Should().Contain("3.14");
    }

    [Fact]
    public void ChunkText_FallsBackToWhitespace()
    {
        // No sentence-ending punctuation, but has whitespace
        var text = new string('A', 300) + " " + new string('B', 300);

        var chunks = DocumentIngestor.ChunkText(text, 512, 128);

        chunks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ChunkText_MaxChunkSizeRespected()
    {
        var text = string.Join(". ", Enumerable.Range(0, 50).Select(i => new string('A', 50)));

        var chunks = DocumentIngestor.ChunkText(text, 512, 128);

        foreach (var chunk in chunks)
        {
            chunk.Length.Should().BeLessThanOrEqualTo(512);
        }
    }

    [Fact]
    public void ChunkText_CoversEntireText()
    {
        var text = string.Join(". ", Enumerable.Range(0, 20).Select(i => $"Sentence number {i}"));

        var chunks = DocumentIngestor.ChunkText(text, 100, 20);

        // All original text should be represented (accounting for overlap)
        var combined = string.Join("", chunks);
        // Each character from the original should appear at least once
        foreach (var ch in text)
        {
            combined.Should().Contain(ch.ToString());
        }
    }

    [Fact]
    public void FindLastSentenceBoundary_PeriodInSecondHalf_FindsIt()
    {
        var text = new string('A', 300) + ". " + new string('B', 50);

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        boundary.Should().Be(301); // Right after the period
    }

    [Fact]
    public void FindLastSentenceBoundary_NewlineInSecondHalf_FindsIt()
    {
        var text = new string('A', 300) + "\n" + new string('B', 50);

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        boundary.Should().Be(301); // Right after the newline
    }

    [Fact]
    public void FindLastSentenceBoundary_DecimalProtected()
    {
        // "3.1" should not be treated as boundary
        var text = new string('A', 250) + " value is 3.1 and more " + new string('B', 50);

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Should fall back to whitespace, not split at "3."
        boundary.Should().NotBe(text.IndexOf("3.", StringComparison.Ordinal) + 2);
    }
}
