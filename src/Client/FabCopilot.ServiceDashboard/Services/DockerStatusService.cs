using System.Collections.Concurrent;
using System.Text.Json;
using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Services;

public sealed class DockerStatusService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<DockerStatusService> _logger;
    private readonly ConcurrentDictionary<string, DockerContainerInfo> _containers = new();
    private bool _dockerAvailable = true;

    public event Action? OnStatusChanged;

    public DockerStatusService(ILogger<DockerStatusService> logger)
    {
        _logger = logger;
    }

    public bool IsDockerAvailable => _dockerAvailable;

    public IReadOnlyDictionary<string, DockerContainerInfo> GetAllContainers() => _containers;

    public DockerContainerInfo? GetContainer(string name)
    {
        // Try exact match first, then partial match on container name
        if (_containers.TryGetValue(name, out var info)) return info;
        return _containers.Values.FirstOrDefault(c =>
            c.Names.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DockerStatusService started — checking every {Interval}s", CheckInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            OnStatusChanged?.Invoke();
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps -a --format \"{{json .}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                _dockerAvailable = false;
                return;
            }

            _dockerAvailable = true;
            _containers.Clear();

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var names = root.GetProperty("Names").GetString() ?? "";
                    var container = new DockerContainerInfo
                    {
                        Id = root.GetProperty("ID").GetString() ?? "",
                        Names = names,
                        Image = root.GetProperty("Image").GetString() ?? "",
                        Status = root.GetProperty("Status").GetString() ?? "",
                        Ports = root.GetProperty("Ports").GetString() ?? "",
                        State = root.GetProperty("State").GetString() ?? "",
                        CreatedAt = root.GetProperty("CreatedAt").GetString() ?? ""
                    };

                    _containers[names] = container;
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dockerAvailable = false;
            _logger.LogWarning("Docker not available: {Message}", ex.Message);
        }
    }
}
