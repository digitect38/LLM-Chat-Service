using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// Generates predictive alerts from Fusion Engine diagnostic reports.
/// Manages alert lifecycle: generation, deduplication, acknowledgment.
/// </summary>
public sealed class PredictiveAlertGenerator
{
    private readonly List<PredictiveAlert> _alerts = [];
    private readonly Dictionary<string, DateTimeOffset> _suppressedAlerts = new();
    private readonly object _lock = new();
    private int _nextId = 1;

    /// <summary>Minimum hypothesis confidence to generate a Warning alert.</summary>
    public double WarningThreshold { get; set; } = 0.40;

    /// <summary>Minimum hypothesis confidence to generate a Critical alert.</summary>
    public double CriticalThreshold { get; set; } = 0.70;

    /// <summary>Suppression window: don't re-alert for the same issue within this duration.</summary>
    public TimeSpan SuppressionWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Generates alerts from a diagnostic report.
    /// </summary>
    public List<PredictiveAlert> GenerateAlerts(DiagnosticReport report)
    {
        var newAlerts = new List<PredictiveAlert>();

        foreach (var hypothesis in report.Hypotheses)
        {
            var severity = hypothesis.Confidence >= CriticalThreshold ? AlertSeverity.Critical
                : hypothesis.Confidence >= WarningThreshold ? AlertSeverity.Warning
                : AlertSeverity.Info;

            // Skip Info-level alerts (only generate Warning and Critical)
            if (severity == AlertSeverity.Info) continue;

            // Check suppression
            var suppressionKey = $"{report.EquipmentId}:{hypothesis.Id}";
            if (IsSuppressed(suppressionKey)) continue;

            var alert = new PredictiveAlert
            {
                Id = GenerateAlertId(),
                EquipmentId = report.EquipmentId,
                Severity = severity,
                Title = FormatTitle(hypothesis, severity),
                Message = FormatMessage(hypothesis, report),
                Hypothesis = hypothesis,
                Actions = hypothesis.RecommendedActions.ToList(),
                ManualReferences = hypothesis.ManualReferences.ToList(),
                GeneratedAt = DateTimeOffset.UtcNow
            };

            lock (_lock)
            {
                _alerts.Add(alert);
                _suppressedAlerts[suppressionKey] = DateTimeOffset.UtcNow;
            }

            newAlerts.Add(alert);
        }

        return newAlerts;
    }

    /// <summary>
    /// Acknowledges an alert by ID.
    /// </summary>
    public bool Acknowledge(string alertId, string acknowledgedBy)
    {
        lock (_lock)
        {
            var alert = _alerts.Find(a => a.Id == alertId);
            if (alert == null) return false;

            alert.Acknowledged = true;
            alert.AcknowledgedBy = acknowledgedBy;
            alert.AcknowledgedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Gets all active (unacknowledged) alerts, optionally filtered by equipment.
    /// </summary>
    public IReadOnlyList<PredictiveAlert> GetActiveAlerts(string? equipmentId = null)
    {
        lock (_lock)
        {
            var query = _alerts.Where(a => !a.Acknowledged);
            if (equipmentId != null)
                query = query.Where(a => a.EquipmentId == equipmentId);
            return query.OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.GeneratedAt)
                .ToList();
        }
    }

    /// <summary>
    /// Gets alert history (including acknowledged), optionally limited.
    /// </summary>
    public IReadOnlyList<PredictiveAlert> GetAlertHistory(int limit = 100, string? equipmentId = null)
    {
        lock (_lock)
        {
            var query = _alerts.AsEnumerable();
            if (equipmentId != null)
                query = query.Where(a => a.EquipmentId == equipmentId);
            return query.OrderByDescending(a => a.GeneratedAt).Take(limit).ToList();
        }
    }

    /// <summary>
    /// Returns alert counts grouped by severity.
    /// </summary>
    public Dictionary<AlertSeverity, int> GetAlertCounts(string? equipmentId = null)
    {
        lock (_lock)
        {
            var query = _alerts.Where(a => !a.Acknowledged);
            if (equipmentId != null)
                query = query.Where(a => a.EquipmentId == equipmentId);

            return query.GroupBy(a => a.Severity)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private string GenerateAlertId()
    {
        lock (_lock)
        {
            return $"PA-{_nextId++:D6}";
        }
    }

    private bool IsSuppressed(string key)
    {
        lock (_lock)
        {
            if (!_suppressedAlerts.TryGetValue(key, out var lastAlertTime))
                return false;

            if (DateTimeOffset.UtcNow - lastAlertTime < SuppressionWindow)
                return true;

            _suppressedAlerts.Remove(key);
            return false;
        }
    }

    private static string FormatTitle(DiagnosticHypothesis hypothesis, AlertSeverity severity)
    {
        var prefix = severity == AlertSeverity.Critical ? "CRITICAL" : "WARNING";
        return $"[{prefix}] {hypothesis.Hypothesis}";
    }

    private static string FormatMessage(DiagnosticHypothesis hypothesis, DiagnosticReport report)
    {
        var parts = new List<string>
        {
            $"Equipment: {report.EquipmentId}",
            $"Confidence: {hypothesis.Confidence:P0}",
            $"Rank: #{hypothesis.Rank} of {report.Hypotheses.Count} hypotheses"
        };

        // Add evidence summary
        if (hypothesis.Tier1Evidence.Count > 0)
            parts.Add($"Statistical evidence: {hypothesis.Tier1Evidence.Count} indicators");
        if (hypothesis.Tier2Evidence.Count > 0)
            parts.Add($"Expert rules matched: {hypothesis.Tier2Evidence.Count}");
        if (hypothesis.Tier3Evidence.Count > 0)
            parts.Add($"Document references: {hypothesis.Tier3Evidence.Count}");

        // Add top actions
        if (hypothesis.RecommendedActions.Count > 0)
        {
            parts.Add("Recommended actions:");
            foreach (var action in hypothesis.RecommendedActions.Take(3))
                parts.Add($"  - {action}");
        }

        return string.Join("\n", parts);
    }
}
