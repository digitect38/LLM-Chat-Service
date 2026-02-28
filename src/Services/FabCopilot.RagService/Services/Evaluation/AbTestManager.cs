using System.Collections.Concurrent;
using FabCopilot.Contracts.Enums;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services.Evaluation;

/// <summary>
/// A/B testing framework for RAG search pipeline comparison.
/// Routes a configurable percentage of queries to variant pipeline(s)
/// and collects per-pipeline metrics for comparison.
/// </summary>
public sealed class AbTestManager
{
    private readonly ILogger<AbTestManager> _logger;
    private readonly ConcurrentDictionary<string, AbTestExperiment> _activeExperiments = new();
    private readonly ConcurrentDictionary<string, AbTestResults> _results = new();

    public AbTestManager(ILogger<AbTestManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates and starts an A/B test experiment.
    /// </summary>
    public AbTestExperiment CreateExperiment(
        string experimentId,
        string description,
        RagPipelineMode controlPipeline,
        RagPipelineMode variantPipeline,
        double variantPercentage = 0.2)
    {
        var experiment = new AbTestExperiment
        {
            ExperimentId = experimentId,
            Description = description,
            ControlPipeline = controlPipeline,
            VariantPipeline = variantPipeline,
            VariantPercentage = Math.Clamp(variantPercentage, 0.01, 0.99),
            StartedAt = DateTime.UtcNow,
            IsActive = true
        };

        _activeExperiments[experimentId] = experiment;
        _results[experimentId] = new AbTestResults { ExperimentId = experimentId };

        _logger.LogInformation(
            "A/B test created: {ExperimentId} — Control={Control}, Variant={Variant}, Split={Pct:P0}",
            experimentId, controlPipeline, variantPipeline, variantPercentage);

        return experiment;
    }

    /// <summary>
    /// Gets the pipeline mode for a query, routing based on A/B test split.
    /// Returns null if no active experiments.
    /// </summary>
    public (RagPipelineMode Pipeline, string? ExperimentId, string Group)? RouteQuery(
        string conversationId, RagPipelineMode defaultPipeline)
    {
        foreach (var (_, experiment) in _activeExperiments)
        {
            if (!experiment.IsActive) continue;
            if (experiment.ControlPipeline != defaultPipeline) continue;

            // Use hash of conversationId for deterministic, consistent routing
            var hash = Math.Abs(conversationId.GetHashCode());
            var bucket = (hash % 100) / 100.0;
            var isVariant = bucket < experiment.VariantPercentage;

            return (
                isVariant ? experiment.VariantPipeline : experiment.ControlPipeline,
                experiment.ExperimentId,
                isVariant ? "variant" : "control"
            );
        }

        return null;
    }

    /// <summary>
    /// Records a query result for an active experiment.
    /// </summary>
    public void RecordResult(
        string experimentId,
        string group,
        string conversationId,
        double searchDurationMs,
        int resultCount,
        double maxScore,
        bool userFeedbackPositive = true)
    {
        if (!_results.TryGetValue(experimentId, out var results)) return;

        var entry = new AbTestQueryResult
        {
            ConversationId = conversationId,
            Group = group,
            SearchDurationMs = searchDurationMs,
            ResultCount = resultCount,
            MaxScore = maxScore,
            UserFeedbackPositive = userFeedbackPositive,
            Timestamp = DateTime.UtcNow
        };

        results.QueryResults.Add(entry);
    }

    /// <summary>
    /// Records user feedback for an A/B test query.
    /// </summary>
    public void RecordFeedback(string experimentId, string conversationId, bool isPositive)
    {
        if (!_results.TryGetValue(experimentId, out var results)) return;

        var result = results.QueryResults
            .FirstOrDefault(r => r.ConversationId == conversationId);

        if (result != null)
        {
            result.UserFeedbackPositive = isPositive;
        }
    }

    /// <summary>
    /// Stops an experiment and returns the aggregated results.
    /// </summary>
    public AbTestReport? StopExperiment(string experimentId)
    {
        if (!_activeExperiments.TryGetValue(experimentId, out var experiment))
            return null;

        experiment.IsActive = false;
        experiment.EndedAt = DateTime.UtcNow;

        if (!_results.TryGetValue(experimentId, out var results))
            return null;

        var report = GenerateReport(experiment, results);

        _logger.LogInformation(
            "A/B test stopped: {ExperimentId} — Control queries={ControlCount}, Variant queries={VariantCount}",
            experimentId, report.ControlMetrics.QueryCount, report.VariantMetrics.QueryCount);

        return report;
    }

    /// <summary>
    /// Gets a snapshot of current experiment results.
    /// </summary>
    public AbTestReport? GetReport(string experimentId)
    {
        if (!_activeExperiments.TryGetValue(experimentId, out var experiment))
            return null;
        if (!_results.TryGetValue(experimentId, out var results))
            return null;

        return GenerateReport(experiment, results);
    }

    /// <summary>
    /// Lists all active experiments.
    /// </summary>
    public List<AbTestExperiment> GetActiveExperiments()
        => _activeExperiments.Values.Where(e => e.IsActive).ToList();

    private static AbTestReport GenerateReport(AbTestExperiment experiment, AbTestResults results)
    {
        var controlResults = results.QueryResults.Where(r => r.Group == "control").ToList();
        var variantResults = results.QueryResults.Where(r => r.Group == "variant").ToList();

        return new AbTestReport
        {
            ExperimentId = experiment.ExperimentId,
            Description = experiment.Description,
            ControlPipeline = experiment.ControlPipeline.ToString(),
            VariantPipeline = experiment.VariantPipeline.ToString(),
            StartedAt = experiment.StartedAt,
            EndedAt = experiment.EndedAt,
            IsActive = experiment.IsActive,
            ControlMetrics = ComputeGroupMetrics(controlResults),
            VariantMetrics = ComputeGroupMetrics(variantResults)
        };
    }

    private static AbTestGroupMetrics ComputeGroupMetrics(List<AbTestQueryResult> results)
    {
        if (results.Count == 0)
            return new AbTestGroupMetrics();

        return new AbTestGroupMetrics
        {
            QueryCount = results.Count,
            AvgSearchDurationMs = results.Average(r => r.SearchDurationMs),
            P50SearchDurationMs = Percentile(results.Select(r => r.SearchDurationMs).ToList(), 0.50),
            P95SearchDurationMs = Percentile(results.Select(r => r.SearchDurationMs).ToList(), 0.95),
            AvgResultCount = results.Average(r => r.ResultCount),
            AvgMaxScore = results.Average(r => r.MaxScore),
            PositiveFeedbackRate = results.Count(r => r.UserFeedbackPositive) / (double)results.Count
        };
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var index = (int)Math.Ceiling(percentile * values.Count) - 1;
        return values[Math.Max(0, Math.Min(index, values.Count - 1))];
    }
}

public sealed class AbTestExperiment
{
    public string ExperimentId { get; set; } = "";
    public string Description { get; set; } = "";
    public RagPipelineMode ControlPipeline { get; set; }
    public RagPipelineMode VariantPipeline { get; set; }
    public double VariantPercentage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AbTestResults
{
    public string ExperimentId { get; set; } = "";
    public ConcurrentBag<AbTestQueryResult> QueryResults { get; } = [];
}

public sealed class AbTestQueryResult
{
    public string ConversationId { get; set; } = "";
    public string Group { get; set; } = "";
    public double SearchDurationMs { get; set; }
    public int ResultCount { get; set; }
    public double MaxScore { get; set; }
    public bool UserFeedbackPositive { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class AbTestReport
{
    public string ExperimentId { get; set; } = "";
    public string Description { get; set; } = "";
    public string ControlPipeline { get; set; } = "";
    public string VariantPipeline { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; }
    public AbTestGroupMetrics ControlMetrics { get; set; } = new();
    public AbTestGroupMetrics VariantMetrics { get; set; } = new();
}

public sealed class AbTestGroupMetrics
{
    public int QueryCount { get; set; }
    public double AvgSearchDurationMs { get; set; }
    public double P50SearchDurationMs { get; set; }
    public double P95SearchDurationMs { get; set; }
    public double AvgResultCount { get; set; }
    public double AvgMaxScore { get; set; }
    public double PositiveFeedbackRate { get; set; }
}
