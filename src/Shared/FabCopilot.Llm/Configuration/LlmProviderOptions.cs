namespace FabCopilot.Llm.Configuration;

public class LlmProviderOptions
{
    public const string SectionName = "Llm";
    public string Provider { get; set; } = "Ollama";
}
