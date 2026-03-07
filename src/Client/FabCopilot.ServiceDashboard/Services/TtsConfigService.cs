using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabCopilot.ServiceDashboard.Services;

/// <summary>
/// Reads and writes TTS configuration in ChatGateway's appsettings.json.
/// Mirrors EmbeddingConfigService TTS methods from WebClient.
/// </summary>
public class TtsConfigService
{
    private readonly string _gatewayConfigPath;

    public TtsConfigService(IWebHostEnvironment env)
    {
        // Dashboard is at src/Client/FabCopilot.ServiceDashboard
        var srcRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", ".."));
        _gatewayConfigPath = Path.Combine(srcRoot, "src", "Services", "FabCopilot.ChatGateway", "appsettings.json");
    }

    /// <summary>For unit testing</summary>
    internal TtsConfigService(string gatewayConfigPath)
    {
        _gatewayConfigPath = gatewayConfigPath;
    }

    public static readonly Dictionary<string, string[]> VoiceMap = new()
    {
        ["Kokoro"] = ["af_heart", "af_sky", "af_bella", "am_adam", "am_michael", "bf_emma", "bm_george", "bf_isabella"],
        ["CosyVoice"] = ["korean_female", "korean_male", "chinese_female", "chinese_male", "english_female", "english_male"],
        ["Orpheus"] = ["tara", "leah", "jess", "leo", "dan", "mia", "zac", "zoe"],
        ["EdgeTts"] = ["ko-KR-SunHiNeural", "ko-KR-InJoonNeural", "en-US-JennyNeural", "en-US-GuyNeural", "ja-JP-NanamiNeural"],
        ["Piper"] = ["ko_KR-kss-low", "en_US-lessac-medium", "en_US-amy-medium", "en_GB-alba-medium"],
        ["FishSpeech"] = ["default"],
        ["Chatterbox"] = ["default"],
        ["Xtts"] = ["Claribel Dervla"],
        ["Bark"] = ["v2/ko_speaker_0", "v2/ko_speaker_1", "v2/ko_speaker_2", "v2/ko_speaker_3",
                     "v2/en_speaker_0", "v2/en_speaker_1", "v2/en_speaker_2", "v2/en_speaker_3"],
        ["Browser"] = ["(OS default)"],
    };

    public static readonly Dictionary<string, string> ProviderLabels = new()
    {
        ["Kokoro"] = "Kokoro 82M (Primary, <0.3s)",
        ["CosyVoice"] = "CosyVoice 3 (Premium, 150ms)",
        ["Orpheus"] = "Orpheus 400M (Emotions)",
        ["EdgeTts"] = "Edge TTS (Cloud, <1s)",
        ["Piper"] = "Piper (CPU, <0.3s)",
        ["FishSpeech"] = "FishSpeech 1.5 (GPU, ~2s)",
        ["Chatterbox"] = "Chatterbox (GPU, EN only)",
        ["Xtts"] = "XTTS 2.0 (Legacy, ~5s)",
        ["Bark"] = "Bark (GPU, ~5s)",
        ["Browser"] = "Browser SpeechSynthesis",
    };

    public (string Provider, string Voice, float Speed) GetCurrentConfig()
    {
        if (!File.Exists(_gatewayConfigPath))
            return ("EdgeTts", "ko-KR-SunHiNeural", 1.0f);

        var json = File.ReadAllText(_gatewayConfigPath);
        var node = JsonNode.Parse(json);
        var tts = node?["Tts"];
        if (tts is null)
            return ("EdgeTts", "ko-KR-SunHiNeural", 1.0f);

        var provider = tts["Provider"]?.GetValue<string>() ?? "EdgeTts";
        var voice = tts["Voice"]?.GetValue<string>() ?? "ko-KR-SunHiNeural";
        var speed = tts["Speed"]?.GetValue<float>() ?? 1.0f;
        return (provider, voice, speed);
    }

    public bool SetConfig(string provider, string voice, float speed = 1.0f)
    {
        if (!File.Exists(_gatewayConfigPath)) return false;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(_gatewayConfigPath);
        var node = JsonNode.Parse(json);
        if (node is null) return false;

        if (node["Tts"] is not JsonObject ttsSection)
        {
            ttsSection = new JsonObject();
            node["Tts"] = ttsSection;
        }

        ttsSection["Provider"] = provider;
        ttsSection["Voice"] = voice;
        ttsSection["Speed"] = speed;

        File.WriteAllText(_gatewayConfigPath, node.ToJsonString(options));
        return true;
    }
}
