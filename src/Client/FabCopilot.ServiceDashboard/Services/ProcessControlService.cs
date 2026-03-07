using System.Diagnostics;
using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Services;

public sealed class ProcessControlService
{
    private readonly ILogger<ProcessControlService> _logger;
    private readonly string _rootPath;
    private readonly string _restartScript;

    public ProcessControlService(ILogger<ProcessControlService> logger)
    {
        _logger = logger;
        // Navigate from project dir to solution root
        _rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        _restartScript = Path.Combine(_rootPath, "scripts", "_restart-now.ps1");
    }

    /// <summary>
    /// Restart all .NET services using the shared restart script.
    /// </summary>
    public async Task<string> RestartAllAsync(bool excludeDashboard = false, CancellationToken ct = default)
    {
        _logger.LogInformation("Executing restart-all via {Script} (excludeDashboard={Exclude})", _restartScript, excludeDashboard);

        if (!File.Exists(_restartScript))
        {
            var msg = $"Restart script not found: {_restartScript}";
            _logger.LogError(msg);
            return msg;
        }

        var excludeArg = excludeDashboard ? " -ExcludeDashboard" : "";
        return await RunPowerShellAsync(
            $"-ExecutionPolicy Bypass -File \"{_restartScript}\"{excludeArg}", ct);
    }

    /// <summary>
    /// Stop a .NET service by process name.
    /// </summary>
    public Task<string> StopServiceAsync(ServiceDefinition svc, CancellationToken ct = default)
    {
        if (svc.ProcessName is null) return Task.FromResult("No process name defined");

        _logger.LogInformation("Stopping {Service}", svc.Name);
        return RunPowerShellAsync(
            $"-Command \"Stop-Process -Name '{svc.ProcessName}' -Force -ErrorAction SilentlyContinue\"", ct);
    }

    /// <summary>
    /// Start a .NET service via dotnet run.
    /// </summary>
    public Task<string> StartServiceAsync(ServiceDefinition svc, CancellationToken ct = default)
    {
        if (svc.ProjectPath is null) return Task.FromResult("No project path defined");

        var projectFullPath = Path.Combine(_rootPath, svc.ProjectPath);
        var logDir = Path.Combine(_rootPath, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"{svc.Name}.log");

        _logger.LogInformation("Starting {Service} from {Path}", svc.Name, projectFullPath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectFullPath}\" --no-build",
                WorkingDirectory = _rootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var proc = Process.Start(psi);
            if (proc is null) return Task.FromResult("Failed to start process");

            // Redirect output to log file in background
            _ = Task.Run(async () =>
            {
                await using var writer = new StreamWriter(logFile, append: false);
                await proc.StandardOutput.BaseStream.CopyToAsync(writer.BaseStream, ct);
            }, ct);

            return Task.FromResult($"Started {svc.Name} (PID {proc.Id})");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error starting {svc.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Restart a Docker container.
    /// </summary>
    public async Task<string> RestartDockerContainerAsync(string containerName, CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting Docker container: {Container}", containerName);
        return await RunCommandAsync("docker", $"restart {containerName}", ct);
    }

    /// <summary>
    /// Stop a Docker container.
    /// </summary>
    public async Task<string> StopDockerContainerAsync(string containerName, CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping Docker container: {Container}", containerName);
        return await RunCommandAsync("docker", $"stop {containerName}", ct);
    }

    /// <summary>
    /// Start an existing Docker container.
    /// </summary>
    public async Task<string> StartDockerContainerAsync(string containerName, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Docker container: {Container}", containerName);
        return await RunCommandAsync("docker", $"start {containerName}", ct);
    }

    /// <summary>
    /// Start a Docker service via docker compose up -d (creates container if needed).
    /// </summary>
    public async Task<string> DockerComposeUpAsync(ServiceDefinition svc, CancellationToken ct = default)
    {
        if (svc.DockerComposeService is null) return "No docker-compose service defined";

        var composeFile = Path.Combine(_rootPath, "infra", "docker-compose.yml");
        if (!File.Exists(composeFile))
            return $"docker-compose.yml not found: {composeFile}";

        var profileArg = svc.DockerProfile is not null ? $"--profile {svc.DockerProfile}" : "";
        var args = $"compose -f \"{composeFile}\" {profileArg} up -d {svc.DockerComposeService}";

        _logger.LogInformation("Docker compose up: {Service} (profile: {Profile})", svc.DockerComposeService, svc.DockerProfile ?? "default");
        return await RunCommandAsync("docker", args, ct);
    }

    /// <summary>
    /// Stop a Docker service via docker compose stop.
    /// </summary>
    public async Task<string> DockerComposeStopAsync(ServiceDefinition svc, CancellationToken ct = default)
    {
        if (svc.DockerComposeService is null) return "No docker-compose service defined";

        var composeFile = Path.Combine(_rootPath, "infra", "docker-compose.yml");
        var profileArg = svc.DockerProfile is not null ? $"--profile {svc.DockerProfile}" : "";
        var args = $"compose -f \"{composeFile}\" {profileArg} stop {svc.DockerComposeService}";

        _logger.LogInformation("Docker compose stop: {Service}", svc.DockerComposeService);
        return await RunCommandAsync("docker", args, ct);
    }

    private static async Task<string> RunPowerShellAsync(string arguments, CancellationToken ct)
    {
        return await RunCommandAsync("powershell", arguments, ct);
    }

    private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return string.IsNullOrWhiteSpace(error) ? output : $"{output}\n{error}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
