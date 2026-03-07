using FabCopilot.ChatGateway.Configuration;
using Microsoft.Extensions.Options;

namespace FabCopilot.ChatGateway.Services.Engines;

/// <summary>
/// Shared TTS engine for OpenAI-compatible APIs (Kokoro, CosyVoice, Chatterbox, Piper, Orpheus).
/// Each instance is registered with a different Name and reads its settings from the matching TtsOptions section.
/// </summary>
public class OpenAiCompatTtsEngine : ITtsEngine
{
    public string Name { get; }

    private readonly IOptionsMonitor<TtsOptions> _options;
    private readonly ILogger _logger;

    public OpenAiCompatTtsEngine(string name, IOptionsMonitor<TtsOptions> options, ILoggerFactory loggerFactory)
    {
        Name = name;
        _options = options;
        _logger = loggerFactory.CreateLogger($"TtsEngine.{name}");
    }

    public async Task<TtsResult> SynthesizeAsync(string text, string voice, TtsOptions options, CancellationToken ct = default)
    {
        var settings = GetSettings(options);
        if (string.IsNullOrEmpty(settings.BaseUrl))
            return TtsResult.Fail($"{Name} base URL not configured");

        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(settings.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
            var payload = new
            {
                model = settings.Model,
                input = text,
                voice = string.IsNullOrEmpty(voice) ? "default" : voice,
                speed = options.Speed
            };

            var response = await client.PostAsJsonAsync("/v1/audio/speech", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return TtsResult.Fail($"{Name} synthesis failed ({(int)response.StatusCode}): {err}");
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/wav";
            _logger.LogInformation("{Engine} synthesized {Bytes} bytes", Name, audioBytes.Length);
            return new TtsResult(audioBytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "{Engine} server unreachable at {Url}", Name, settings.BaseUrl);
            return TtsResult.Fail($"{Name} server unreachable");
        }
    }

    private OpenAiCompatSettings GetSettings(TtsOptions options) => Name switch
    {
        "Kokoro" => options.Kokoro,
        "CosyVoice" => options.CosyVoice,
        "Chatterbox" => options.Chatterbox,
        "Piper" => options.Piper,
        "Orpheus" => options.Orpheus,
        _ => new OpenAiCompatSettings()
    };
}
