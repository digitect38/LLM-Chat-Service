using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class FindLastSentenceBoundaryEdgeCaseTests
{
    [Fact]
    public void NoBoundary_FallsBackToWhitespaceOrFullLength()
    {
        // No punctuation and no whitespace in second half
        var text = new string('A', 100);

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Falls back to full length (no whitespace either)
        boundary.Should().Be(text.Length);
    }

    [Fact]
    public void PeriodAtEnd_ReturnsBoundaryAfterPeriod()
    {
        var text = new string('A', 300) + "End of sentence.";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Should find the period and return position after it
        boundary.Should().Be(text.Length);
    }

    [Fact]
    public void ExclamationMarkBoundary()
    {
        var text = new string('A', 300) + "Watch out! More text";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        var exclamIdx = text.LastIndexOf('!');
        boundary.Should().Be(exclamIdx + 1);
    }

    [Fact]
    public void QuestionMarkBoundary()
    {
        var text = new string('A', 300) + "Is this right? More text";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        var questionIdx = text.LastIndexOf('?');
        boundary.Should().Be(questionIdx + 1);
    }

    [Fact]
    public void KoreanSentenceEnder_PeriodAfterKorean()
    {
        var text = new string('가', 300) + "이것은 테스트입니다. 추가 내용";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Should find the period after Korean text
        boundary.Should().BeGreaterThan(300);
        var dotIdx = text.IndexOf("다.", StringComparison.Ordinal);
        boundary.Should().Be(dotIdx + 2); // after the period
    }

    [Fact]
    public void DecimalNumber_NotTreatedAsBoundary()
    {
        // 3.14 should not be split at the period
        var text = new string('A', 300) + " value 3.14 rest";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        var decimalDotIdx = text.IndexOf("3.1", StringComparison.Ordinal) + 1;
        // Should NOT return the position after "3."
        boundary.Should().NotBe(decimalDotIdx + 1);
    }

    [Fact]
    public void MultipleBoundaries_ReturnsLastOneSearchedBackward()
    {
        // Two periods in the second half — search goes backward, finds the LAST one first
        var text = new string('A', 200) + "First sentence. Second sentence. Trailing";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Backward search finds "sentence." (the second one) first
        var lastDotIdx = text.LastIndexOf('.');
        boundary.Should().Be(lastDotIdx + 1);
    }

    [Fact]
    public void BoundaryExactlyAtMinPos()
    {
        // Place a period exactly at the 50% mark
        var halfLen = 100;
        var text = new string('A', halfLen) + "." + new string('B', halfLen - 1);

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // The period at halfLen should be found
        boundary.Should().Be(halfLen + 1);
    }

    [Fact]
    public void NewlineAsBoundary()
    {
        var text = new string('A', 300) + "\nMore text here";

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Newline at position 300, boundary is 301
        boundary.Should().Be(301);
    }

    [Fact]
    public void BoundaryBeforeMinPos_FallsBackToWhitespace()
    {
        // Period only in the first half (before 50% mark)
        var text = "Short. " + new string('A', 300);

        var boundary = DocumentIngestor.FindLastSentenceBoundary(text);

        // Period is before the 50% mark, so it won't be found
        // Should fall back to whitespace in the second half
        boundary.Should().BeGreaterThan(text.Length / 2);
    }
}
