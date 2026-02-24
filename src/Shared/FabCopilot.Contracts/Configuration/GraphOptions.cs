namespace FabCopilot.Contracts.Configuration;

public sealed class GraphOptions
{
    public const string SectionName = "Graph";

    public int GraphMaxDepth { get; set; } = 2;
}
