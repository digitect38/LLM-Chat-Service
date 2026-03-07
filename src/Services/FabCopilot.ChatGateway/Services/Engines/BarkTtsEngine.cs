using FabCopilot.ChatGateway.Configuration;
using Microsoft.Extensions.Options;

namespace FabCopilot.ChatGateway.Services.Engines;

public class BarkTtsEngine : ITtsEngine
{
    public string Name => "Bark";

    private readonly IOptionsMonitor<TtsOptions> _options;
    private readonly ILogger<BarkTtsEngine> _logger;

    public BarkTtsEngine(IOptionsMonitor<TtsOptions> options, ILogger<BarkTtsEngine> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<TtsResult> SynthesizeAsync(string text, string voice, TtsOptions options, CancellationToken ct = default)
    {
        var barkOpts = options.Bark;
        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(barkOpts.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
            var speaker = string.IsNullOrEmpty(voice) ? barkOpts.Speaker : voice;

            // Bark with OpenAI-compat endpoint
            var payload = new
            {
                model = "bark",
                input = text,
                voice = speaker,
                speed = options.Speed
            };

            var response = await client.PostAsJsonAsync("/v1/audio/speech", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return TtsResult.Fail($"Bark synthesis failed ({(int)response.StatusCode}): {err}");
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            _logger.LogInformation("Bark synthesized {Bytes} bytes with speaker {Speaker}", audioBytes.Length, speaker);
            return new TtsResult(audioBytes, "audio/wav");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Bark server unreachable at {Url}", barkOpts.BaseUrl);
            return TtsResult.Fail("Bark server unreachable");
        }
    }
}
