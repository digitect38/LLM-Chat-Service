using System.Text.Json;
using System.Text.Json.Nodes;
using FabCopilot.ServiceDashboard.Services;

namespace FabCopilot.ServiceDashboard.Tests;

public class ModelConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ModelConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"model-config-tests-{Guid.NewGuid():N}");
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
        var svc = new ModelConfigService(Path.Combine(_tempDir, "nonexistent.json"));
        var cfg = svc.GetCurrentConfig();

        cfg.ChatModel.Should().Be("exaone3.5:7.8b");
        cfg.EmbeddingModel.Should().Be("snowflake-arctic-embed2");
        cfg.MaxTokens.Should().Be(1536);
        cfg.NumCtx.Should().Be(4096);
    }

    [Fact]
    public void GetCurrentConfig_ReadsOllamaSection()
    {
        var path = CreateLlmConfigFile("qwen2.5:7b", "bge-m3", 2048, 8192, 180);
        var svc = new ModelConfigService(path);
        var cfg = svc.GetCurrentConfig();

        cfg.ChatModel.Should().Be("qwen2.5:7b");
        cfg.EmbeddingModel.Should().Be("bge-m3");
        cfg.MaxTokens.Should().Be(2048);
        cfg.NumCtx.Should().Be(8192);
        cfg.TimeoutSeconds.Should().Be(180);
    }

    [Fact]
    public void GetCurrentConfig_ReadsAvailableModels()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);
        var cfg = svc.GetCurrentConfig();

        cfg.AvailableModels.Should().HaveCount(2);
        cfg.AvailableModels.Should().Contain(m => m.Id == "exaone3.5:7.8b");
        cfg.AvailableModels.Should().Contain(m => m.Id == "qwen2.5:7b");
    }

    [Fact]
    public void GetCurrentConfig_ReadsAvailableEmbeddingModels()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);
        var cfg = svc.GetCurrentConfig();

        cfg.AvailableEmbeddingModels.Should().HaveCount(2);
        cfg.AvailableEmbeddingModels.Should().Contain(m => m.Id == "snowflake-arctic-embed2" && m.VectorSize == 1024);
        cfg.AvailableEmbeddingModels.Should().Contain(m => m.Id == "bge-m3" && m.VectorSize == 1024);
    }

    [Fact]
    public void SetChatModel_UpdatesValue()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);

        var ok = svc.SetChatModel("qwen2.5:7b");
        ok.Should().BeTrue();

        var cfg = svc.GetCurrentConfig();
        cfg.ChatModel.Should().Be("qwen2.5:7b");
    }

    [Fact]
    public void SetEmbeddingModel_UpdatesValue()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);

        var ok = svc.SetEmbeddingModel("bge-m3");
        ok.Should().BeTrue();

        var cfg = svc.GetCurrentConfig();
        cfg.EmbeddingModel.Should().Be("bge-m3");
    }

    [Fact]
    public void SetParameters_UpdatesAllValues()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);

        var ok = svc.SetParameters(2048, 8192, 180);
        ok.Should().BeTrue();

        var cfg = svc.GetCurrentConfig();
        cfg.MaxTokens.Should().Be(2048);
        cfg.NumCtx.Should().Be(8192);
        cfg.TimeoutSeconds.Should().Be(180);
    }

    [Fact]
    public void SetChatModel_PreservesOtherSettings()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);

        svc.SetChatModel("qwen2.5:7b");

        var cfg = svc.GetCurrentConfig();
        cfg.EmbeddingModel.Should().Be("snowflake-arctic-embed2");
        cfg.MaxTokens.Should().Be(1536);
    }

    [Fact]
    public void SetChatModel_PreservesOtherSections()
    {
        var path = CreateLlmConfigFile("exaone3.5:7.8b", "snowflake-arctic-embed2", 1536, 4096, 120);
        var svc = new ModelConfigService(path);

        svc.SetChatModel("qwen2.5:7b");

        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json);
        node!["Nats"]!["Url"]!.GetValue<string>().Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void SetChatModel_NoFile_ReturnsFalse()
    {
        var svc = new ModelConfigService(Path.Combine(_tempDir, "nonexistent.json"));
        var ok = svc.SetChatModel("test");
        ok.Should().BeFalse();
    }

    [Fact]
    public void SetParameters_NoFile_ReturnsFalse()
    {
        var svc = new ModelConfigService(Path.Combine(_tempDir, "nonexistent.json"));
        var ok = svc.SetParameters(2048, 8192, 180);
        ok.Should().BeFalse();
    }

    private string CreateLlmConfigFile(string chatModel, string embedModel, int maxTokens, int numCtx, int timeout)
    {
        var config = new
        {
            Nats = new { Url = "nats://localhost:4222" },
            Ollama = new
            {
                BaseUrl = "http://localhost:11434",
                ChatModel = chatModel,
                EmbeddingModel = embedModel,
                MaxTokens = maxTokens,
                NumCtx = numCtx,
                TimeoutSeconds = timeout,
                AvailableModels = new[]
                {
                    new { Id = "exaone3.5:7.8b", DisplayName = "EXAONE 3.5 (7.8B)" },
                    new { Id = "qwen2.5:7b", DisplayName = "Qwen 2.5 (7B)" }
                },
                AvailableEmbeddingModels = new[]
                {
                    new { Id = "snowflake-arctic-embed2", DisplayName = "Arctic Embed2 (1024D)", VectorSize = 1024 },
                    new { Id = "bge-m3", DisplayName = "BGE-M3 (1024D)", VectorSize = 1024 }
                }
            }
        };

        var path = Path.Combine(_tempDir, $"appsettings-{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }
}
