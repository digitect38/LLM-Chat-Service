namespace FabCopilot.Observability.Configuration;

public class ObservabilityOptions
{
    public const string SectionName = "Observability";
    public string ServiceName { get; set; } = "fab-copilot";
    public bool EnableTracing { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
    public string LogFilePath { get; set; } = "logs/fab-copilot-.log";

    /// <summary>Enable JSON structured log file (in addition to text console).</summary>
    public bool EnableJsonLog { get; set; } = false;

    /// <summary>JSON log file path (daily rolling).</summary>
    public string JsonLogFilePath { get; set; } = "logs/fab-copilot-.json";

    /// <summary>Enable sensitive data masking in logs at INFO level and above.</summary>
    public bool EnableSensitiveDataMasking { get; set; } = true;
}
