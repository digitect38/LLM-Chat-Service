using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabCopilot.ServiceDashboard.Services;

/// <summary>
/// Reads and writes STT (Whisper) configuration in ChatGateway's appsettings.json.
/// </summary>
public class SttConfigService
{
    private readonly string _gatewayConfigPath;

    public SttConfigService(IWebHostEnvironment env)
    {
        var srcRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", ".."));
        _gatewayConfigPath = Path.Combine(srcRoot, "src", "Services", "FabCopilot.ChatGateway", "appsettings.json");
    }

    /// <summary>For unit testing</summary>
    internal SttConfigService(string gatewayConfigPath)
    {
        _gatewayConfigPath = gatewayConfigPath;
    }

    public static readonly Dictionary<string, string> EngineLabels = new()
    {
        ["auto"] = "Auto-detect (Whisper preferred)",
        ["whisper"] = "Whisper (On-Premise GPU)",
        ["webspeech"] = "Web Speech API (Browser/Cloud)",
    };

    public static readonly Dictionary<string, string> LanguageLabels = new()
    {
        ["auto"] = "Auto-detect",
        ["ko"] = "Korean",
        ["en"] = "English",
        ["ja"] = "Japanese",
        ["zh"] = "Chinese",
        ["de"] = "German",
        ["fr"] = "French",
        ["es"] = "Spanish",
    };

    public (string Engine, string BaseUrl, string Language, int MaxFileSizeMb, int TimeoutSeconds) GetCurrentConfig()
    {
        if (!File.Exists(_gatewayConfigPath))
            return ("auto", "http://localhost:8300", "auto", 25, 60);

        var json = File.ReadAllText(_gatewayConfigPath);
        var node = JsonNode.Parse(json);
        var whisper = node?["Whisper"];
        if (whisper is null)
            return ("auto", "http://localhost:8300", "auto", 25, 60);

        var engine = whisper["Engine"]?.GetValue<string>() ?? "auto";
        var baseUrl = whisper["BaseUrl"]?.GetValue<string>() ?? "http://localhost:8300";
        var language = whisper["Language"]?.GetValue<string>() ?? "auto";
        var maxFile = whisper["MaxFileSizeMb"]?.GetValue<int>() ?? 25;
        var timeout = whisper["TimeoutSeconds"]?.GetValue<int>() ?? 60;
        return (engine, baseUrl, language, maxFile, timeout);
    }

    public bool SetConfig(string engine, string language, int maxFileSizeMb, int timeoutSeconds)
    {
        if (!File.Exists(_gatewayConfigPath)) return false;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = File.ReadAllText(_gatewayConfigPath);
        var node = JsonNode.Parse(json);
        if (node is null) return false;

        if (node["Whisper"] is not JsonObject section)
        {
            section = new JsonObject();
            node["Whisper"] = section;
        }

        section["Engine"] = engine;
        section["Language"] = language;
        section["MaxFileSizeMb"] = maxFileSizeMb;
        section["TimeoutSeconds"] = timeoutSeconds;

        File.WriteAllText(_gatewayConfigPath, node.ToJsonString(options));
        return true;
    }
}
