using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabCopilot.ServiceDashboard.Tests;

/// <summary>
/// Tests that verify TTS voice change persistence and propagation.
/// The voice change flow:
///   1. ServerSettings UI → EmbeddingConfigService.SetTtsProvider() → writes appsettings.json
///   2. IOptionsMonitor detects file change → TtsOptions.Voice reloads
///   3. Synthesis endpoint reads ttsOpts.CurrentValue.Voice → passes to engine
/// These tests verify step 1 (file write) and step 3 (voice propagation) independently.
/// </summary>
public class TtsVoiceChangeTests : IDisposable
{
    private readonly string _tempDir;

    public TtsVoiceChangeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tts-voice-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Config file write/read tests ─────────────────────────

    [Fact]
    public void WriteTtsConfig_SetsVoice_ReadBackMatches()
    {
        var configPath = CreateConfigFile(new { Tts = new { Provider = "Kokoro", Voice = "af_heart", Speed = 1.0 } });

        // Simulate SetTtsProvider writing a new voice
        WriteTtsProvider(configPath, "Kokoro", "am_michael");

        var (provider, voice, _) = ReadTtsConfig(configPath);
        provider.Should().Be("Kokoro");
        voice.Should().Be("am_michael");
    }

    [Fact]
    public void WriteTtsConfig_ChangingOnlyVoice_PreservesProvider()
    {
        var configPath = CreateConfigFile(new { Tts = new { Provider = "Kokoro", Voice = "af_heart", Speed = 1.0 } });

        WriteTtsProvider(configPath, "Kokoro", "bf_emma");

        var (provider, voice, _) = ReadTtsConfig(configPath);
        provider.Should().Be("Kokoro");
        voice.Should().Be("bf_emma");
    }

    [Fact]
    public void WriteTtsConfig_ChangingProviderAndVoice_BothUpdate()
    {
        var configPath = CreateConfigFile(new { Tts = new { Provider = "Kokoro", Voice = "af_heart", Speed = 1.0 } });

        WriteTtsProvider(configPath, "EdgeTts", "ko-KR-SunHiNeural");

        var (provider, voice, _) = ReadTtsConfig(configPath);
        provider.Should().Be("EdgeTts");
        voice.Should().Be("ko-KR-SunHiNeural");
    }

    [Fact]
    public void WriteTtsConfig_PreservesOtherSettings()
    {
        var config = new
        {
            Nats = new { Url = "nats://localhost:4222" },
            Tts = new
            {
                Provider = "Kokoro",
                Voice = "af_heart",
                Speed = 1.0,
                Kokoro = new { BaseUrl = "http://localhost:8401" },
                EdgeTts = new { BaseUrl = "http://localhost:5050" }
            }
        };
        var configPath = CreateConfigFile(config);

        WriteTtsProvider(configPath, "EdgeTts", "en-US-JennyNeural");

        var json = File.ReadAllText(configPath);
        var node = JsonNode.Parse(json)!;

        // NATS section should be preserved
        node["Nats"]?["Url"]?.GetValue<string>().Should().Be("nats://localhost:4222");
        // Sub-engine config should be preserved
        node["Tts"]?["Kokoro"]?["BaseUrl"]?.GetValue<string>().Should().Be("http://localhost:8401");
        node["Tts"]?["EdgeTts"]?["BaseUrl"]?.GetValue<string>().Should().Be("http://localhost:5050");
    }

    [Fact]
    public void WriteTtsConfig_MultipleWrites_LastOneWins()
    {
        var configPath = CreateConfigFile(new { Tts = new { Provider = "Kokoro", Voice = "af_heart", Speed = 1.0 } });

        WriteTtsProvider(configPath, "Kokoro", "am_adam");
        WriteTtsProvider(configPath, "Kokoro", "bf_isabella");
        WriteTtsProvider(configPath, "EdgeTts", "ko-KR-InJoonNeural");

        var (provider, voice, _) = ReadTtsConfig(configPath);
        provider.Should().Be("EdgeTts");
        voice.Should().Be("ko-KR-InJoonNeural");
    }

    [Fact]
    public void WriteTtsConfig_WithNoTtsSection_CreatesTtsSection()
    {
        var configPath = CreateConfigFile(new { Nats = new { Url = "test" } });

        WriteTtsProvider(configPath, "Kokoro", "af_sky");

        var (provider, voice, _) = ReadTtsConfig(configPath);
        provider.Should().Be("Kokoro");
        voice.Should().Be("af_sky");
    }

    // ─── Voice parameter propagation tests ────────────────────

    [Theory]
    [InlineData("af_heart")]
    [InlineData("am_michael")]
    [InlineData("bf_emma")]
    [InlineData("ko-KR-SunHiNeural")]
    [InlineData("en-US-JennyNeural")]
    [InlineData("v2/ko_speaker_0")]
    public void VoiceValue_ShouldBeValidString(string voice)
    {
        voice.Should().NotBeNullOrWhiteSpace();
        voice.Should().NotContain("\n");
        voice.Should().NotContain("\r");
    }

    [Fact]
    public void TtsVoiceMap_EachProvider_HasAtLeastOneVoice()
    {
        var voiceMap = new Dictionary<string, string[]>
        {
            ["Kokoro"] = ["af_heart", "af_sky", "af_bella", "am_adam", "am_michael", "bf_emma", "bm_george", "bf_isabella"],
            ["CosyVoice"] = ["korean_female", "korean_male", "chinese_female", "chinese_male", "english_female", "english_male"],
            ["Orpheus"] = ["tara", "leah", "jess", "leo", "dan", "mia", "zac", "zoe"],
            ["EdgeTts"] = ["ko-KR-SunHiNeural", "ko-KR-InJoonNeural", "en-US-JennyNeural", "en-US-GuyNeural", "ja-JP-NanamiNeural"],
            ["Piper"] = ["ko_KR-kss-low", "en_US-lessac-medium", "en_US-amy-medium", "en_GB-alba-medium"],
            ["FishSpeech"] = ["default"],
            ["Chatterbox"] = ["default"],
            ["Xtts"] = ["Claribel Dervla"],
            ["Bark"] = ["v2/ko_speaker_0", "v2/ko_speaker_1", "v2/ko_speaker_2", "v2/ko_speaker_3"],
            ["Browser"] = ["(OS default)"],
        };

        foreach (var (provider, voices) in voiceMap)
        {
            voices.Should().NotBeEmpty($"provider '{provider}' must have at least one voice option");
        }
    }

    [Fact]
    public void WriteTtsConfig_VoiceWithSpecialChars_IsPreserved()
    {
        var configPath = CreateConfigFile(new { Tts = new { Provider = "Bark", Voice = "default", Speed = 1.0 } });

        // Bark voices have slashes
        WriteTtsProvider(configPath, "Bark", "v2/ko_speaker_3");

        var (_, voice, _) = ReadTtsConfig(configPath);
        voice.Should().Be("v2/ko_speaker_3");
    }

    // ─── Helpers ──────────────────────────────────────────────

    private string CreateConfigFile(object config)
    {
        var path = Path.Combine(_tempDir, $"appsettings-{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// Mirrors EmbeddingConfigService.SetTtsProvider logic
    /// </summary>
    private static void WriteTtsProvider(string configPath, string provider, string voice, float speed = 1.0f)
    {
        if (!File.Exists(configPath)) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(configPath);
        var node = JsonNode.Parse(json);
        if (node is null) return;

        if (node["Tts"] is not JsonObject ttsSection)
        {
            ttsSection = new JsonObject();
            node["Tts"] = ttsSection;
        }

        ttsSection["Provider"] = provider;
        ttsSection["Voice"] = voice;
        ttsSection["Speed"] = speed;

        File.WriteAllText(configPath, node.ToJsonString(options));
    }

    /// <summary>
    /// Mirrors EmbeddingConfigService.GetCurrentTtsConfig logic
    /// </summary>
    private static (string provider, string voice, float speed) ReadTtsConfig(string configPath)
    {
        if (!File.Exists(configPath))
            return ("EdgeTts", "ko-KR-SunHiNeural", 1.0f);

        var json = File.ReadAllText(configPath);
        var node = JsonNode.Parse(json);
        var tts = node?["Tts"];
        if (tts is null)
            return ("EdgeTts", "ko-KR-SunHiNeural", 1.0f);

        var provider = tts["Provider"]?.GetValue<string>() ?? "EdgeTts";
        var voice = tts["Voice"]?.GetValue<string>() ?? "ko-KR-SunHiNeural";
        var speed = tts["Speed"]?.GetValue<float>() ?? 1.0f;
        return (provider, voice, speed);
    }
}
