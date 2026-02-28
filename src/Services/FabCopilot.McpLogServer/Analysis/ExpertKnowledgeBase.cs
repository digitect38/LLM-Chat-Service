using System.Text.Json;
using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// In-memory expert knowledge base with rule management, matching, and verification.
/// Supports CRUD operations, feedback-based confidence updating, and auto-deactivation.
/// </summary>
public sealed class ExpertKnowledgeBase
{
    private readonly Dictionary<string, ExpertRule> _rules = new();
    private readonly object _lock = new();
    private int _nextId = 1;

    /// <summary>Minimum precision required to keep a rule active (after 10+ hits).</summary>
    public double MinPrecisionThreshold { get; set; } = 0.70;

    /// <summary>Minimum hits before auto-deactivation can trigger.</summary>
    public int MinHitsForDeactivation { get; set; } = 10;

    /// <summary>
    /// Adds a new expert rule to the knowledge base.
    /// </summary>
    public ExpertRule AddRule(ExpertRule rule)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(rule.Id))
            {
                rule.Id = $"ER-{_nextId++:D4}";
            }
            else if (_nextId <= int.Parse(rule.Id.Replace("ER-", "").TrimStart('0') is "" ? "0"
                         : rule.Id.Replace("ER-", "").TrimStart('0')))
            {
                _nextId = int.Parse(rule.Id.Replace("ER-", "").TrimStart('0') is ""
                    ? "0" : rule.Id.Replace("ER-", "").TrimStart('0')) + 1;
            }

            rule.CreatedAt = DateTimeOffset.UtcNow;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            _rules[rule.Id] = rule;
            return rule;
        }
    }

    /// <summary>
    /// Updates an existing rule (creates a new version).
    /// </summary>
    public ExpertRule? UpdateRule(string ruleId, Action<ExpertRule> updater)
    {
        lock (_lock)
        {
            if (!_rules.TryGetValue(ruleId, out var rule)) return null;
            updater(rule);
            rule.Version++;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            return rule;
        }
    }

    /// <summary>
    /// Removes a rule from the knowledge base.
    /// </summary>
    public bool RemoveRule(string ruleId)
    {
        lock (_lock)
        {
            return _rules.Remove(ruleId);
        }
    }

    /// <summary>
    /// Gets a rule by ID.
    /// </summary>
    public ExpertRule? GetRule(string ruleId)
    {
        lock (_lock)
        {
            return _rules.GetValueOrDefault(ruleId);
        }
    }

    /// <summary>
    /// Lists all rules, optionally filtered by equipment type and active status.
    /// </summary>
    public IReadOnlyList<ExpertRule> ListRules(string? equipmentType = null, bool? activeOnly = null)
    {
        lock (_lock)
        {
            var query = _rules.Values.AsEnumerable();
            if (equipmentType != null)
                query = query.Where(r => r.EquipmentType.Equals(equipmentType, StringComparison.OrdinalIgnoreCase));
            if (activeOnly.HasValue)
                query = query.Where(r => r.IsActive == activeOnly.Value);
            return query.OrderByDescending(r => r.Confidence).ToList();
        }
    }

    /// <summary>
    /// Finds rules whose triggers match the given sensor values and/or alarm codes.
    /// </summary>
    public List<ExpertRuleMatch> EvaluateRules(
        string equipmentType,
        Dictionary<string, double>? sensorValues = null,
        HashSet<string>? activeAlarms = null,
        string? equipmentModel = null)
    {
        var matches = new List<ExpertRuleMatch>();

        lock (_lock)
        {
            var candidates = _rules.Values
                .Where(r => r.IsActive
                    && r.EquipmentType.Equals(equipmentType, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(r.EquipmentModel)
                        || equipmentModel == null
                        || r.EquipmentModel.Equals(equipmentModel, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var rule in candidates)
            {
                var matchedTriggers = 0;
                var triggerDetails = new List<string>();

                foreach (var trigger in rule.Triggers)
                {
                    if (EvaluateTrigger(trigger, sensorValues, activeAlarms, out var detail))
                    {
                        matchedTriggers++;
                        triggerDetails.Add(detail);
                    }
                }

                if (matchedTriggers > 0 && rule.Triggers.Count > 0)
                {
                    var matchRatio = (double)matchedTriggers / rule.Triggers.Count;
                    matches.Add(new ExpertRuleMatch
                    {
                        Rule = rule,
                        MatchedTriggers = matchedTriggers,
                        TotalTriggers = rule.Triggers.Count,
                        MatchRatio = matchRatio,
                        TriggerDetails = triggerDetails,
                        EffectiveConfidence = rule.Confidence * matchRatio
                    });
                }
            }
        }

        return matches.OrderByDescending(m => m.EffectiveConfidence).ToList();
    }

    /// <summary>
    /// Records a rule hit (triggered) and optionally confirmation feedback.
    /// </summary>
    public void RecordFeedback(string ruleId, bool wasCorrect)
    {
        lock (_lock)
        {
            if (!_rules.TryGetValue(ruleId, out var rule)) return;

            rule.HitCount++;
            if (wasCorrect) rule.ConfirmCount++;

            // Update confidence using exponential moving average
            var alpha = 0.1; // Smoothing factor
            var feedbackValue = wasCorrect ? 1.0 : 0.0;
            rule.Confidence = rule.Confidence * (1 - alpha) + feedbackValue * alpha;

            // Auto-deactivation check
            if (rule.HitCount >= MinHitsForDeactivation && rule.Precision < MinPrecisionThreshold)
            {
                rule.IsActive = false;
            }

            rule.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Returns statistics about the knowledge base.
    /// </summary>
    public KnowledgeBaseStats GetStats()
    {
        lock (_lock)
        {
            var activeRules = _rules.Values.Where(r => r.IsActive).ToList();
            return new KnowledgeBaseStats
            {
                TotalRules = _rules.Count,
                ActiveRules = activeRules.Count,
                InactiveRules = _rules.Count - activeRules.Count,
                AverageConfidence = activeRules.Count > 0
                    ? activeRules.Average(r => r.Confidence) : 0,
                TotalHits = _rules.Values.Sum(r => r.HitCount),
                OverallPrecision = _rules.Values.Sum(r => r.HitCount) > 0
                    ? (double)_rules.Values.Sum(r => r.ConfirmCount) / _rules.Values.Sum(r => r.HitCount) : 0,
                EquipmentTypes = _rules.Values.Select(r => r.EquipmentType).Distinct().ToList()
            };
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private static bool EvaluateTrigger(
        RuleTrigger trigger,
        Dictionary<string, double>? sensorValues,
        HashSet<string>? activeAlarms,
        out string detail)
    {
        detail = "";

        switch (trigger.Type.ToLowerInvariant())
        {
            case "sensor":
                if (sensorValues == null || !sensorValues.TryGetValue(trigger.Source, out var sensorValue))
                    return false;

                var result = trigger.Operator.ToLowerInvariant() switch
                {
                    "gt" => trigger.Value.HasValue && sensorValue > trigger.Value.Value,
                    "lt" => trigger.Value.HasValue && sensorValue < trigger.Value.Value,
                    "eq" => trigger.Value.HasValue && Math.Abs(sensorValue - trigger.Value.Value) < 0.001,
                    "gte" => trigger.Value.HasValue && sensorValue >= trigger.Value.Value,
                    "lte" => trigger.Value.HasValue && sensorValue <= trigger.Value.Value,
                    "between" => trigger.Value.HasValue && trigger.ValueUpper.HasValue
                        && sensorValue >= trigger.Value.Value && sensorValue <= trigger.ValueUpper.Value,
                    _ => false
                };

                if (result)
                    detail = $"{trigger.Source}={sensorValue:F2} {trigger.Operator} {trigger.Value}";
                return result;

            case "alarm":
            case "error_code":
                if (activeAlarms == null) return false;
                var alarmMatch = activeAlarms.Contains(trigger.Source);
                if (alarmMatch)
                    detail = $"Alarm {trigger.Source} active";
                return alarmMatch;

            default:
                return false;
        }
    }
}

/// <summary>
/// Result of matching an expert rule against current conditions.
/// </summary>
public sealed class ExpertRuleMatch
{
    public ExpertRule Rule { get; set; } = null!;
    public int MatchedTriggers { get; set; }
    public int TotalTriggers { get; set; }
    public double MatchRatio { get; set; }
    public List<string> TriggerDetails { get; set; } = [];
    public double EffectiveConfidence { get; set; }
}

/// <summary>
/// Statistics about the expert knowledge base.
/// </summary>
public sealed class KnowledgeBaseStats
{
    public int TotalRules { get; set; }
    public int ActiveRules { get; set; }
    public int InactiveRules { get; set; }
    public double AverageConfidence { get; set; }
    public int TotalHits { get; set; }
    public double OverallPrecision { get; set; }
    public List<string> EquipmentTypes { get; set; } = [];
}
