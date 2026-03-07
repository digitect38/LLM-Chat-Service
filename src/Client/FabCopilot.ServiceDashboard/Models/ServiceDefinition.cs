namespace FabCopilot.ServiceDashboard.Models;

public sealed class ServiceDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required ServiceCategory Category { get; init; }
    public required HealthCheckMethod HealthCheck { get; init; }
    public int? Port { get; init; }
    public string? HealthPath { get; init; }
    public string? ProjectPath { get; init; }
    public string? DockerContainer { get; init; }
    public string? DockerComposeService { get; init; }
    public string? DockerProfile { get; init; }
    public string? ProcessName { get; init; }
}

public enum ServiceCategory
{
    DotNet,
    Infrastructure,
    TtsEngine,
    SttEngine
}

public enum HealthCheckMethod
{
    HttpGet,
    TcpConnect,
    ProcessCheck
}
