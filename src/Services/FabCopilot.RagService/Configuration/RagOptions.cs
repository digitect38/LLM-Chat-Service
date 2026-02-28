using FabCopilot.Contracts.Enums;

namespace FabCopilot.RagService.Configuration;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public float MinScore { get; set; } = 0.55f;

    public int DefaultTopK { get; set; } = 5;

    public string? WatchFolder { get; set; }

    public int DebounceMs { get; set; } = 500;

    public bool ScanOnStartup { get; set; } = true;

    // Advanced RAG
    public bool EnableQueryRewriting { get; set; } = true;
    public bool EnableLlmReranking { get; set; } = true;
    public int LlmRerankCandidateCount { get; set; } = 50;

    // GraphRAG
    public bool EnableGraphLookup { get; set; }
    public int GraphMaxDepth { get; set; } = 2;
    public bool ExtractEntitiesOnIngest { get; set; }

    // Agentic RAG
    public int DefaultMaxAgenticIterations { get; set; } = 3;

    // BM25
    public bool EnableBm25 { get; set; } = true;
    public double Bm25K1 { get; set; } = 1.2;
    public double Bm25B { get; set; } = 0.75;

    // Hybrid Search (RRF)
    public bool EnableHybridSearch { get; set; } = true;
    public float VectorWeight { get; set; } = 1.0f;
    public float Bm25Weight { get; set; } = 1.0f;

    // MMR Diversity
    public bool EnableMmr { get; set; } = true;
    public double MmrLambda { get; set; } = 0.7;

    // RAG Cache
    public bool EnableRagCache { get; set; } = true;
    public int RagCacheTtlHours { get; set; } = 24;

    // Per-stage timeout budgets (ms)
    public int VectorSearchTimeoutMs { get; set; } = 5000;
    public int Bm25SearchTimeoutMs { get; set; } = 2000;
    public int LlmRerankTimeoutMs { get; set; } = 30000;
    public int QueryRewriteTimeoutMs { get; set; } = 10000;
    public int TotalPipelineTimeoutMs { get; set; } = 55000;

    // Multi-tier Chunking
    public bool EnableSemanticChunking { get; set; } = true;
    public float SemanticBoundaryThreshold { get; set; } = 0.65f;
    public int MinChunkTokens { get; set; } = 64;
    public int MaxChunkTokens { get; set; } = 512;

    // Default pipeline
    public RagPipelineMode DefaultPipelineMode { get; set; } = RagPipelineMode.Naive;
}
