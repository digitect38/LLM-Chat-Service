using System.Text.Json;
using System.Text.Json.Nodes;
using FabCopilot.ServiceDashboard.Services;

namespace FabCopilot.ServiceDashboard.Tests;

public class SttConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SttConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stt-config-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetCurrentConfig_NoFile_ReturnsDefaults()
    {
        var svc = new SttConfigService(Path.Combine(_tempDir, "nonexistent.json"));
        var (engine, baseUrl, lang, maxFile, timeout) = svc.GetCurrentConfig();

        engine.Should().Be("auto");
        baseUrl.Should().Be("http://localhost:8300");
        lang.Should().Be("auto");
        maxFile.Should().Be(25);
        timeout.Should().Be(60);
    }

    [Fact]
    public void GetCurrentConfig_NoWhisperSection_ReturnsDefaults()
    {
        var path = CreateConfigFile(new { Nats = new { Url = "test" } });
        var svc = new SttConfigService(path);
        var (engine, _, lang, _, _) = svc.GetCurrentConfig();

        engine.Should().Be("auto");
        lang.Should().Be("auto");
    }

    [Fact]
    public void GetCurrentConfig_WithWhisperSection_ReadsValues()
    {
        var path = CreateConfigFile(new
        {
            Whisper = new { Engine = "whisper", BaseUrl = "http://localhost:8300", Language = "ko", MaxFileSizeMb = 50, TimeoutSeconds = 90 }
        });
        var svc = new SttConfigService(path);
        var (engine, baseUrl, lang, maxFile, timeout) = svc.GetCurrentConfig();

        engine.Should().Be("whisper");
        baseUrl.Should().Be("http://localhost:8300");
        lang.Should().Be("ko");
        maxFile.Should().Be(50);
        timeout.Should().Be(90);
    }

    [Fact]
    public void GetCurrentConfig_NoEngineField_DefaultsToAuto()
    {
        var path = CreateConfigFile(new
        {
            Whisper = new { BaseUrl = "http://localhost:8300", Language = "ko", MaxFileSizeMb = 25, TimeoutSeconds = 60 }
        });
        var svc = new SttConfigService(path);
        var (engine, _, _, _, _) = svc.GetCurrentConfig();

        engine.Should().Be("auto");
    }

    [Fact]
    public void SetConfig_WritesEngineAndLanguage()
    {
        var path = CreateConfigFile(new
        {
            Whisper = new { BaseUrl = "http://localhost:8300", MaxFileSizeMb = 25, TimeoutSeconds = 60 }
        });
        var svc = new SttConfigService(path);

        var ok = svc.SetConfig("whisper", "ko", 30, 90);
        ok.Should().BeTrue();

        var (engine, _, lang, maxFile, timeout) = svc.GetCurrentConfig();
        engine.Should().Be("whisper");
        lang.Should().Be("ko");
        maxFile.Should().Be(30);
        timeout.Should().Be(90);
    }

    [Fact]
    public void SetConfig_WebSpeechEngine_Saves()
    {
        var path = CreateConfigFile(new
        {
            Whisper = new { BaseUrl = "http://localhost:8300", MaxFileSizeMb = 25, TimeoutSeconds = 60 }
        });
        var svc = new SttConfigService(path);

        svc.SetConfig("webspeech", "ko", 25, 60);

        var (engine, _, _, _, _) = svc.GetCurrentConfig();
        engine.Should().Be("webspeech");
    }

    [Fact]
    public void SetConfig_PreservesBaseUrl()
    {
        var path = CreateConfigFile(new
        {
            Whisper = new { BaseUrl = "http://custom:9999", MaxFileSizeMb = 25, TimeoutSeconds = 60 }
        });
        var svc = new SttConfigService(path);

        svc.SetConfig("auto", "en", 25, 60);

        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json);
        node!["Whisper"]!["BaseUrl"]!.GetValue<string>().Should().Be("http://custom:9999");
    }

    [Fact]
    public void SetConfig_PreservesOtherSections()
    {
        var path = CreateConfigFile(new
        {
            Nats = new { Url = "nats://localhost:4222" },
            Whisper = new { BaseUrl = "http://localhost:8300", MaxFileSizeMb = 25, TimeoutSeconds = 60 }
        });
        var svc = new SttConfigService(path);

        svc.SetConfig("whisper", "ja", 50, 120);

        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json);
        node!["Nats"]!["Url"]!.GetValue<string>().Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void SetConfig_NoFile_ReturnsFalse()
    {
        var svc = new SttConfigService(Path.Combine(_tempDir, "nonexistent.json"));
        var ok = svc.SetConfig("auto", "ko", 25, 60);
        ok.Should().BeFalse();
    }

    [Fact]
    public void LanguageLabels_ContainsCommonLanguages()
    {
        SttConfigService.LanguageLabels.Should().ContainKey("auto");
        SttConfigService.LanguageLabels.Should().ContainKey("ko");
        SttConfigService.LanguageLabels.Should().ContainKey("en");
        SttConfigService.LanguageLabels.Should().ContainKey("ja");
        SttConfigService.LanguageLabels.Should().ContainKey("zh");
    }

    [Fact]
    public void EngineLabels_ContainsAllEngines()
    {
        SttConfigService.EngineLabels.Should().ContainKey("auto");
        SttConfigService.EngineLabels.Should().ContainKey("whisper");
        SttConfigService.EngineLabels.Should().ContainKey("webspeech");
        SttConfigService.EngineLabels.Should().HaveCount(3);
    }

    [Fact]
    public void SetConfig_CreatesWhisperSection_IfMissing()
    {
        var path = CreateConfigFile(new { Nats = new { Url = "test" } });
        var svc = new SttConfigService(path);

        var ok = svc.SetConfig("whisper", "de", 40, 80);
        ok.Should().BeTrue();

        var (engine, _, lang, maxFile, timeout) = svc.GetCurrentConfig();
        engine.Should().Be("whisper");
        lang.Should().Be("de");
        maxFile.Should().Be(40);
        timeout.Should().Be(80);
    }

    [Fact]
    public void SetConfig_EngineChange_DoesNotAffectOtherFields()
    {
        var path = CreateConfigFile(new
        {
            Whisper = new { Engine = "auto", BaseUrl = "http://localhost:8300", Language = "ko", MaxFileSizeMb = 25, TimeoutSeconds = 60 }
        });
        var svc = new SttConfigService(path);

        svc.SetConfig("webspeech", "ko", 25, 60);

        var (engine, _, lang, maxFile, timeout) = svc.GetCurrentConfig();
        engine.Should().Be("webspeech");
        lang.Should().Be("ko");
        maxFile.Should().Be(25);
        timeout.Should().Be(60);
    }

    private string CreateConfigFile(object config)
    {
        var path = Path.Combine(_tempDir, $"appsettings-{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }
}
