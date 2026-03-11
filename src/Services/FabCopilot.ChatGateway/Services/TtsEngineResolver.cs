using FabCopilot.ChatGateway.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace FabCopilot.ChatGateway.Services;

public class TtsEngineResolver : IDisposable
{
    // v4.84 fallback chain: Kokoro → EdgeTts → Piper → CosyVoice → first available
    private static readonly string[] FallbackChain = ["Kokoro", "EdgeTts", "Piper", "CosyVoice"];

    private volatile string _currentProvider;
    private readonly Dictionary<string, ITtsEngine> _engines;
    private readonly IDisposable? _changeToken;
    private readonly ILogger<TtsEngineResolver> _logger;

    public TtsEngineResolver(IOptionsMonitor<TtsOptions> options, IEnumerable<ITtsEngine> engines, ILogger<TtsEngineResolver> logger)
    {
        _engines = engines.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        _currentProvider = options.CurrentValue.Provider;
        _changeToken = options.OnChange(o => _currentProvider = o.Provider);
        _logger = logger;
    }

    public ITtsEngine Resolve() =>
        _engines.TryGetValue(_currentProvider, out var engine)
            ? engine
            : _engines.GetValueOrDefault("Kokoro")
              ?? _engines.GetValueOrDefault("EdgeTts")
              ?? _engines.Values.First();

    // Default voice per engine — used during fallback when the configured voice doesn't match
    private static readonly Dictionary<string, string> DefaultVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Kokoro"] = "af_heart",
        ["EdgeTts"] = "ko-KR-SunHiNeural",
        ["Piper"] = "alloy",
        ["CosyVoice"] = "korean_female",
        ["Orpheus"] = "tara",
        ["FishSpeech"] = "default",
        ["Chatterbox"] = "default",
        ["Bark"] = "v2/ko_speaker_0",
        ["Xtts"] = "Claribel Dervla",
    };

    /// <summary>
    /// Synthesize with automatic fallback chain.
    /// If the primary engine fails, tries the next engine in the fallback chain.
    /// Each fallback engine uses its own default voice to avoid voice mismatch errors.
    /// </summary>
    /// <summary>Result of a TTS synthesis attempt with fallback chain tracking.</summary>
    public record SynthesizeResult(TtsResult Result, string EngineName, string VoiceUsed, string? FallbackFrom, List<string> Chain);

    public async Task<SynthesizeResult> SynthesizeWithFallbackAsync(
        string text, string voice, TtsOptions options, CancellationToken ct = default)
    {
        var chain = new List<string>();
        var primary = Resolve();
        var result = await primary.SynthesizeAsync(text, voice, options, ct);
        if (result.IsSuccess)
        {
            chain.Add($"{primary.Name}:ok");
            return new(result, primary.Name, voice, null, chain);
        }

        var shortError = ExtractShortError(result.Error);
        chain.Add($"{primary.Name}:fail({shortError})");
        _logger.LogWarning("[TTS FALLBACK] Primary engine '{Engine}' failed (voice={Voice}): {Error}",
            primary.Name, voice, result.Error);

        foreach (var fallbackName in FallbackChain)
        {
            if (fallbackName.Equals(primary.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_engines.TryGetValue(fallbackName, out var fallbackEngine))
                continue;

            // Use engine-specific default voice during fallback
            var fallbackVoice = DefaultVoices.GetValueOrDefault(fallbackName, "default");
            _logger.LogWarning("[TTS FALLBACK] Trying fallback engine '{FallbackEngine}' (voice={FallbackVoice}), original was '{Primary}'",
                fallbackName, fallbackVoice, primary.Name);
            result = await fallbackEngine.SynthesizeAsync(text, fallbackVoice, options, ct);
            if (result.IsSuccess)
            {
                chain.Add($"{fallbackName}:ok");
                var chainStr = string.Join(" > ", chain);
                _logger.LogWarning("[TTS FALLBACK] Succeeded with fallback engine '{FallbackEngine}' (voice={FallbackVoice}). Chain: {Chain}",
                    fallbackName, fallbackVoice, chainStr);
                return new(result, fallbackName, fallbackVoice, primary.Name, chain);
            }

            shortError = ExtractShortError(result.Error);
            chain.Add($"{fallbackName}:fail({shortError})");
            _logger.LogWarning("[TTS FALLBACK] Fallback engine '{FallbackEngine}' also failed: {Error}", fallbackName, result.Error);
        }

        var finalChain = string.Join(" > ", chain);
        _logger.LogError("[TTS FALLBACK] ALL engines failed! Chain: {Chain}", finalChain);
        return new(TtsResult.Fail("All TTS engines failed"), primary.Name, voice, null, chain);
    }

    /// <summary>Extract a short error description from the full error message.</summary>
    private static string ExtractShortError(string? error)
    {
        if (string.IsNullOrEmpty(error)) return "unknown";
        // Extract HTTP status code if present, e.g. "Piper synthesis failed (400): ..."
        var match = System.Text.RegularExpressions.Regex.Match(error, @"\((\d{3})\)");
        if (match.Success) return match.Groups[1].Value;
        if (error.Contains("unreachable", StringComparison.OrdinalIgnoreCase)) return "unreachable";
        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return "timeout";
        return "error";
    }

    public string CurrentProvider => _currentProvider;

    public IReadOnlyCollection<string> AvailableEngines => _engines.Keys;

    public void Dispose() => _changeToken?.Dispose();
}
