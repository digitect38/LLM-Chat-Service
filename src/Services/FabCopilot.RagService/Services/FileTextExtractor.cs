using System.Text;
using UglyToad.PdfPig;

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
