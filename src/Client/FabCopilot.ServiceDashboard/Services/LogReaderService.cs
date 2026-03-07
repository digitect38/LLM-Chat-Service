using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Services;

public sealed partial class LogReaderService
{
    private readonly string _logDir;

    // Pattern: servicename-YYYYMMDD.json (e.g., gateway-20260306.json)
    [GeneratedRegex(@"^(.+?)[-_](\d{8})\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex JsonLogFilePattern();

    public LogReaderService()
    {
        _logDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "logs"));
    }

    internal LogReaderService(string logDirectory)
    {
        _logDir = logDirectory;
    }

    /// <summary>
    /// Get available log file names (without extension).
    /// </summary>
    public string[] GetAvailableLogs()
    {
        if (!Directory.Exists(_logDir)) return [];

        return Directory.GetFiles(_logDir, "*.log")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToArray();
    }

    /// <summary>
    /// Read the last N lines from a service log file.
    /// </summary>
    public string[] ReadLastLines(string serviceName, int lineCount = 100)
    {
        var path = Path.Combine(_logDir, $"{serviceName}.log");
        if (!File.Exists(path)) return [$"Log file not found: {serviceName}.log"];

        try
        {
            // Read with shared access so we don't block the writing process
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
        catch (Exception ex)
        {
            return [$"Error reading log: {ex.Message}"];
        }
    }

    /// <summary>
    /// Read logs for a service — uses docker logs for Docker-based services, file for .NET services.
    /// </summary>
    public string[] ReadServiceLog(string serviceName, int lineCount = 200)
    {
        var svcDef = HealthCheckService.AllServices.FirstOrDefault(s => s.Name == serviceName);
        if (svcDef?.DockerContainer is not null)
            return ReadDockerLogs(svcDef.DockerContainer, lineCount);
        return ReadLastLines(serviceName, lineCount);
    }

    /// <summary>
    /// Read Docker container logs via `docker logs --tail N`.
    /// </summary>
    public string[] ReadDockerLogs(string containerName, int lineCount = 200)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"logs --tail {lineCount} {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            // Docker logs go to stderr for many images
            var combined = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout + "\n" + stderr;
            return combined.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(lineCount).ToArray();
        }
        catch (Exception ex)
        {
            return [$"Error reading docker logs: {ex.Message}"];
        }
    }

    // ─── JSON Log Support ────────────────────────────────────

    /// <summary>
    /// Get available JSON log file names (without extension).
    /// </summary>
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

    /// <summary>
    /// Read and parse the last N lines from a JSON log file with optional filtering.
    /// </summary>
    public List<JsonLogEntry> ReadJsonLogEntries(
        string serviceName, int lineCount = 200,
        string? correlationIdFilter = null, string? levelFilter = null)
    {
        var path = FindJsonLogFile(serviceName);
        if (path is null) return [];

        try
        {
            var rawLines = ReadRawLastLines(path, lineCount * 2); // over-read for filtering
            var entries = new List<JsonLogEntry>();

            foreach (var line in rawLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<JsonLogEntry>(line);
                    if (entry is null) continue;

                    // Apply correlation ID filter
                    if (!string.IsNullOrEmpty(correlationIdFilter) &&
                        !string.Equals(entry.CorrelationId, correlationIdFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Apply level filter
                    if (!string.IsNullOrEmpty(levelFilter) && !string.Equals(levelFilter, "ALL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(entry.DisplayLevel, levelFilter, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    entries.Add(entry);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            return entries.TakeLast(lineCount).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Read correlated logs across all service JSON log files for a given CorrelationId.
    /// </summary>
    public List<JsonLogEntry> ReadCorrelatedLogs(string correlationId, int lineCount = 500)
    {
        if (!Directory.Exists(_logDir)) return [];

        var allEntries = new List<JsonLogEntry>();
        var jsonFiles = Directory.GetFiles(_logDir, "*.json");

        foreach (var file in jsonFiles)
        {
            try
            {
                var rawLines = ReadRawLastLines(file, 2000); // scan recent logs
                foreach (var line in rawLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.Contains(correlationId)) continue; // fast pre-filter

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

        // Sort by timestamp and take most recent
        return allEntries
            .OrderBy(e => e.Timestamp)
            .TakeLast(lineCount)
            .ToList();
    }

    // ─── Search / Index Support ──────────────────────────────

    /// <summary>
    /// Get an index of JSON log files grouped by service, with parsed dates.
    /// </summary>
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

    /// <summary>
    /// Get JSON log files within a date range for a specific service (or all).
    /// </summary>
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
                // If no date in filename, always include
                if (date is null)
                {
                    result.Add(filePath);
                    continue;
                }

                // Prune by date range
                if (start.HasValue && date.Value.Date < start.Value.Date)
                    continue;
                if (end.HasValue && date.Value.Date > end.Value.Date)
                    continue;

                result.Add(filePath);
            }
        }

        return result;
    }

    /// <summary>
    /// Full-text search across JSON logs with filtering and pagination.
    /// </summary>
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

                // Keyword filter (full-text on rendered message + exception)
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

    /// <summary>
    /// Lazily parse a JSON log file line-by-line.
    /// </summary>
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

    /// <summary>
    /// Get distinct service names from available JSON log files.
    /// </summary>
    public string[] GetDistinctServices()
    {
        return GetJsonLogFileIndex().Keys.OrderBy(k => k).ToArray();
    }

    private string? FindJsonLogFile(string serviceName)
    {
        if (!Directory.Exists(_logDir)) return null;

        // Try exact match first, then prefix match
        var candidates = Directory.GetFiles(_logDir, "*.json")
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f) ?? "";
                return name.StartsWith(serviceName, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTime)
            .ToArray();

        return candidates.Length > 0 ? candidates[0] : null;
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
