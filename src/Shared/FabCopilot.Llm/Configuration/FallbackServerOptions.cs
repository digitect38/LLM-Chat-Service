namespace FabCopilot.Llm.Configuration;

public class FallbackServerOptions
{
    public const string SectionName = "Llm:FallbackServer";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Ollama";
    public string BaseUrl { get; set; } = string.Empty;
    public string ChatModel { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutSeconds { get; set; } = 300;
    public int HealthCheckIntervalSeconds { get; set; } = 10;
    public int FailoverThresholdSeconds { get; set; } = 30;

    /// <summary>
    /// SLM model to use when both primary and secondary are unavailable.
    /// Must be a lightweight model that can run on CPU (e.g., "tinyllama").
    /// Empty = disabled.
    /// </summary>
    public string SlmFallbackModel { get; set; } = string.Empty;
}
