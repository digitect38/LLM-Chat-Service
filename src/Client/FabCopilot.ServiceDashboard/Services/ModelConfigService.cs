using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabCopilot.ServiceDashboard.Services;

/// <summary>
/// Reads and writes default LLM/Embedding model configuration in LlmService's appsettings.json.
/// </summary>
public class ModelConfigService
{
    private readonly string _llmConfigPath;

    public ModelConfigService(IWebHostEnvironment env)
    {
        var srcRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", ".."));
        _llmConfigPath = Path.Combine(srcRoot, "src", "Services", "FabCopilot.LlmService", "appsettings.json");
    }

    /// <summary>For unit testing</summary>
    internal ModelConfigService(string llmConfigPath)
    {
        _llmConfigPath = llmConfigPath;
    }

    public record ModelInfo(string Id, string DisplayName);
    public record EmbeddingModelInfo(string Id, string DisplayName, int VectorSize);

    public class ModelConfig
    {
        public string ChatModel { get; set; } = "";
        public string EmbeddingModel { get; set; } = "";
        public int MaxTokens { get; set; }
        public int NumCtx { get; set; }
        public int TimeoutSeconds { get; set; }
        public List<ModelInfo> AvailableModels { get; set; } = [];
        public List<EmbeddingModelInfo> AvailableEmbeddingModels { get; set; } = [];
    }

    public ModelConfig GetCurrentConfig()
    {
        var result = new ModelConfig
        {
            ChatModel = "exaone3.5:7.8b",
            EmbeddingModel = "snowflake-arctic-embed2",
            MaxTokens = 1536,
            NumCtx = 4096,
            TimeoutSeconds = 120,
        };

        if (!File.Exists(_llmConfigPath)) return result;

        var json = File.ReadAllText(_llmConfigPath);
        var node = JsonNode.Parse(json);
        var ollama = node?["Ollama"];
        if (ollama is null) return result;

        result.ChatModel = ollama["ChatModel"]?.GetValue<string>() ?? result.ChatModel;
        result.EmbeddingModel = ollama["EmbeddingModel"]?.GetValue<string>() ?? result.EmbeddingModel;
        result.MaxTokens = ollama["MaxTokens"]?.GetValue<int>() ?? result.MaxTokens;
        result.NumCtx = ollama["NumCtx"]?.GetValue<int>() ?? result.NumCtx;
        result.TimeoutSeconds = ollama["TimeoutSeconds"]?.GetValue<int>() ?? result.TimeoutSeconds;

        if (ollama["AvailableModels"] is JsonArray models)
        {
            result.AvailableModels = models
                .Where(m => m is not null)
                .Select(m => new ModelInfo(
                    m!["Id"]?.GetValue<string>() ?? "",
                    m["DisplayName"]?.GetValue<string>() ?? ""))
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .ToList();
        }

        if (ollama["AvailableEmbeddingModels"] is JsonArray embedModels)
        {
            result.AvailableEmbeddingModels = embedModels
                .Where(m => m is not null)
                .Select(m => new EmbeddingModelInfo(
                    m!["Id"]?.GetValue<string>() ?? "",
                    m["DisplayName"]?.GetValue<string>() ?? "",
                    m["VectorSize"]?.GetValue<int>() ?? 0))
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .ToList();
        }

        return result;
    }

    public bool SetChatModel(string chatModel)
    {
        return UpdateOllamaField("ChatModel", chatModel);
    }

    public bool SetEmbeddingModel(string embeddingModel)
    {
        return UpdateOllamaField("EmbeddingModel", embeddingModel);
    }

    public bool SetParameters(int maxTokens, int numCtx, int timeoutSeconds)
    {
        if (!File.Exists(_llmConfigPath)) return false;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(_llmConfigPath);
        var node = JsonNode.Parse(json);
        if (node is null) return false;

        if (node["Ollama"] is not JsonObject section) return false;

        section["MaxTokens"] = maxTokens;
        section["NumCtx"] = numCtx;
        section["TimeoutSeconds"] = timeoutSeconds;

        File.WriteAllText(_llmConfigPath, node.ToJsonString(options));
        return true;
    }

    private bool UpdateOllamaField(string fieldName, string value)
    {
        if (!File.Exists(_llmConfigPath)) return false;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(_llmConfigPath);
        var node = JsonNode.Parse(json);
        if (node is null) return false;

        if (node["Ollama"] is not JsonObject section) return false;

        section[fieldName] = value;

        File.WriteAllText(_llmConfigPath, node.ToJsonString(options));
        return true;
    }
}
