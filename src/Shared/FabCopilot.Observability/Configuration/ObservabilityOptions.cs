namespace FabCopilot.Observability.Configuration;

public class ObservabilityOptions
{
    public const string SectionName = "Observability";
    public string ServiceName { get; set; } = "fab-copilot";
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
    public string LogFilePath { get; set; } = "logs/fab-copilot-.log";
}
