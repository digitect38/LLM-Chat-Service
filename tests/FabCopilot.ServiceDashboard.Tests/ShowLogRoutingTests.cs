using FabCopilot.ServiceDashboard.Models;
using FabCopilot.ServiceDashboard.Services;

namespace FabCopilot.ServiceDashboard.Tests;

/// <summary>
/// Tests for the ShowLog / ReadServiceLog routing logic.
/// Verifies that:
///   1. .NET services use file-based log reading
///   2. Docker services use docker logs (not file-based)
///   3. Every service in AllServices has a valid log source
///   4. Log buttons exist for all service categories
/// </summary>
public class ShowLogRoutingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LogReaderService _logReader;

    public ShowLogRoutingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"showlog-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logReader = new LogReaderService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Log routing: .NET vs Docker ──────────────────────────

    [Theory]
    [InlineData("ChatGateway")]
    [InlineData("WebClient")]
    [InlineData("LlmService")]
    [InlineData("KnowledgeService")]
    [InlineData("RagService")]
    public void ReadServiceLog_DotNetServices_UseFileLog(string serviceName)
    {
        // Create a log file for this service
        var logContent = $"[INFO] {serviceName} started\n[INFO] Listening\n";
        File.WriteAllText(Path.Combine(_tempDir, $"{serviceName}.log"), logContent);

        var result = _logReader.ReadServiceLog(serviceName, 100);
        result.Should().NotBeEmpty();

        // Verify it read from the file (contains our content)
        string.Join(" ", result).Should().Contain("started");
    }

    [Theory]
    [InlineData("NATS", "infra-nats-1")]
    [InlineData("Redis", "infra-redis-1")]
    [InlineData("Qdrant", "infra-qdrant-1")]
    [InlineData("Ollama", "infra-ollama-1")]
    [InlineData("EdgeTts", "fab-edge-tts")]
    [InlineData("XTTS", "fab-tts")]
    [InlineData("Kokoro", "fab-kokoro")]
    [InlineData("CosyVoice", "fab-cosyvoice")]
    [InlineData("FishSpeech", "fab-fish-speech")]
    [InlineData("Chatterbox", "fab-chatterbox")]
    [InlineData("Bark", "fab-bark")]
    [InlineData("Piper", "fab-piper")]
    [InlineData("Orpheus", "fab-orpheus")]
    public void ReadServiceLog_DockerServices_UseDockerLogs_NotFileLog(string serviceName, string containerName)
    {
        // Create a fake file log that should NOT be used for Docker services
        File.WriteAllText(Path.Combine(_tempDir, $"{serviceName}.log"), "THIS IS A FILE LOG - SHOULD NOT APPEAR");

        var svcDef = HealthCheckService.AllServices.FirstOrDefault(s => s.Name == serviceName);
        svcDef.Should().NotBeNull();
        svcDef!.DockerContainer.Should().Be(containerName);

        var result = _logReader.ReadServiceLog(serviceName, 10);
        result.Should().NotBeNull();

        // Docker services should use docker logs, NOT file logs
        var combined = string.Join(" ", result);
        combined.Should().NotContain("THIS IS A FILE LOG - SHOULD NOT APPEAR",
            $"Docker service '{serviceName}' should use docker logs, not file log");
    }

    // ─── All services have log capability ─────────────────────

    [Fact]
    public void AllDotNetServices_HaveNoDockerContainer()
    {
        var dotNet = HealthCheckService.AllServices
            .Where(s => s.Category == ServiceCategory.DotNet);

        foreach (var svc in dotNet)
        {
            svc.DockerContainer.Should().BeNull(
                $".NET service '{svc.Name}' should not have DockerContainer — uses file log");
        }
    }

    [Fact]
    public void AllInfraServices_HaveDockerContainer()
    {
        var infra = HealthCheckService.AllServices
            .Where(s => s.Category == ServiceCategory.Infrastructure);

        foreach (var svc in infra)
        {
            svc.DockerContainer.Should().NotBeNullOrWhiteSpace(
                $"Infrastructure service '{svc.Name}' needs DockerContainer for docker log");
        }
    }

    [Fact]
    public void AllTtsServices_HaveDockerContainer()
    {
        var tts = HealthCheckService.AllServices
            .Where(s => s.Category == ServiceCategory.TtsEngine);

        foreach (var svc in tts)
        {
            svc.DockerContainer.Should().NotBeNullOrWhiteSpace(
                $"TTS service '{svc.Name}' needs DockerContainer for docker log");
        }
    }

    // ─── ReadServiceLog edge cases ────────────────────────────

    [Fact]
    public void ReadServiceLog_UnknownName_FallsToFileLog()
    {
        var result = _logReader.ReadServiceLog("SomeUnknownService", 10);
        result.Should().ContainSingle()
            .Which.Should().Contain("Log file not found");
    }

    [Fact]
    public void ReadServiceLog_NullOrEmpty_DoesNotThrow()
    {
        // Edge case: empty service name
        var result = _logReader.ReadServiceLog("", 10);
        result.Should().NotBeNull();
    }

    [Fact]
    public void ReadServiceLog_WithLineCount_RespectsLimit()
    {
        var lines = Enumerable.Range(1, 50).Select(i => $"Line {i}");
        File.WriteAllLines(Path.Combine(_tempDir, "ChatGateway.log"), lines);

        var result = _logReader.ReadServiceLog("ChatGateway", 10);
        result.Should().HaveCount(10);
        result[0].Should().Be("Line 41");
        result[9].Should().Be("Line 50");
    }
}
