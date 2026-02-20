namespace FabCopilot.WebClient.Configuration;

public class ModelOptions
{
    public const string SectionName = "Models";
    public string Default { get; set; } = string.Empty;
    public List<ModelEntry> Available { get; set; } = [];
}

public class ModelEntry
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
