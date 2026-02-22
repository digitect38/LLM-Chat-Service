namespace FabCopilot.Llm.Models;

public sealed class LlmOptions
{
    public string? Model { get; set; }
    public float Temperature { get; set; } = 0.1f;
    public int MaxTokens { get; set; } = 4096;
}
