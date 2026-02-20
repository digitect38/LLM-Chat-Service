using FabCopilot.Contracts.Enums;

namespace FabCopilot.RagService.Configuration;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public float MinScore { get; set; } = 0.45f;

    public int DefaultTopK { get; set; } = 3;

    public string? WatchFolder { get; set; }

    public int DebounceMs { get; set; } = 500;

    public bool ScanOnStartup { get; set; } = true;

    // Advanced RAG
    public bool EnableQueryRewriting { get; set; } = true;
    public bool EnableLlmReranking { get; set; } = true;
    public int LlmRerankCandidateCount { get; set; } = 20;

    // GraphRAG
    public bool EnableGraphLookup { get; set; }
    public int GraphMaxDepth { get; set; } = 2;
    public bool ExtractEntitiesOnIngest { get; set; }

    // Agentic RAG
    public int DefaultMaxAgenticIterations { get; set; } = 3;

    // Default pipeline
    public RagPipelineMode DefaultPipelineMode { get; set; } = RagPipelineMode.Naive;
}
