using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Services;

public sealed class LogAnalyzerService
{
    private readonly LogReaderService _logReader;

    public LogAnalyzerService(LogReaderService logReader)
    {
        _logReader = logReader;
    }

    /// <summary>
    /// Get error/warning rates bucketed by time interval per service.
    /// </summary>
    public List<ErrorRateBucket> GetErrorRates(DateTime start, DateTime end, TimeSpan bucketSize)
    {
        var files = _logReader.GetFilesInDateRange(null, start, end);
        var buckets = new Dictionary<(DateTime Bucket, string Service), ErrorRateBucket>();

        foreach (var file in files)
        {
            foreach (var entry in _logReader.StreamJsonLogFile(file))
            {
                if (!DateTimeOffset.TryParse(entry.Timestamp, out var ts)) continue;
                var local = ts.LocalDateTime;
                if (local < start || local > end) continue;

                var level = entry.DisplayLevel;
                if (level != "ERR" && level != "WRN") continue;

                var service = entry.ServiceName ?? "Unknown";
                var bucketStart = new DateTime(
                    local.Ticks - (local.Ticks % bucketSize.Ticks), local.Kind);

                var key = (bucketStart, service);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new ErrorRateBucket { BucketStart = bucketStart, ServiceName = service };
                    buckets[key] = bucket;
                }

                if (level == "ERR") bucket.ErrorCount++;
                else bucket.WarningCount++;
            }
        }

        return buckets.Values
            .OrderBy(b => b.BucketStart)
            .ThenBy(b => b.ServiceName)
            .ToList();
    }

    /// <summary>
    /// Get top error message templates grouped by count.
    /// </summary>
    public List<ErrorTemplateGroup> GetTopErrors(DateTime start, DateTime end, string? service = null, int top = 20)
    {
        var files = _logReader.GetFilesInDateRange(service, start, end);
        var groups = new Dictionary<string, ErrorTemplateGroup>();

        foreach (var file in files)
        {
            foreach (var entry in _logReader.StreamJsonLogFile(file))
            {
                if (entry.DisplayLevel != "ERR") continue;
                if (!DateTimeOffset.TryParse(entry.Timestamp, out var ts)) continue;

                var local = ts.LocalDateTime;
                if (local < start || local > end) continue;

                var template = entry.MessageTemplate ?? "(no template)";
                var svc = entry.ServiceName ?? "Unknown";
                var key = $"{svc}::{template}";

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new ErrorTemplateGroup
                    {
                        MessageTemplate = template,
                        ServiceName = svc,
                        FirstSeen = local,
                        LastSeen = local
                    };
                    groups[key] = group;
                }

                group.Count++;
                if (local < group.FirstSeen) group.FirstSeen = local;
                if (local > group.LastSeen) group.LastSeen = local;
            }
        }

        return groups.Values
            .OrderByDescending(g => g.Count)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Build a cross-service request flow for a given CorrelationId.
    /// </summary>
    public RequestFlow GetRequestFlow(string correlationId)
    {
        var entries = _logReader.ReadCorrelatedLogs(correlationId, 1000);
        var flow = new RequestFlow { CorrelationId = correlationId };

        FlowEvent? prev = null;
        foreach (var entry in entries)
        {
            if (!DateTimeOffset.TryParse(entry.Timestamp, out var ts)) continue;

            var evt = new FlowEvent
            {
                Timestamp = ts.LocalDateTime,
                ServiceName = entry.ServiceName ?? "Unknown",
                Message = entry.RenderedMessage,
                Level = entry.DisplayLevel,
                GapFromPrevious = prev is not null
                    ? ts.LocalDateTime - prev.Timestamp
                    : TimeSpan.Zero
            };

            flow.Events.Add(evt);
            prev = evt;
        }

        if (flow.Events.Count >= 2)
        {
            flow.TotalDuration = flow.Events[^1].Timestamp - flow.Events[0].Timestamp;
        }

        return flow;
    }

    /// <summary>
    /// Get per-service health summary (entry counts by level).
    /// </summary>
    public List<ServiceHealthBucket> GetServiceHealth(DateTime start, DateTime end)
    {
        var files = _logReader.GetFilesInDateRange(null, start, end);
        var buckets = new Dictionary<string, ServiceHealthBucket>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            foreach (var entry in _logReader.StreamJsonLogFile(file))
            {
                if (!DateTimeOffset.TryParse(entry.Timestamp, out var ts)) continue;
                var local = ts.LocalDateTime;
                if (local < start || local > end) continue;

                var service = entry.ServiceName ?? "Unknown";
                if (!buckets.TryGetValue(service, out var bucket))
                {
                    bucket = new ServiceHealthBucket { ServiceName = service };
                    buckets[service] = bucket;
                }

                bucket.TotalCount++;
                switch (entry.DisplayLevel)
                {
                    case "ERR": bucket.ErrorCount++; break;
                    case "WRN": bucket.WarningCount++; break;
                    case "INF": bucket.InfoCount++; break;
                    case "DBG": bucket.DebugCount++; break;
                }
            }
        }

        return buckets.Values.OrderBy(b => b.ServiceName).ToList();
    }
}
