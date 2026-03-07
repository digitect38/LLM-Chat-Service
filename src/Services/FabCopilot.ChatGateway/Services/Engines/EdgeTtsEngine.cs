using FabCopilot.ChatGateway.Configuration;

namespace FabCopilot.ChatGateway.Services.Engines;

/// <summary>
/// Edge TTS engine via openai-edge-tts Docker container (travisvn/openai-edge-tts).
/// Uses OpenAI-compatible /v1/audio/speech endpoint with direct Edge voice name pass-through.
/// Container: docker run -d -p 5050:5050 travisvn/openai-edge-tts:latest
/// </summary>
public class EdgeTtsEngine : ITtsEngine
{
    public string Name => "EdgeTts";

    private readonly ILogger<EdgeTtsEngine> _logger;

    public EdgeTtsEngine(ILogger<EdgeTtsEngine> logger)
    {
        _logger = logger;
    }

    public async Task<TtsResult> SynthesizeAsync(string text, string voice, TtsOptions options, CancellationToken ct = default)
    {
        var baseUrl = options.EdgeTts.BaseUrl;
        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            };

            var payload = new
            {
                model = "tts-1",
                input = text,
                voice = string.IsNullOrEmpty(voice) ? "ko-KR-SunHiNeural" : voice,
                speed = options.Speed
            };

            var response = await client.PostAsJsonAsync("/v1/audio/speech", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return TtsResult.Fail($"Edge TTS failed ({(int)response.StatusCode}): {err}");
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";

            _logger.LogInformation("Edge TTS synthesized {Bytes} bytes for voice {Voice}", audioBytes.Length, voice);
            return new TtsResult(audioBytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Edge TTS server unreachable at {Url}", baseUrl);
            return TtsResult.Fail("Edge TTS 서버에 연결할 수 없습니다. openai-edge-tts 컨테이너를 시작하세요.");
        }
    }
}
