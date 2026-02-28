namespace FabCopilot.RagService.Interfaces;

/// <summary>
/// Abstraction for extracting text from image files via OCR.
/// Implementations may use Tesseract, EasyOCR, or HTTP-based OCR services.
/// </summary>
public interface IImageOcrExtractor
{
    /// <summary>
    /// Extracts text from an image file.
    /// Returns the extracted text, or empty string if OCR is unavailable or fails.
    /// </summary>
    Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default);

    /// <summary>
    /// Extracts text from an image byte array.
    /// </summary>
    Task<string> ExtractTextAsync(byte[] imageData, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the OCR service is available and ready.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
