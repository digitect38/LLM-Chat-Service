namespace FabCopilot.Llm.Models;

public sealed class LlmChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;

    public static LlmChatMessage System(string content) => new() { Role = "system", Content = content };
    public static LlmChatMessage User(string content) => new() { Role = "user", Content = content };
    public static LlmChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };
}
