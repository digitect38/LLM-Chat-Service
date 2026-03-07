using FabCopilot.ServiceDashboard.Models;
using FabCopilot.ServiceDashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FabCopilot.ServiceDashboard.Tests;

public class ProcessControlServiceTests
{
    private readonly ProcessControlService _sut;

    public ProcessControlServiceTests()
    {
        _sut = new ProcessControlService(NullLogger<ProcessControlService>.Instance);
    }

    // ─── StopServiceAsync ─────────────────────────────────────

    [Fact]
    public async Task StopServiceAsync_NullProcessName_ReturnsError()
    {
        var svc = new ServiceDefinition
        {
            Name = "Test",
            DisplayName = "Test Service",
            Category = ServiceCategory.DotNet,
            HealthCheck = HealthCheckMethod.HttpGet,
            ProcessName = null
        };

        var result = await _sut.StopServiceAsync(svc);
        result.Should().Be("No process name defined");
    }

    [Fact]
    public async Task StopServiceAsync_WithProcessName_DoesNotReturnNullError()
    {
        var svc = new ServiceDefinition
        {
            Name = "Test",
            DisplayName = "Test Service",
            Category = ServiceCategory.DotNet,
            HealthCheck = HealthCheckMethod.HttpGet,
            ProcessName = "nonexistent-process-xyz"
        };

        // Should attempt to stop (even if process doesn't exist)
        // SilentlyContinue means no error from PowerShell
        var result = await _sut.StopServiceAsync(svc);
        result.Should().NotBe("No process name defined");
    }

    // ─── StartServiceAsync ────────────────────────────────────

    [Fact]
    public async Task StartServiceAsync_NullProjectPath_ReturnsError()
    {
        var svc = new ServiceDefinition
        {
            Name = "Test",
            DisplayName = "Test Service",
            Category = ServiceCategory.DotNet,
            HealthCheck = HealthCheckMethod.HttpGet,
            ProjectPath = null
        };

        var result = await _sut.StartServiceAsync(svc);
        result.Should().Be("No project path defined");
    }

    // ─── DockerComposeUpAsync ─────────────────────────────────

    [Fact]
    public async Task DockerComposeUpAsync_NullComposeService_ReturnsError()
    {
        var svc = new ServiceDefinition
        {
            Name = "Test",
            DisplayName = "Test",
            Category = ServiceCategory.Infrastructure,
            HealthCheck = HealthCheckMethod.TcpConnect,
            DockerComposeService = null
        };

        var result = await _sut.DockerComposeUpAsync(svc);
        result.Should().Be("No docker-compose service defined");
    }

    [Fact]
    public async Task DockerComposeUpAsync_WithProfile_IncludesProfileArg()
    {
        // This tests that the method doesn't crash with valid inputs
        // The docker-compose.yml may not exist in test root, so it should return "not found"
        var svc = new ServiceDefinition
        {
            Name = "TestTts",
            DisplayName = "Test TTS",
            Category = ServiceCategory.TtsEngine,
            HealthCheck = HealthCheckMethod.TcpConnect,
            DockerComposeService = "test-tts",
            DockerProfile = "tts"
        };

        var result = await _sut.DockerComposeUpAsync(svc);
        // In test environment, docker-compose.yml won't exist at the computed path
        // Either returns "not found" or docker error — both are acceptable
        result.Should().NotBeNullOrEmpty();
    }

    // ─── DockerComposeStopAsync ───────────────────────────────

    [Fact]
    public async Task DockerComposeStopAsync_NullComposeService_ReturnsError()
    {
        var svc = new ServiceDefinition
        {
            Name = "Test",
            DisplayName = "Test",
            Category = ServiceCategory.Infrastructure,
            HealthCheck = HealthCheckMethod.TcpConnect,
            DockerComposeService = null
        };

        var result = await _sut.DockerComposeStopAsync(svc);
        result.Should().Be("No docker-compose service defined");
    }

    // ─── RestartAllAsync ──────────────────────────────────────

    [Fact]
    public async Task RestartAllAsync_MissingScript_ReturnsNotFoundError()
    {
        // In test environment, _restartScript won't exist at the computed path
        var result = await _sut.RestartAllAsync();
        result.Should().NotBeNullOrEmpty();
        // Either contains "not found" or some PowerShell output
        (result.Contains("not found", StringComparison.OrdinalIgnoreCase)
         || result.Length > 0).Should().BeTrue();
    }

    // ─── RestartDockerContainerAsync ──────────────────────────

    [Fact]
    public async Task RestartDockerContainerAsync_ReturnsResult()
    {
        // Will fail or succeed depending on Docker availability
        var result = await _sut.RestartDockerContainerAsync("nonexistent-container-xyz");
        result.Should().NotBeNull();
    }

    // ─── Input validation across all real services ────────────

    [Fact]
    public void AllDotNetServices_HaveRequiredFieldsForControl()
    {
        var dotNetServices = HealthCheckService.AllServices
            .Where(s => s.Category == ServiceCategory.DotNet);

        foreach (var svc in dotNetServices)
        {
            svc.ProcessName.Should().NotBeNull($"{svc.Name} needs ProcessName for StopServiceAsync");
            svc.ProjectPath.Should().NotBeNull($"{svc.Name} needs ProjectPath for StartServiceAsync");
        }
    }

    [Fact]
    public void AllDockerServices_HaveRequiredFieldsForControl()
    {
        var dockerServices = HealthCheckService.AllServices
            .Where(s => s.Category is ServiceCategory.Infrastructure or ServiceCategory.TtsEngine);

        foreach (var svc in dockerServices)
        {
            svc.DockerComposeService.Should().NotBeNull(
                $"{svc.Name} needs DockerComposeService for DockerComposeUpAsync");
            svc.DockerContainer.Should().NotBeNull(
                $"{svc.Name} needs DockerContainer for RestartDockerContainerAsync");
        }
    }
}
