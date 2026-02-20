namespace FabCopilot.VectorStore.Configuration;

public class QdrantOptions
{
    public const string SectionName = "Qdrant";
    public string Host { get; set; } = "localhost";
    public int GrpcPort { get; set; } = 6334;
    public int HttpPort { get; set; } = 6333;
    public string DefaultCollection { get; set; } = "knowledge";
    public int VectorSize { get; set; } = 768;
}
