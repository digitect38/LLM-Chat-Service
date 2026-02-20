namespace FabCopilot.RagService.Configuration;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public float MinScore { get; set; } = 0.3f;

    public int DefaultTopK { get; set; } = 5;

    public string? WatchFolder { get; set; }

    public int DebounceMs { get; set; } = 500;

    public bool ScanOnStartup { get; set; } = true;
}
