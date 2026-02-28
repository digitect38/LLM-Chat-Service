using System.Text.Json;
using System.Text.Json.Nodes;
using FabCopilot.WebClient.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// EmbeddingConfigService를 통한 서버 설정 저장/로드 검증
/// - LLM 모델 선택 저장
/// - 검색 모드(Hybrid/Strict) 저장
/// - Embedding/LLM Provider 저장
/// - RAG 토글 저장
/// </summary>
public class ServerSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _ragPath;
    private readonly string _knowledgePath;
    private readonly string _llmPath;
    private readonly string _webClientPath;
    private readonly EmbeddingConfigService _svc;

    public ServerSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fab-settings-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _ragPath = Path.Combine(_tempDir, "rag-appsettings.json");
        _knowledgePath = Path.Combine(_tempDir, "knowledge-appsettings.json");
        _llmPath = Path.Combine(_tempDir, "llm-appsettings.json");
        _webClientPath = Path.Combine(_tempDir, "webclient-appsettings.json");

        // Seed minimal config files
        var ragJson = new JsonObject
        {
            ["Embedding"] = new JsonObject { ["Provider"] = "Ollama" },
            ["Rag"] = new JsonObject
            {
                ["EnableQueryRewriting"] = false,
                ["EnableLlmReranking"] = false,
                ["EnableGraphLookup"] = true,
                ["EnableHybridSearch"] = true,
                ["EnableMmr"] = true,
                ["EnableRagCache"] = true
            }
        };
        var llmJson = new JsonObject
        {
            ["Llm"] = new JsonObject { ["Provider"] = "Ollama" },
            ["Embedding"] = new JsonObject { ["Provider"] = "Ollama" },
            ["Ollama"] = new JsonObject { ["ChatModel"] = "exaone3.5:7.8b" }
        };
        var knowledgeJson = new JsonObject
        {
            ["Embedding"] = new JsonObject { ["Provider"] = "Ollama" }
        };
        var webClientJson = new JsonObject
        {
            ["Models"] = new JsonObject { ["Default"] = "exaone3.5:7.8b" }
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_ragPath, ragJson.ToJsonString(opts));
        File.WriteAllText(_llmPath, llmJson.ToJsonString(opts));
        File.WriteAllText(_knowledgePath, knowledgeJson.ToJsonString(opts));
        File.WriteAllText(_webClientPath, webClientJson.ToJsonString(opts));

        _svc = new EmbeddingConfigService(
            new[] { _ragPath, _knowledgePath, _llmPath },
            _llmPath,
            _webClientPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ═══════════════════════════════════════════════════════════
    // LLM 모델 저장/로드
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentLlmModel_ReturnsPersistedModel()
    {
        var model = _svc.GetCurrentLlmModel();
        model.Should().Be("exaone3.5:7.8b");
    }

    [Fact]
    public void SetLlmModel_UpdatesLlmServiceConfig()
    {
        _svc.SetLlmModel("qwen2.5:7b");

        var json = JsonNode.Parse(File.ReadAllText(_llmPath));
        json!["Ollama"]!["ChatModel"]!.GetValue<string>().Should().Be("qwen2.5:7b");
    }

    [Fact]
    public void SetLlmModel_UpdatesWebClientDefault()
    {
        _svc.SetLlmModel("deepseek-r1:7b");

        var json = JsonNode.Parse(File.ReadAllText(_webClientPath));
        json!["Models"]!["Default"]!.GetValue<string>().Should().Be("deepseek-r1:7b");
    }

    [Fact]
    public void SetLlmModel_RoundTrips()
    {
        _svc.SetLlmModel("llama3.1:8b");
        _svc.GetCurrentLlmModel().Should().Be("llama3.1:8b");
    }

    [Fact]
    public void SetLlmModel_EmptyString_NoChange()
    {
        _svc.SetLlmModel("");
        _svc.GetCurrentLlmModel().Should().Be("exaone3.5:7.8b");
    }

    [Fact]
    public void SetLlmModel_CreatesOllamaSectionIfMissing()
    {
        // Remove Ollama section
        var node = JsonNode.Parse(File.ReadAllText(_llmPath))!;
        (node as JsonObject)!.Remove("Ollama");
        File.WriteAllText(_llmPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        _svc.SetLlmModel("test-model");
        _svc.GetCurrentLlmModel().Should().Be("test-model");
    }

    // ═══════════════════════════════════════════════════════════
    // 검색 모드 저장/로드
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentSearchMode_DefaultsToHybrid()
    {
        _svc.GetCurrentSearchMode().Should().Be("hybrid");
    }

    [Fact]
    public void SetSearchMode_Strict_Persists()
    {
        _svc.SetSearchMode("strict");
        _svc.GetCurrentSearchMode().Should().Be("strict");
    }

    [Fact]
    public void SetSearchMode_Hybrid_Persists()
    {
        _svc.SetSearchMode("strict");
        _svc.SetSearchMode("hybrid");
        _svc.GetCurrentSearchMode().Should().Be("hybrid");
    }

    [Fact]
    public void SetSearchMode_WritesToWebClientConfig()
    {
        _svc.SetSearchMode("strict");

        var json = JsonNode.Parse(File.ReadAllText(_webClientPath));
        json!["SearchMode"]!.GetValue<string>().Should().Be("strict");
    }

    [Fact]
    public void SetSearchMode_EmptyString_NoChange()
    {
        _svc.SetSearchMode("strict");
        _svc.SetSearchMode("");
        _svc.GetCurrentSearchMode().Should().Be("strict");
    }

    // ═══════════════════════════════════════════════════════════
    // Embedding Provider 저장/로드
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentProvider_ReturnsOllama()
    {
        _svc.GetCurrentProvider().Should().Be("Ollama");
    }

    [Fact]
    public void SetProvider_Tei_UpdatesAllThreeFiles()
    {
        _svc.SetProvider("Tei");

        foreach (var path in new[] { _ragPath, _knowledgePath, _llmPath })
        {
            var json = JsonNode.Parse(File.ReadAllText(path));
            json!["Embedding"]!["Provider"]!.GetValue<string>().Should().Be("Tei", $"file: {Path.GetFileName(path)}");
        }
    }

    [Fact]
    public void SetProvider_RoundTrips()
    {
        _svc.SetProvider("Tei");
        _svc.GetCurrentProvider().Should().Be("Tei");

        _svc.SetProvider("Ollama");
        _svc.GetCurrentProvider().Should().Be("Ollama");
    }

    // ═══════════════════════════════════════════════════════════
    // LLM Chat Provider 저장/로드
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentLlmProvider_ReturnsOllama()
    {
        _svc.GetCurrentLlmProvider().Should().Be("Ollama");
    }

    [Fact]
    public void SetLlmProvider_Tgi_Persists()
    {
        _svc.SetLlmProvider("Tgi");
        _svc.GetCurrentLlmProvider().Should().Be("Tgi");
    }

    [Fact]
    public void SetLlmProvider_WritesToLlmServiceConfig()
    {
        _svc.SetLlmProvider("Tgi");

        var json = JsonNode.Parse(File.ReadAllText(_llmPath));
        json!["Llm"]!["Provider"]!.GetValue<string>().Should().Be("Tgi");
    }

    // ═══════════════════════════════════════════════════════════
    // RAG 토글 저장/로드
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetRagToggles_ReadsFromConfig()
    {
        var toggles = _svc.GetRagToggles();

        toggles["EnableQueryRewriting"].Should().BeFalse();
        toggles["EnableLlmReranking"].Should().BeFalse();
        toggles["EnableGraphLookup"].Should().BeTrue();
        toggles["EnableHybridSearch"].Should().BeTrue();
        toggles["EnableMmr"].Should().BeTrue();
        toggles["EnableRagCache"].Should().BeTrue();
    }

    [Fact]
    public void SetRagToggles_PersistsAllToggles()
    {
        _svc.SetRagToggles(new Dictionary<string, bool>
        {
            ["EnableQueryRewriting"] = true,
            ["EnableLlmReranking"] = true,
            ["EnableGraphLookup"] = false,
            ["EnableHybridSearch"] = false,
            ["EnableMmr"] = false,
            ["EnableRagCache"] = false,
        });

        var toggles = _svc.GetRagToggles();
        toggles["EnableQueryRewriting"].Should().BeTrue();
        toggles["EnableLlmReranking"].Should().BeTrue();
        toggles["EnableGraphLookup"].Should().BeFalse();
        toggles["EnableHybridSearch"].Should().BeFalse();
        toggles["EnableMmr"].Should().BeFalse();
        toggles["EnableRagCache"].Should().BeFalse();
    }

    [Fact]
    public void SetRagToggles_PreservesOtherRagConfig()
    {
        // Add extra Rag config key
        var node = JsonNode.Parse(File.ReadAllText(_ragPath))!;
        node["Rag"]!["MinScore"] = 0.45;
        File.WriteAllText(_ragPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        _svc.SetRagToggles(new Dictionary<string, bool>
        {
            ["EnableQueryRewriting"] = true,
            ["EnableLlmReranking"] = false,
            ["EnableGraphLookup"] = true,
            ["EnableHybridSearch"] = true,
            ["EnableMmr"] = true,
            ["EnableRagCache"] = true,
        });

        var updated = JsonNode.Parse(File.ReadAllText(_ragPath))!;
        updated["Rag"]!["MinScore"]!.GetValue<double>().Should().Be(0.45);
    }

    // ═══════════════════════════════════════════════════════════
    // 전체 설정 일괄 적용 시나리오
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FullApplyScenario_AllSettingsPersisted()
    {
        // Simulate ServerSettings.ApplyServerSettings()
        _svc.SetLlmModel("qwen2.5:7b");
        _svc.SetSearchMode("strict");
        _svc.SetLlmProvider("Tgi");
        _svc.SetProvider("Tei");
        _svc.SetRagToggles(new Dictionary<string, bool>
        {
            ["EnableQueryRewriting"] = true,
            ["EnableLlmReranking"] = true,
            ["EnableGraphLookup"] = false,
            ["EnableHybridSearch"] = false,
            ["EnableMmr"] = true,
            ["EnableRagCache"] = false,
        });

        // Verify all settings persisted
        _svc.GetCurrentLlmModel().Should().Be("qwen2.5:7b");
        _svc.GetCurrentSearchMode().Should().Be("strict");
        _svc.GetCurrentLlmProvider().Should().Be("Tgi");
        _svc.GetCurrentProvider().Should().Be("Tei");

        var toggles = _svc.GetRagToggles();
        toggles["EnableQueryRewriting"].Should().BeTrue();
        toggles["EnableLlmReranking"].Should().BeTrue();
        toggles["EnableGraphLookup"].Should().BeFalse();
        toggles["EnableHybridSearch"].Should().BeFalse();
        toggles["EnableMmr"].Should().BeTrue();
        toggles["EnableRagCache"].Should().BeFalse();
    }

    [Fact]
    public void MissingConfigFile_NoException()
    {
        var svc = new EmbeddingConfigService(
            new[] { "/nonexistent/path.json" },
            "/nonexistent/llm.json",
            "/nonexistent/webclient.json");

        // Should return defaults without throwing
        svc.GetCurrentProvider().Should().Be("Ollama");
        svc.GetCurrentLlmProvider().Should().Be("Ollama");
        svc.GetCurrentLlmModel().Should().BeEmpty();
        svc.GetCurrentSearchMode().Should().Be("hybrid");
    }
}
