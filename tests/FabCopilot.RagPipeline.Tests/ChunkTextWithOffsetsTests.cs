using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class ChunkTextWithOffsetsTests
{
    [Fact]
    public void EmptyText_ReturnsEmptyList()
    {
        var result = DocumentIngestor.ChunkTextWithOffsets("", 512, 128);

        result.Should().BeEmpty();
    }

    [Fact]
    public void NullText_ReturnsEmptyList()
    {
        var result = DocumentIngestor.ChunkTextWithOffsets(null!, 512, 128);

        result.Should().BeEmpty();
    }

    [Fact]
    public void TextShorterThanMaxChunk_SingleChunkWithCorrectOffsets()
    {
        var text = "Short text here.";

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCount(1);
        result[0].Text.Should().Be(text);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(text.Length);
    }

    [Fact]
    public void TextExactlyAtMaxChunk_SingleChunk()
    {
        var text = new string('A', 512);

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCount(1);
        result[0].Text.Should().Be(text);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(512);
    }

    [Fact]
    public void TwoChunks_SecondChunkStartOverlapsFirstChunkEnd()
    {
        // Build text with sentence boundaries to ensure predictable splitting
        var sentence1 = new string('A', 300) + ". ";
        var sentence2 = new string('B', 300) + ". ";
        var text = sentence1 + sentence2;

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCountGreaterThan(1);
        // Second chunk should start before first chunk ends (overlap region)
        result[1].Start.Should().BeLessThan(result[0].End);
    }

    [Fact]
    public void OverlapRegion_ContainsDuplicateTextInBothChunks()
    {
        var sentence1 = new string('A', 300) + ". ";
        var sentence2 = new string('B', 300) + ". ";
        var text = sentence1 + sentence2;

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCountGreaterThan(1);
        var overlapStart = result[1].Start;
        var overlapEnd = result[0].End;
        if (overlapStart < overlapEnd)
        {
            var overlapText = text.Substring(overlapStart, overlapEnd - overlapStart);
            result[0].Text.Should().Contain(overlapText);
            result[1].Text.Should().Contain(overlapText);
        }
    }

    [Fact]
    public void SentenceBoundary_RespectedInOffsetCalculation()
    {
        var sentence1 = new string('A', 300) + ". ";
        var sentence2 = new string('B', 300) + ". ";
        var text = sentence1 + sentence2;

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCountGreaterThan(1);
        // First chunk should end at a sentence boundary (after the period)
        result[0].Text.Should().EndWith(".");
    }

    [Fact]
    public void SingleCharacterText_SingleChunk()
    {
        var result = DocumentIngestor.ChunkTextWithOffsets("X", 512, 128);

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("X");
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(1);
    }

    [Fact]
    public void UnicodeText_OffsetsAreCharBased()
    {
        // Korean text: offsets should be char-based, not byte-based
        var text = "안녕하세요. " + new string('가', 300) + ". " + new string('나', 300);

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCountGreaterOrEqualTo(1);
        foreach (var chunk in result)
        {
            chunk.Text.Should().Be(text.Substring(chunk.Start, chunk.End - chunk.Start));
        }
    }

    [Fact]
    public void VeryLongText_MultipleChunks_AllOffsetsContiguous()
    {
        var text = string.Join(". ", Enumerable.Range(0, 100).Select(i => new string('X', 50)));

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCountGreaterThan(3);
        // All chunk texts should match their offset positions
        foreach (var chunk in result)
        {
            chunk.Text.Should().Be(text.Substring(chunk.Start, chunk.End - chunk.Start));
        }
    }

    [Fact]
    public void NewlineOnlyText_ChunksPreserveNewlines()
    {
        var text = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Line {i}"));

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        result.Should().HaveCountGreaterThan(1);
        // Each chunk text should match the extracted substring
        foreach (var chunk in result)
        {
            var expected = text.Substring(chunk.Start, chunk.End - chunk.Start);
            chunk.Text.Should().Be(expected);
        }
    }

    [Fact]
    public void OffsetEnd_NeverExceedsOriginalTextLength()
    {
        var text = string.Join(". ", Enumerable.Range(0, 50).Select(i => new string('Y', 30)));

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        foreach (var chunk in result)
        {
            chunk.End.Should().BeLessThanOrEqualTo(text.Length);
            chunk.Start.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void AllChunks_ReconstructOriginalText()
    {
        var text = string.Join(". ", Enumerable.Range(0, 30).Select(i => $"Sentence number {i}"));

        var result = DocumentIngestor.ChunkTextWithOffsets(text, 512, 128);

        // Every character in the original text should be covered by at least one chunk
        var covered = new bool[text.Length];
        foreach (var chunk in result)
        {
            for (var i = chunk.Start; i < chunk.End; i++)
                covered[i] = true;
        }

        covered.Should().AllBeEquivalentTo(true);
    }
}
