using FabCopilot.ChatGateway.Configuration;
using Microsoft.Extensions.Options;

namespace FabCopilot.ChatGateway.Services.Engines;

public class FishSpeechTtsEngine : ITtsEngine
{
    public string Name => "FishSpeech";

    private readonly IOptionsMonitor<TtsOptions> _options;
    private readonly ILogger<FishSpeechTtsEngine> _logger;

    public FishSpeechTtsEngine(IOptionsMonitor<TtsOptions> options, ILogger<FishSpeechTtsEngine> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<TtsResult> SynthesizeAsync(string text, string voice, TtsOptions options, CancellationToken ct = default)
    {
        var fishOpts = options.FishSpeech;
        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(fishOpts.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
            var payload = new
            {
                text,
                reference_id = string.IsNullOrEmpty(voice) ? fishOpts.ReferenceId : voice,
                format = "wav"
            };

            var response = await client.PostAsJsonAsync("/v1/tts", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return TtsResult.Fail($"FishSpeech synthesis failed ({(int)response.StatusCode}): {err}");
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            _logger.LogInformation("FishSpeech synthesized {Bytes} bytes", audioBytes.Length);
            return new TtsResult(audioBytes, "audio/wav");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "FishSpeech server unreachable at {Url}", fishOpts.BaseUrl);
            return TtsResult.Fail("FishSpeech server unreachable");
        }
    }
}
