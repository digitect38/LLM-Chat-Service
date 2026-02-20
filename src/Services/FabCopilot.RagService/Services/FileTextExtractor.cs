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
