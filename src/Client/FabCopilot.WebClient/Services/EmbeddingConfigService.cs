using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabCopilot.WebClient.Services;

public class EmbeddingConfigService
{
    private readonly string[] _appsettingsPaths;
    private readonly string _llmServicePath;

    public EmbeddingConfigService(IWebHostEnvironment env)
    {
        // WebClient is at src/Client/FabCopilot.WebClient
        // Services are at src/Services/FabCopilot.{Name}
        var clientRoot = env.ContentRootPath; // .../src/Client/FabCopilot.WebClient
        var srcRoot = Path.GetFullPath(Path.Combine(clientRoot, "..", "..", ".."));

        _llmServicePath = Path.Combine(srcRoot, "src", "Services", "FabCopilot.LlmService", "appsettings.json");

        _appsettingsPaths = new[]
        {
            Path.Combine(srcRoot, "src", "Services", "FabCopilot.RagService", "appsettings.json"),
            Path.Combine(srcRoot, "src", "Services", "FabCopilot.KnowledgeService", "appsettings.json"),
            _llmServicePath,
        };
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
}
