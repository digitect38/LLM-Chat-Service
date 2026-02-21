using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FabCopilot.RagService.Services;

public sealed class FileTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".pdf"
    };

    public static bool IsSupported(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public static bool IsPdf(string filePath)
        => Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public string ExtractText(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".md" or ".txt" => File.ReadAllText(filePath, Encoding.UTF8),
            ".pdf" => ExtractPdfText(filePath),
            _ => throw new NotSupportedException($"Unsupported file extension: {ext}")
        };
    }

    /// <summary>
    /// Extracts text from a PDF file page by page.
    /// Returns a list of (pageNumber, pageText) tuples (1-based page numbers).
    /// </summary>
    public List<(int PageNumber, string Text)> ExtractPdfPages(string filePath)
    {
        var pages = new List<(int, string)>();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var text = page.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                pages.Add((page.Number, text));
            }
        }

        return pages;
    }

    /// <summary>
    /// Extracts text from a PDF page-by-page and appends any detected tables as Markdown.
    /// Tables are detected by clustering words by Y-coordinate (rows) and X-coordinate (columns).
    /// </summary>
    public List<(int PageNumber, string Text)> ExtractPdfPagesWithTables(string filePath)
    {
        var pages = new List<(int, string)>();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var text = page.Text?.Trim() ?? string.Empty;
            var tableMarkdown = ExtractTablesFromPage(page);

            if (!string.IsNullOrEmpty(tableMarkdown))
            {
                text = text + "\n\n" + tableMarkdown;
            }

            if (!string.IsNullOrEmpty(text.Trim()))
            {
                pages.Add((page.Number, text.Trim()));
            }
        }

        return pages;
    }

    /// <summary>
    /// Detects tables on a PDF page by clustering words into rows and columns.
    /// Returns Markdown-formatted tables, or empty string if no table detected.
    /// </summary>
    internal static string ExtractTablesFromPage(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count < 4) return string.Empty;

        // Cluster words by Y-coordinate (rows) — tolerance for slight misalignment
        const double yTolerance = 3.0;
        var rows = new List<List<Word>>();

        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            var placed = false;
            foreach (var row in rows)
            {
                if (Math.Abs(row[0].BoundingBox.Bottom - word.BoundingBox.Bottom) < yTolerance)
                {
                    row.Add(word);
                    placed = true;
                    break;
                }
            }
            if (!placed)
            {
                rows.Add([word]);
            }
        }

        // Sort words within each row by X-coordinate
        foreach (var row in rows)
        {
            row.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        // Detect column boundaries: require at least 3 rows with consistent column count
        if (rows.Count < 3) return string.Empty;

        // Check if rows have a consistent number of "columns" (word groups separated by large gaps)
        const double colGapThreshold = 20.0;
        var columnCounts = rows.Select(r => CountColumns(r, colGapThreshold)).ToList();
        var mostCommonColCount = columnCounts
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .First();

        if (mostCommonColCount.Key < 2 || mostCommonColCount.Count() < 3)
            return string.Empty;

        var targetCols = mostCommonColCount.Key;

        // Build markdown table from rows that match the target column count
        var tableRows = new List<List<string>>();
        foreach (var row in rows)
        {
            var cells = SplitRowIntoCells(row, targetCols, colGapThreshold);
            if (cells.Count == targetCols)
                tableRows.Add(cells);
        }

        if (tableRows.Count < 2) return string.Empty;

        var sb = new StringBuilder();
        // Header row
        sb.AppendLine("| " + string.Join(" | ", tableRows[0]) + " |");
        sb.AppendLine("| " + string.Join(" | ", tableRows[0].Select(_ => "---")) + " |");
        // Data rows
        for (var i = 1; i < tableRows.Count; i++)
        {
            sb.AppendLine("| " + string.Join(" | ", tableRows[i]) + " |");
        }

        return sb.ToString().TrimEnd();
    }

    private static int CountColumns(List<Word> row, double gapThreshold)
    {
        if (row.Count <= 1) return row.Count;
        var cols = 1;
        for (var i = 1; i < row.Count; i++)
        {
            if (row[i].BoundingBox.Left - row[i - 1].BoundingBox.Right > gapThreshold)
                cols++;
        }
        return cols;
    }

    private static List<string> SplitRowIntoCells(List<Word> row, int targetCols, double gapThreshold)
    {
        if (row.Count == 0) return [];

        var cells = new List<string>();
        var currentCell = new StringBuilder(row[0].Text);

        for (var i = 1; i < row.Count; i++)
        {
            var gap = row[i].BoundingBox.Left - row[i - 1].BoundingBox.Right;
            if (gap > gapThreshold && cells.Count < targetCols - 1)
            {
                cells.Add(currentCell.ToString().Trim());
                currentCell.Clear();
            }
            else
            {
                currentCell.Append(' ');
            }
            currentCell.Append(row[i].Text);
        }
        cells.Add(currentCell.ToString().Trim());

        return cells;
    }

    private static string ExtractPdfText(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }
}
