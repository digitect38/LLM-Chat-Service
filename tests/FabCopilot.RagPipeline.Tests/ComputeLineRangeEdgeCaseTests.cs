using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class ComputeLineRangeEdgeCaseTests
{
    [Fact]
    public void EmptyString_Returns_1_1()
    {
        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange("", 0, 0);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(1);
    }

    [Fact]
    public void NoNewlines_Returns_1_1()
    {
        var text = "Hello World no newlines here";

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, 5, 10);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(1);
    }

    [Fact]
    public void CharStartAtFirstCharOfLine3_LineStartIs3()
    {
        var text = "Line1\nLine2\nLine3 content here";
        var charStart = text.IndexOf("Line3", StringComparison.Ordinal);
        var charEnd = charStart + 5;

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, charStart, charEnd);

        lineStart.Should().Be(3);
    }

    [Fact]
    public void CharEndAtLastCharBeforeNewline_CorrectLineEnd()
    {
        var text = "Line1\nLine2\nLine3";
        // charEnd at end of "Line2" (just before the second \n)
        var charStart = 0;
        var charEnd = text.IndexOf("\nLine3", StringComparison.Ordinal);

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, charStart, charEnd);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(2);
    }

    [Fact]
    public void ZeroLengthRange_SameLine()
    {
        var text = "Line1\nLine2\nLine3";
        var pos = text.IndexOf("Line2", StringComparison.Ordinal);

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, pos, pos);

        lineStart.Should().Be(lineEnd);
    }

    [Fact]
    public void CharEndExceedsTextLength_ClampedSafely()
    {
        var text = "Line1\nLine2\nLine3";

        // Should not throw, charEnd is clamped by Math.Min
        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, 0, text.Length + 100);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(3); // 3 lines total
    }

    [Fact]
    public void FullRange_CoversAllLines()
    {
        var text = "Line1\nLine2\nLine3\nLine4";

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, 0, text.Length);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(4);
    }

    [Fact]
    public void WindowsLineEndings_CountsCorrectly()
    {
        // \r\n — only \n is counted as line separator by ComputeLineRange
        var text = "Line1\r\nLine2\r\nLine3";
        var charStart = 0;
        var charEnd = text.Length;

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, charStart, charEnd);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(3);
    }

    [Fact]
    public void TrailingNewline_CountsCorrectly()
    {
        var text = "Line1\nLine2\n";
        var charStart = 0;
        var charEnd = text.Length;

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, charStart, charEnd);

        lineStart.Should().Be(1);
        // Trailing newline means we see 2 newlines, lineEnd incremented to 3
        lineEnd.Should().Be(3);
    }

    [Fact]
    public void ConsecutiveNewlines_EmptyLinesIncluded()
    {
        var text = "Line1\n\n\nLine4";
        var charStart = 0;
        var charEnd = text.Length;

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, charStart, charEnd);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(4); // 3 newlines = 4 lines
    }

    [Fact]
    public void CharStartRightAfterNewline_NextLineNumber()
    {
        var text = "Line1\nLine2\nLine3";
        // Position right after second \n (start of Line3)
        var newlinePos = text.IndexOf("Line3", StringComparison.Ordinal);

        var (lineStart, _) = DocumentIngestor.ComputeLineRange(text, newlinePos, newlinePos + 5);

        lineStart.Should().Be(3);
    }

    [Fact]
    public void LargeText_1000Lines_AccurateLineRange()
    {
        var lines = Enumerable.Range(1, 1000).Select(i => $"Line number {i}").ToList();
        var text = string.Join("\n", lines);

        // Target line 500
        var offset = 0;
        for (var i = 0; i < 499; i++)
            offset += lines[i].Length + 1; // +1 for \n

        var charStart = offset;
        var charEnd = offset + lines[499].Length;

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, charStart, charEnd);

        lineStart.Should().Be(500);
        lineEnd.Should().Be(500);
    }
}
