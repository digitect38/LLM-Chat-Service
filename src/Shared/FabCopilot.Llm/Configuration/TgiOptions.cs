namespace FabCopilot.Llm.Configuration;

public class TgiOptions
{
    public const string SectionName = "Tgi";
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string ChatModel { get; set; } = "default";
    public int TimeoutSeconds { get; set; } = 300;
}
