namespace FabCopilot.Llm.Configuration;

public class EmbeddingProviderOptions
{
    public const string SectionName = "Embedding";
    public string Provider { get; set; } = "Ollama";
    public string? FallbackServer { get; set; }
}
