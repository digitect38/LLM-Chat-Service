using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Services;

public sealed class HealthCheckService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<HealthCheckService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, ServiceStatus> _statuses = new();

    public event Action? OnStatusChanged;

    public static readonly List<ServiceDefinition> AllServices =
    [
        // .NET Services
        new()
        {
            Name = "ChatGateway", DisplayName = "Chat Gateway",
            Category = ServiceCategory.DotNet, HealthCheck = HealthCheckMethod.HttpGet,
            Port = 5000, HealthPath = "/health",
            ProjectPath = @"src\Services\FabCopilot.ChatGateway", ProcessName = "FabCopilot.ChatGateway"
        },
        new()
        {
            Name = "WebClient", DisplayName = "Web Client",
            Category = ServiceCategory.DotNet, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 5010,
            ProjectPath = @"src\Client\FabCopilot.WebClient", ProcessName = "FabCopilot.WebClient"
        },
        new()
        {
            Name = "LlmService", DisplayName = "LLM Service",
            Category = ServiceCategory.DotNet, HealthCheck = HealthCheckMethod.ProcessCheck,
            ProjectPath = @"src\Services\FabCopilot.LlmService", ProcessName = "FabCopilot.LlmService"
        },
        new()
        {
            Name = "KnowledgeService", DisplayName = "Knowledge Service",
            Category = ServiceCategory.DotNet, HealthCheck = HealthCheckMethod.HttpGet,
            Port = 5002, HealthPath = "/health",
            ProjectPath = @"src\Services\FabCopilot.KnowledgeService", ProcessName = "FabCopilot.KnowledgeService"
        },
        new()
        {
            Name = "RagService", DisplayName = "RAG Service",
            Category = ServiceCategory.DotNet, HealthCheck = HealthCheckMethod.ProcessCheck,
            ProjectPath = @"src\Services\FabCopilot.RagService", ProcessName = "FabCopilot.RagService"
        },

        // Infrastructure (Docker)
        new()
        {
            Name = "NATS", DisplayName = "NATS",
            Category = ServiceCategory.Infrastructure, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 4222, DockerContainer = "infra-nats-1", DockerComposeService = "nats"
        },
        new()
        {
            Name = "Redis", DisplayName = "Redis",
            Category = ServiceCategory.Infrastructure, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 6379, DockerContainer = "infra-redis-1", DockerComposeService = "redis"
        },
        new()
        {
            Name = "Qdrant", DisplayName = "Qdrant",
            Category = ServiceCategory.Infrastructure, HealthCheck = HealthCheckMethod.HttpGet,
            Port = 6333, HealthPath = "/healthz", DockerContainer = "infra-qdrant-1", DockerComposeService = "qdrant"
        },
        new()
        {
            Name = "Ollama", DisplayName = "Ollama",
            Category = ServiceCategory.Infrastructure, HealthCheck = HealthCheckMethod.HttpGet,
            Port = 11434, HealthPath = "/api/tags", DockerContainer = "infra-ollama-1", DockerComposeService = "ollama"
        },
        // STT Engines (Docker)
        new()
        {
            Name = "Whisper", DisplayName = "Whisper (faster-whisper)",
            Category = ServiceCategory.SttEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8300, DockerContainer = "fab-whisper", DockerComposeService = "whisper", DockerProfile = "whisper"
        },

        // TTS Engines (Docker, profile "tts")
        new()
        {
            Name = "EdgeTts", DisplayName = "Edge TTS",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 5050, DockerContainer = "fab-edge-tts", DockerComposeService = "edge-tts", DockerProfile = "tts"
        },
        new()
        {
            Name = "XTTS", DisplayName = "XTTS",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8400, DockerContainer = "fab-tts", DockerComposeService = "tts", DockerProfile = "tts"
        },
        new()
        {
            Name = "Kokoro", DisplayName = "Kokoro",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8401, DockerContainer = "fab-kokoro", DockerComposeService = "kokoro-tts", DockerProfile = "tts"
        },
        new()
        {
            Name = "CosyVoice", DisplayName = "CosyVoice",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8402, DockerContainer = "fab-cosyvoice", DockerComposeService = "cosyvoice", DockerProfile = "tts"
        },
        new()
        {
            Name = "FishSpeech", DisplayName = "Fish Speech",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8403, DockerContainer = "fab-fish-speech", DockerComposeService = "fish-speech", DockerProfile = "tts"
        },
        new()
        {
            Name = "Chatterbox", DisplayName = "Chatterbox",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8404, DockerContainer = "fab-chatterbox", DockerComposeService = "chatterbox", DockerProfile = "tts"
        },
        new()
        {
            Name = "Bark", DisplayName = "Bark",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8405, DockerContainer = "fab-bark", DockerComposeService = "bark-tts", DockerProfile = "tts"
        },
        new()
        {
            Name = "Piper", DisplayName = "Piper",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8406, DockerContainer = "fab-piper", DockerComposeService = "piper-tts", DockerProfile = "tts"
        },
        new()
        {
            Name = "Orpheus", DisplayName = "Orpheus",
            Category = ServiceCategory.TtsEngine, HealthCheck = HealthCheckMethod.TcpConnect,
            Port = 8407, DockerContainer = "fab-orpheus", DockerComposeService = "orpheus-tts", DockerProfile = "tts"
        },
    ];

    public HealthCheckService(ILogger<HealthCheckService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = HttpTimeout };

        foreach (var svc in AllServices)
        {
            _statuses[svc.Name] = new ServiceStatus
            {
                ServiceName = svc.Name,
                State = ServiceState.Unknown,
                LastChecked = DateTimeOffset.UtcNow
            };
        }
    }

    public ServiceStatus GetStatus(string serviceName)
    {
        return _statuses.GetValueOrDefault(serviceName)
            ?? new ServiceStatus { ServiceName = serviceName, State = ServiceState.Unknown };
    }

    public IReadOnlyDictionary<string, ServiceStatus> GetAllStatuses() => _statuses;

    public string ActiveTtsProvider { get; private set; } = "?";
    public string ActiveTtsVoice { get; private set; } = "";

    private static readonly string[] _gatewayConfigPaths =
    [
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "src", "Services", "FabCopilot.ChatGateway", "appsettings.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "src", "Services", "FabCopilot.ChatGateway", "appsettings.json"),
    ];

    private void ReadActiveTtsProvider()
    {
        foreach (var rel in _gatewayConfigPaths)
        {
            var path = Path.GetFullPath(rel);
            if (!File.Exists(path)) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("Tts", out var tts))
                {
                    if (tts.TryGetProperty("Provider", out var p)) ActiveTtsProvider = p.GetString() ?? "?";
                    if (tts.TryGetProperty("Voice", out var v)) ActiveTtsVoice = v.GetString() ?? "";
                }
                return;
            }
            catch { /* ignore read errors */ }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HealthCheckService started — checking every {Interval}s", CheckInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var tasks = AllServices.Select(svc => CheckServiceAsync(svc, stoppingToken));
            await Task.WhenAll(tasks);
            ReadActiveTtsProvider();

            OnStatusChanged?.Invoke();

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckServiceAsync(ServiceDefinition svc, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        ServiceState state;
        string? error = null;

        try
        {
            state = svc.HealthCheck switch
            {
                HealthCheckMethod.HttpGet => await CheckHttpAsync(svc, ct),
                HealthCheckMethod.TcpConnect => await CheckTcpAsync(svc, ct),
                HealthCheckMethod.ProcessCheck => CheckProcess(svc),
                _ => ServiceState.Unknown
            };
        }
        catch (Exception ex)
        {
            state = ServiceState.Down;
            error = ex.Message;
        }

        sw.Stop();

        _statuses[svc.Name] = new ServiceStatus
        {
            ServiceName = svc.Name,
            State = state,
            ResponseTimeMs = sw.ElapsedMilliseconds,
            LastChecked = DateTimeOffset.Now,
            ErrorMessage = error
        };
    }

    private async Task<ServiceState> CheckHttpAsync(ServiceDefinition svc, CancellationToken ct)
    {
        if (svc.Port is null || svc.HealthPath is null) return ServiceState.Unknown;

        var url = $"http://127.0.0.1:{svc.Port}{svc.HealthPath}";
        using var response = await _httpClient.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? ServiceState.Up : ServiceState.Degraded;
    }

    private static async Task<ServiceState> CheckTcpAsync(ServiceDefinition svc, CancellationToken ct)
    {
        if (svc.Port is null) return ServiceState.Unknown;

        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TcpTimeout);

        await tcp.ConnectAsync("127.0.0.1", svc.Port.Value, cts.Token);
        return ServiceState.Up;
    }

    private static ServiceState CheckProcess(ServiceDefinition svc)
    {
        if (svc.ProcessName is null) return ServiceState.Unknown;

        var processes = Process.GetProcessesByName(svc.ProcessName);
        return processes.Length > 0 ? ServiceState.Up : ServiceState.Down;
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }
}
