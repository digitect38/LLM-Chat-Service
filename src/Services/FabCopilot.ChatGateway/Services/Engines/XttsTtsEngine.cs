using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FabCopilot.ChatGateway.Configuration;

namespace FabCopilot.ChatGateway.Services.Engines;

public class XttsTtsEngine : ITtsEngine
{
    public string Name => "Xtts";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<XttsTtsEngine> _logger;
    private readonly ConcurrentDictionary<string, (JsonArray Embedding, JsonArray GptCondLatent)> _speakerCache = new();

    public XttsTtsEngine(IHttpClientFactory httpFactory, ILogger<XttsTtsEngine> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<TtsResult> SynthesizeAsync(string text, string voice, TtsOptions options, CancellationToken ct = default)
    {
        var xttsOpts = options.Xtts;
        try
        {
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(xttsOpts.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };

            // Truncate for XTTS Korean char limit
            var voiceText = TruncateForXtts(text, xttsOpts.MaxChars);
            var speakerName = string.IsNullOrEmpty(voice) ? "Claribel Dervla" : voice;

            // Fetch speaker embedding (cached)
            var (embedding, gptCondLatent) = await GetSpeakerVoiceAsync(client, speakerName, ct);
            if (embedding is null || gptCondLatent is null)
                return TtsResult.Fail("XTTS speaker voice data unavailable");

            var payload = new
            {
                text = voiceText,
                language = "ko",
                speaker_embedding = embedding,
                gpt_cond_latent = gptCondLatent
            };

            var response = await client.PostAsJsonAsync("/tts", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return TtsResult.Fail($"XTTS synthesis failed ({(int)response.StatusCode}): {err}");
            }

            // XTTS returns base64-encoded WAV in a JSON string
            var contentType = response.Content.Headers.ContentType?.MediaType;
            byte[] audioBytes;
            if (contentType is "application/json" or "text/plain")
            {
                var raw = await response.Content.ReadAsStringAsync(ct);
                if (raw.StartsWith('"') && raw.EndsWith('"'))
                    raw = raw[1..^1];
                audioBytes = Convert.FromBase64String(raw);
            }
            else
            {
                audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            }

            _logger.LogInformation("XTTS synthesized {Bytes} bytes for speaker {Speaker}", audioBytes.Length, speakerName);
            return new TtsResult(audioBytes, "audio/wav");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "XTTS server unreachable at {Url}", xttsOpts.BaseUrl);
            return TtsResult.Fail("XTTS server unreachable");
        }
    }

    private async Task<(JsonArray? Embedding, JsonArray? GptCondLatent)> GetSpeakerVoiceAsync(
        HttpClient client, string speakerName, CancellationToken ct)
    {
        if (_speakerCache.TryGetValue(speakerName, out var cached))
            return (cached.Embedding, cached.GptCondLatent);

        var resp = await client.GetAsync("/studio_speakers", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var studioSpeakers = JsonNode.Parse(json);

        var speaker = studioSpeakers?[speakerName];
        if (speaker is null)
        {
            var first = studioSpeakers?.AsObject().FirstOrDefault();
            if (first is { Value: not null })
            {
                speaker = first.Value.Value;
                _logger.LogInformation("XTTS speaker '{Requested}' not found, using '{Fallback}'", speakerName, first.Value.Key);
            }
        }

        var embedding = speaker?["speaker_embedding"]?.AsArray();
        var gptCondLatent = speaker?["gpt_cond_latent"]?.AsArray();

        if (embedding is not null && gptCondLatent is not null)
            _speakerCache.TryAdd(speakerName, (embedding, gptCondLatent));

        return (embedding, gptCondLatent);
    }

    private static string TruncateForXtts(string text, int maxChars)
    {
        // Strip citation markers
        var voiceText = System.Text.RegularExpressions.Regex.Replace(
            text, @"\[MNL-[^\]]*\]|\[cite-\d+\]|\[출처[^\]]*\]|\*\*|##|#+\s", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        if (voiceText.Length <= maxChars)
            return voiceText;

        var cut = voiceText.LastIndexOf('.', maxChars - 1);
        if (cut < 30) cut = voiceText.LastIndexOf(' ', maxChars - 1);
        if (cut < 30) cut = maxChars;
        return voiceText[..(cut + 1)];
    }
}
