using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabCopilot.WebClient.Services;

public class EmbeddingConfigService
{
    private readonly string[] _appsettingsPaths;
    private readonly string _llmServicePath;
    private readonly string _webClientPath;
    private readonly string _gatewayPath;

    public EmbeddingConfigService(IWebHostEnvironment env)
    {
        // WebClient is at src/Client/FabCopilot.WebClient
        // Services are at src/Services/FabCopilot.{Name}
        var clientRoot = env.ContentRootPath; // .../src/Client/FabCopilot.WebClient
        var srcRoot = Path.GetFullPath(Path.Combine(clientRoot, "..", "..", ".."));

        _llmServicePath = Path.Combine(srcRoot, "src", "Services", "FabCopilot.LlmService", "appsettings.json");
        _webClientPath = Path.Combine(clientRoot, "appsettings.json");
        _gatewayPath = Path.Combine(srcRoot, "src", "Services", "FabCopilot.ChatGateway", "appsettings.json");

        _appsettingsPaths = new[]
        {
            Path.Combine(srcRoot, "src", "Services", "FabCopilot.RagService", "appsettings.json"),
            Path.Combine(srcRoot, "src", "Services", "FabCopilot.KnowledgeService", "appsettings.json"),
            _llmServicePath,
        };
    }

    // ─── For unit testing ───────────────────────────────────────
    internal EmbeddingConfigService(string[] appsettingsPaths, string llmServicePath, string webClientPath)
    {
        _appsettingsPaths = appsettingsPaths;
        _llmServicePath = llmServicePath;
        _webClientPath = webClientPath;
        _gatewayPath = "";
    }

    public string GetCurrentProvider()
    {
        // Read from the first available appsettings.json
        foreach (var path in _appsettingsPaths)
        {
            if (!File.Exists(path)) continue;

            var json = File.ReadAllText(path);
            var node = JsonNode.Parse(json);
            var provider = node?["Embedding"]?["Provider"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(provider))
                return provider;
        }

        return "Ollama";
    }

    public void SetProvider(string provider)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };

        foreach (var path in _appsettingsPaths)
        {
            if (!File.Exists(path)) continue;

            var json = File.ReadAllText(path);
            var node = JsonNode.Parse(json);
            if (node is null) continue;

            // Ensure Embedding section exists
            if (node["Embedding"] is not JsonObject embeddingSection)
            {
                embeddingSection = new JsonObject();
                node["Embedding"] = embeddingSection;
            }

            embeddingSection["Provider"] = provider;

            File.WriteAllText(path, node.ToJsonString(options));
        }
    }

    public string GetCurrentLlmProvider()
    {
        if (!File.Exists(_llmServicePath)) return "Ollama";

        var json = File.ReadAllText(_llmServicePath);
        var node = JsonNode.Parse(json);
        var provider = node?["Llm"]?["Provider"]?.GetValue<string>();
        return !string.IsNullOrEmpty(provider) ? provider : "Ollama";
    }

    public void SetLlmProvider(string provider)
    {
        if (!File.Exists(_llmServicePath)) return;

        var options = new JsonSerializerOptions { WriteIndented = true };

        var json = File.ReadAllText(_llmServicePath);
        var node = JsonNode.Parse(json);
        if (node is null) return;

        if (node["Llm"] is not JsonObject llmSection)
        {
            llmSection = new JsonObject();
            node["Llm"] = llmSection;
        }

        llmSection["Provider"] = provider;

        File.WriteAllText(_llmServicePath, node.ToJsonString(options));
    }

    private static readonly string[] RagToggleKeys =
    {
        "EnableQueryRewriting",
        "EnableLlmReranking",
        "EnableGraphLookup",
        "EnableHybridSearch",
        "EnableMmr",
        "EnableRagCache",
    };

    public Dictionary<string, bool> GetRagToggles()
    {
        var ragPath = _appsettingsPaths[0]; // RagService appsettings.json
        var defaults = new Dictionary<string, bool>
        {
            ["EnableQueryRewriting"] = true,
            ["EnableLlmReranking"] = false,
            ["EnableGraphLookup"] = true,
            ["EnableHybridSearch"] = true,
            ["EnableMmr"] = true,
            ["EnableRagCache"] = true,
        };

        if (!File.Exists(ragPath)) return defaults;

        var json = File.ReadAllText(ragPath);
        var node = JsonNode.Parse(json);
        var ragSection = node?["Rag"];
        if (ragSection is null) return defaults;

        foreach (var key in RagToggleKeys)
        {
            var val = ragSection[key];
            if (val is not null)
                defaults[key] = val.GetValue<bool>();
        }

        return defaults;
    }

    public void SetRagToggles(Dictionary<string, bool> toggles)
    {
        var ragPath = _appsettingsPaths[0]; // RagService appsettings.json
        if (!File.Exists(ragPath)) return;

        var options = new JsonSerializerOptions { WriteIndented = true };

        var json = File.ReadAllText(ragPath);
        var node = JsonNode.Parse(json);
        if (node is null) return;

        if (node["Rag"] is not JsonObject ragSection)
        {
            ragSection = new JsonObject();
            node["Rag"] = ragSection;
        }

        foreach (var key in RagToggleKeys)
        {
            if (toggles.TryGetValue(key, out var value))
                ragSection[key] = value;
        }

        File.WriteAllText(ragPath, node.ToJsonString(options));
    }

    // ─── LLM Model ─────────────────────────────────────────────

    public string GetCurrentLlmModel()
    {
        if (!File.Exists(_llmServicePath)) return string.Empty;

        var json = File.ReadAllText(_llmServicePath);
        var node = JsonNode.Parse(json);
        return node?["Ollama"]?["ChatModel"]?.GetValue<string>() ?? string.Empty;
    }

    public void SetLlmModel(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return;

        var options = new JsonSerializerOptions { WriteIndented = true };

        // Update LlmService Ollama.ChatModel
        if (File.Exists(_llmServicePath))
        {
            var json = File.ReadAllText(_llmServicePath);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                if (node["Ollama"] is not JsonObject ollamaSection)
                {
                    ollamaSection = new JsonObject();
                    node["Ollama"] = ollamaSection;
                }
                ollamaSection["ChatModel"] = modelId;
                File.WriteAllText(_llmServicePath, node.ToJsonString(options));
            }
        }

        // Update WebClient Models.Default
        if (File.Exists(_webClientPath))
        {
            var json = File.ReadAllText(_webClientPath);
            var node = JsonNode.Parse(json);
            if (node is not null)
            {
                if (node["Models"] is not JsonObject modelsSection)
                {
                    modelsSection = new JsonObject();
                    node["Models"] = modelsSection;
                }
                modelsSection["Default"] = modelId;
                File.WriteAllText(_webClientPath, node.ToJsonString(options));
            }
        }
    }

    // ─── Search Mode ────────────────────────────────────────────

    public string GetCurrentSearchMode()
    {
        if (!File.Exists(_webClientPath)) return "hybrid";

        var json = File.ReadAllText(_webClientPath);
        var node = JsonNode.Parse(json);
        return node?["SearchMode"]?.GetValue<string>() ?? "hybrid";
    }

    public void SetSearchMode(string mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        if (!File.Exists(_webClientPath)) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(_webClientPath);
        var node = JsonNode.Parse(json);
        if (node is null) return;

        node["SearchMode"] = mode;
        File.WriteAllText(_webClientPath, node.ToJsonString(options));
    }

    // ─── TTS Provider ──────────────────────────────────────────

    public (string Provider, string Voice, float Speed) GetCurrentTtsConfig()
    {
        if (string.IsNullOrEmpty(_gatewayPath) || !File.Exists(_gatewayPath))
            return ("EdgeTts", "ko-KR-SunHiNeural", 1.0f);

        var json = File.ReadAllText(_gatewayPath);
        var node = JsonNode.Parse(json);
        var tts = node?["Tts"];
        if (tts is null)
            return ("EdgeTts", "ko-KR-SunHiNeural", 1.0f);

        var provider = tts["Provider"]?.GetValue<string>() ?? "EdgeTts";
        var voice = tts["Voice"]?.GetValue<string>() ?? "ko-KR-SunHiNeural";
        var speed = tts["Speed"]?.GetValue<float>() ?? 1.0f;
        return (provider, voice, speed);
    }

    public void SetTtsProvider(string provider, string voice, float speed = 1.0f)
    {
        if (string.IsNullOrEmpty(_gatewayPath) || !File.Exists(_gatewayPath)) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(_gatewayPath);
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

        File.WriteAllText(_gatewayPath, node.ToJsonString(options));
    }
}
