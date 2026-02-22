using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FabCopilot.Observability.Metrics;

/// <summary>
/// Centralized metrics definitions for Fab Copilot services.
/// Uses System.Diagnostics.Metrics which are auto-collected by OpenTelemetry.
/// </summary>
public static class FabMetrics
{
    public const string MeterName = "FabCopilot";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // ─── RAG Pipeline Metrics ──────────────────────────────────────

    public static readonly Histogram<double> RagVectorSearchDuration =
        Meter.CreateHistogram<double>("rag.vector_search.ms", "ms", "Time spent on vector search");

    public static readonly Histogram<double> RagBm25SearchDuration =
        Meter.CreateHistogram<double>("rag.bm25_search.ms", "ms", "Time spent on BM25 search");

    public static readonly Histogram<double> RagRerankDuration =
        Meter.CreateHistogram<double>("rag.rerank.ms", "ms", "Time spent on reranking");

    public static readonly Histogram<double> RagPipelineDuration =
        Meter.CreateHistogram<double>("rag.pipeline.ms", "ms", "Total RAG pipeline duration");

    public static readonly Counter<long> RagCacheHits =
        Meter.CreateCounter<long>("rag.cache.hit", "hits", "RAG cache hit count");

    public static readonly Counter<long> RagCacheMisses =
        Meter.CreateCounter<long>("rag.cache.miss", "misses", "RAG cache miss count");

    public static readonly Histogram<double> RagQueryRewriteDuration =
        Meter.CreateHistogram<double>("rag.query_rewrite.ms", "ms", "Time spent on query rewriting");

    public static readonly Histogram<double> RagGraphLookupDuration =
        Meter.CreateHistogram<double>("rag.graph_lookup.ms", "ms", "Time spent on graph lookup");

    public static readonly Counter<long> RagStageTimeoutCount =
        Meter.CreateCounter<long>("rag.stage.timeout", "timeouts", "RAG stage timeout count");

    // ─── LLM Metrics ───────────────────────────────────────────────

    public static readonly Histogram<double> LlmFirstTokenDuration =
        Meter.CreateHistogram<double>("llm.first_token.ms", "ms", "Time to first LLM token");

    public static readonly Histogram<double> LlmTotalDuration =
        Meter.CreateHistogram<double>("llm.total.ms", "ms", "Total LLM streaming duration");

    public static readonly Histogram<double> LlmRagRetrievalDuration =
        Meter.CreateHistogram<double>("llm.rag_retrieval.ms", "ms", "Time waiting for RAG retrieval");

    public static readonly Counter<long> LlmRequestCount =
        Meter.CreateCounter<long>("llm.request.count", "requests", "Total LLM requests processed");

    public static readonly Counter<long> LlmGateATriggeredCount =
        Meter.CreateCounter<long>("llm.gate_a.triggered", "triggers", "Gate A (low confidence) trigger count");

    public static readonly Counter<long> LlmGateBTriggeredCount =
        Meter.CreateCounter<long>("llm.gate_b.triggered", "triggers", "Gate B (no citation) trigger count");

    public static readonly Counter<long> LlmGateCTriggeredCount =
        Meter.CreateCounter<long>("llm.gate_c.triggered", "triggers", "Gate C (response quality) trigger count");

    // ─── Timeout Telemetry (v3.3 §11.1) ─────────────────────────────

    public static readonly Counter<long> TimeoutOccurrenceCount =
        Meter.CreateCounter<long>("timeout.occurrence", "timeouts", "Total timeout occurrences");

    public static readonly Counter<long> TimeoutExtendCount =
        Meter.CreateCounter<long>("timeout.extend", "extends", "User chose to extend timeout");

    public static readonly Counter<long> TimeoutCancelCount =
        Meter.CreateCounter<long>("timeout.cancel", "cancels", "User chose to cancel on timeout");

    public static readonly Counter<long> TimeoutSimplifyCount =
        Meter.CreateCounter<long>("timeout.simplify", "simplifies", "User chose to simplify query on timeout");

    // ─── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Creates and starts a Stopwatch. Use with RecordElapsed to measure durations.
    /// </summary>
    public static Stopwatch StartTimer() => Stopwatch.StartNew();

    /// <summary>
    /// Records elapsed milliseconds on a histogram and returns the elapsed value.
    /// </summary>
    public static double RecordElapsed(Stopwatch sw, Histogram<double> histogram, params KeyValuePair<string, object?>[] tags)
    {
        sw.Stop();
        var ms = sw.Elapsed.TotalMilliseconds;
        histogram.Record(ms, tags);
        return ms;
    }
}
