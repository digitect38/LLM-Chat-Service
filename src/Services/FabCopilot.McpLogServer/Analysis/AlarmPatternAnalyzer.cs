using FabCopilot.Contracts.Interfaces;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// Alarm/event pattern analysis engine.
/// Detects frequent alarms, cascading patterns, time-based distributions,
/// and cross-correlations with sensor data.
/// </summary>
public static class AlarmPatternAnalyzer
{
    /// <summary>
    /// Computes Top-N most frequent alarm codes.
    /// </summary>
    public static List<(string AlarmCode, int Count, double Percentage)> TopNFrequent(
        IReadOnlyList<AlarmEvent> alarms, int topN = 10)
    {
        if (alarms.Count == 0) return [];

        return alarms
            .GroupBy(a => a.AlarmCode)
            .Select(g => (AlarmCode: g.Key, Count: g.Count(), Percentage: (double)g.Count() / alarms.Count))
            .OrderByDescending(x => x.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// Analyzes alarm distribution by hour of day.
    /// Returns counts for each hour (0-23).
    /// </summary>
    public static Dictionary<int, int> HourlyDistribution(IReadOnlyList<AlarmEvent> alarms)
    {
        var dist = new Dictionary<int, int>();
        for (var h = 0; h < 24; h++) dist[h] = 0;

        foreach (var alarm in alarms)
        {
            var hour = alarm.Timestamp.Hour;
            dist[hour]++;
        }

        return dist;
    }

    /// <summary>
    /// Analyzes alarm distribution by day of week.
    /// Returns counts for each day (0=Sunday through 6=Saturday).
    /// </summary>
    public static Dictionary<DayOfWeek, int> DayOfWeekDistribution(IReadOnlyList<AlarmEvent> alarms)
    {
        var dist = new Dictionary<DayOfWeek, int>();
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) dist[day] = 0;

        foreach (var alarm in alarms)
        {
            dist[alarm.Timestamp.DayOfWeek]++;
        }

        return dist;
    }

    /// <summary>
    /// Detects cascading alarm patterns (A → B → C within a time window).
    /// Returns sequences of alarm codes that frequently occur together.
    /// </summary>
    public static List<AlarmSequence> DetectCascadingPatterns(
        IReadOnlyList<AlarmEvent> alarms,
        TimeSpan maxGap = default,
        int minOccurrences = 2)
    {
        if (maxGap == default) maxGap = TimeSpan.FromMinutes(10);

        var sorted = alarms.OrderBy(a => a.Timestamp).ToList();
        var sequenceMap = new Dictionary<string, int>();

        // Find consecutive alarm sequences within the time window
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var sequence = new List<string> { sorted[i].AlarmCode };

            for (var j = i + 1; j < sorted.Count && j < i + 5; j++) // max sequence length = 5
            {
                if (sorted[j].Timestamp - sorted[j - 1].Timestamp > maxGap)
                    break;

                sequence.Add(sorted[j].AlarmCode);

                var key = string.Join(" → ", sequence);
                sequenceMap.TryGetValue(key, out var count);
                sequenceMap[key] = count + 1;
            }
        }

        return sequenceMap
            .Where(kv => kv.Value >= minOccurrences)
            .Select(kv => new AlarmSequence
            {
                Pattern = kv.Key,
                AlarmCodes = kv.Key.Split(" → ").ToList(),
                Occurrences = kv.Value
            })
            .OrderByDescending(s => s.Occurrences)
            .ToList();
    }

    /// <summary>
    /// Computes Mean Time Between Failures (MTBF) for each alarm code.
    /// </summary>
    public static Dictionary<string, TimeSpan> ComputeMtbf(IReadOnlyList<AlarmEvent> alarms)
    {
        var result = new Dictionary<string, TimeSpan>();

        var grouped = alarms
            .GroupBy(a => a.AlarmCode)
            .Where(g => g.Count() >= 2);

        foreach (var group in grouped)
        {
            var sorted = group.OrderBy(a => a.Timestamp).ToList();
            var intervals = new List<double>();

            for (var i = 1; i < sorted.Count; i++)
            {
                intervals.Add((sorted[i].Timestamp - sorted[i - 1].Timestamp).TotalHours);
            }

            if (intervals.Count > 0)
            {
                result[group.Key] = TimeSpan.FromHours(intervals.Average());
            }
        }

        return result;
    }

    /// <summary>
    /// Computes Mean Time To Repair (MTTR) for each alarm code.
    /// Only applies to alarms that have a ClearedAt timestamp.
    /// </summary>
    public static Dictionary<string, TimeSpan> ComputeMttr(IReadOnlyList<AlarmEvent> alarms)
    {
        var result = new Dictionary<string, TimeSpan>();

        var grouped = alarms
            .Where(a => a.ClearedAt.HasValue)
            .GroupBy(a => a.AlarmCode);

        foreach (var group in grouped)
        {
            var durations = group
                .Select(a => (a.ClearedAt!.Value - a.Timestamp).TotalMinutes)
                .ToList();

            if (durations.Count > 0)
            {
                result[group.Key] = TimeSpan.FromMinutes(durations.Average());
            }
        }

        return result;
    }

    /// <summary>
    /// Finds alarm codes that frequently co-occur within a time window.
    /// Returns pairs of alarm codes with their co-occurrence count.
    /// </summary>
    public static List<(string AlarmA, string AlarmB, int CoOccurrences)> FindCoOccurringAlarms(
        IReadOnlyList<AlarmEvent> alarms,
        TimeSpan window = default)
    {
        if (window == default) window = TimeSpan.FromMinutes(30);

        var sorted = alarms.OrderBy(a => a.Timestamp).ToList();
        var coOccurrences = new Dictionary<string, int>();

        for (var i = 0; i < sorted.Count; i++)
        {
            for (var j = i + 1; j < sorted.Count; j++)
            {
                if (sorted[j].Timestamp - sorted[i].Timestamp > window)
                    break;

                if (sorted[i].AlarmCode == sorted[j].AlarmCode)
                    continue;

                var pair = string.Compare(sorted[i].AlarmCode, sorted[j].AlarmCode, StringComparison.Ordinal) < 0
                    ? $"{sorted[i].AlarmCode}|{sorted[j].AlarmCode}"
                    : $"{sorted[j].AlarmCode}|{sorted[i].AlarmCode}";

                coOccurrences.TryGetValue(pair, out var count);
                coOccurrences[pair] = count + 1;
            }
        }

        return coOccurrences
            .Select(kv =>
            {
                var parts = kv.Key.Split('|');
                return (AlarmA: parts[0], AlarmB: parts[1], CoOccurrences: kv.Value);
            })
            .OrderByDescending(x => x.CoOccurrences)
            .ToList();
    }
}

/// <summary>
/// A detected cascading alarm sequence pattern.
/// </summary>
public sealed class AlarmSequence
{
    public string Pattern { get; set; } = "";
    public List<string> AlarmCodes { get; set; } = [];
    public int Occurrences { get; set; }
}
