namespace FabCopilot.Llm.Configuration;

public class TeiOptions
{
    public const string SectionName = "Tei";
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string EmbeddingModel { get; set; } = "bge-m3";
    public int TimeoutSeconds { get; set; } = 120;
    public int VectorSize { get; set; } = 1024;
}
