using System.Net.Http.Headers;
using System.Text.Json;
using FabCopilot.RagService.Interfaces;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services.ImageOcr;

/// <summary>
/// HTTP-based OCR extractor that delegates to an external OCR service.
/// Compatible with Tesseract HTTP Server, EasyOCR Server, or any service
/// that accepts image files and returns extracted text.
///
/// Expected API contract:
///   POST /ocr  (multipart/form-data with "file" field)
///   Response: { "text": "extracted text..." }
/// </summary>
public sealed class HttpImageOcrExtractor : IImageOcrExtractor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpImageOcrExtractor> _logger;

    public HttpImageOcrExtractor(IHttpClientFactory httpClientFactory, ILogger<HttpImageOcrExtractor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default)
    {
        if (!File.Exists(imagePath))
        {
            _logger.LogWarning("Image file not found: {ImagePath}", imagePath);
            return "";
        }

        var imageData = await File.ReadAllBytesAsync(imagePath, ct);
        var fileName = Path.GetFileName(imagePath);
        return await ExtractTextAsync(imageData, fileName, ct);
    }

    public async Task<string> ExtractTextAsync(byte[] imageData, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("OCR");
            using var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageData);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(fileName));
            content.Add(imageContent, "file", fileName);

            var response = await client.PostAsync("/ocr", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OCR request failed: {Status}", response.StatusCode);
                return "";
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var textProp)
                ? textProp.GetString() ?? ""
                : "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "OCR service unreachable, skipping OCR for {FileName}", fileName);
            return "";
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("OCR");
            client.Timeout = TimeSpan.FromSeconds(3);
            var resp = await client.GetAsync("/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream"
        };
    }
}
