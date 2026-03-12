using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FabCopilot.WebClient.Models;

namespace FabCopilot.WebClient.Services;

public sealed partial class LogReaderService
{
    private readonly string _logDir;

    [GeneratedRegex(@"^(.+?)[-_](\d{8})\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex JsonLogFilePattern();

    public LogReaderService(IConfiguration? configuration = null)
    {
        var configured = configuration?["Logging:LogDirectory"];
        _logDir = !string.IsNullOrEmpty(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "logs"));
    }

    internal LogReaderService(string logDirectory)
    {
        _logDir = logDirectory;
    }

    // ─── JSON Log Support ────────────────────────────────────

    public string[] GetAvailableJsonLogs()
    {
        if (!Directory.Exists(_logDir)) return [];

        return Directory.GetFiles(_logDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToArray();
    }

    public List<JsonLogEntry> ReadCorrelatedLogs(string correlationId, int lineCount = 500)
    {
        if (!Directory.Exists(_logDir)) return [];

        var allEntries = new List<JsonLogEntry>();
        var jsonFiles = Directory.GetFiles(_logDir, "*.json");

        foreach (var file in jsonFiles)
        {
            try
            {
                var rawLines = ReadRawLastLines(file, 2000);
                foreach (var line in rawLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.Contains(correlationId)) continue;

                    try
                    {
                        var entry = JsonSerializer.Deserialize<JsonLogEntry>(line);
                        if (entry is not null &&
                            string.Equals(entry.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                        {
                            allEntries.Add(entry);
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch { }
        }

        return allEntries
            .OrderBy(e => e.Timestamp)
            .TakeLast(lineCount)
            .ToList();
    }

    // ─── Search / Index Support ──────────────────────────────

    public Dictionary<string, List<(string FilePath, DateTime? Date)>> GetJsonLogFileIndex()
    {
        var index = new Dictionary<string, List<(string, DateTime?)>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_logDir)) return index;

        foreach (var file in Directory.GetFiles(_logDir, "*.json"))
        {
            var fileName = Path.GetFileName(file);
            var match = JsonLogFilePattern().Match(fileName);

            string service;
            DateTime? date = null;

            if (match.Success)
            {
                service = match.Groups[1].Value;
                if (DateTime.TryParseExact(match.Groups[2].Value, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    date = parsedDate;
                }
            }
            else
            {
                service = Path.GetFileNameWithoutExtension(file) ?? "unknown";
            }

            if (!index.ContainsKey(service))
                index[service] = [];
            index[service].Add((file, date));
        }

        return index;
    }

    public List<string> GetFilesInDateRange(string? service, DateTime? start, DateTime? end)
    {
        var index = GetJsonLogFileIndex();
        var result = new List<string>();

        foreach (var (svc, files) in index)
        {
            if (service is not null && !string.Equals(svc, service, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var (filePath, date) in files)
            {
                if (date is null)
                {
                    result.Add(filePath);
                    continue;
                }

                if (start.HasValue && date.Value.Date < start.Value.Date)
                    continue;
                if (end.HasValue && date.Value.Date > end.Value.Date)
                    continue;

                result.Add(filePath);
            }
        }

        return result;
    }

    public LogSearchResult SearchJsonLogs(LogSearchQuery query)
    {
        var sw = Stopwatch.StartNew();
        var files = GetFilesInDateRange(query.ServiceName, query.StartTime, query.EndTime);
        var matchedEntries = new List<JsonLogEntry>();
        const int maxScan = 10_000;

        foreach (var file in files)
        {
            if (matchedEntries.Count >= maxScan) break;

            foreach (var entry in StreamJsonLogFile(file))
            {
                if (matchedEntries.Count >= maxScan) break;

                // Time range filter (precise)
                if (query.StartTime.HasValue || query.EndTime.HasValue)
                {
                    if (DateTimeOffset.TryParse(entry.Timestamp, out var ts))
                    {
                        var local = ts.LocalDateTime;
                        if (query.StartTime.HasValue && local < query.StartTime.Value) continue;
                        if (query.EndTime.HasValue && local > query.EndTime.Value) continue;
                    }
                }

                // Level filter
                if (query.Levels.Count > 0 && !query.Levels.Contains(entry.DisplayLevel))
                    continue;

                // CorrelationId filter
                if (!string.IsNullOrEmpty(query.CorrelationId) &&
                    !string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Keyword filter
                if (!string.IsNullOrEmpty(query.Keyword))
                {
                    var haystack = (entry.RenderedMessage + " " + (entry.Exception ?? "")).AsSpan();
                    if (!haystack.Contains(query.Keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                matchedEntries.Add(entry);
            }
        }

        // Sort descending (newest first)
        matchedEntries.Sort((a, b) =>
        {
            var ta = DateTimeOffset.TryParse(a.Timestamp, out var dtoA) ? dtoA : DateTimeOffset.MinValue;
            var tb = DateTimeOffset.TryParse(b.Timestamp, out var dtoB) ? dtoB : DateTimeOffset.MinValue;
            return tb.CompareTo(ta);
        });

        var totalCount = matchedEntries.Count;
        var paged = matchedEntries
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        sw.Stop();

        return new LogSearchResult
        {
            Entries = paged,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
            SearchDuration = sw.Elapsed
        };
    }

    public IEnumerable<JsonLogEntry> StreamJsonLogFile(string filePath)
    {
        if (!File.Exists(filePath)) yield break;

        FileStream? fs = null;
        StreamReader? reader = null;
        try
        {
            fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            reader = new StreamReader(fs);

            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonLogEntry? entry = null;
                try
                {
                    entry = JsonSerializer.Deserialize<JsonLogEntry>(line);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }

                if (entry is not null)
                    yield return entry;
            }
        }
        finally
        {
            reader?.Dispose();
            fs?.Dispose();
        }
    }

    public string[] GetDistinctServices()
    {
        return GetJsonLogFileIndex().Keys.OrderBy(k => k).ToArray();
    }

    private static string[] ReadRawLastLines(string path, int lineCount)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var allLines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            allLines.Add(line);
        }

        return allLines.Count <= lineCount
            ? allLines.ToArray()
            : allLines.Skip(allLines.Count - lineCount).ToArray();
    }
}
