using FabCopilot.ServiceDashboard.Services;

namespace FabCopilot.ServiceDashboard.Tests;

public class LogReaderServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LogReaderService _sut;

    public LogReaderServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dashboard-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new LogReaderService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── GetAvailableLogs ─────────────────────────────────────

    [Fact]
    public void GetAvailableLogs_EmptyDirectory_ReturnsEmptyArray()
    {
        var result = _sut.GetAvailableLogs();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableLogs_NonExistentDirectory_ReturnsEmptyArray()
    {
        var sut = new LogReaderService(Path.Combine(_tempDir, "nonexistent"));
        var result = sut.GetAvailableLogs();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableLogs_WithLogFiles_ReturnsNamesWithoutExtension()
    {
        File.WriteAllText(Path.Combine(_tempDir, "ChatGateway.log"), "test");
        File.WriteAllText(Path.Combine(_tempDir, "WebClient.log"), "test");

        var result = _sut.GetAvailableLogs();
        result.Should().BeEquivalentTo(["ChatGateway", "WebClient"]);
    }

    [Fact]
    public void GetAvailableLogs_IgnoresNonLogFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "ChatGateway.log"), "test");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "test");
        File.WriteAllText(Path.Combine(_tempDir, "data.json"), "test");

        var result = _sut.GetAvailableLogs();
        result.Should().ContainSingle().Which.Should().Be("ChatGateway");
    }

    [Fact]
    public void GetAvailableLogs_ReturnsSortedNames()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Zebra.log"), "");
        File.WriteAllText(Path.Combine(_tempDir, "Alpha.log"), "");
        File.WriteAllText(Path.Combine(_tempDir, "Middle.log"), "");

        var result = _sut.GetAvailableLogs();
        result.Should().BeInAscendingOrder();
    }

    // ─── ReadLastLines ────────────────────────────────────────

    [Fact]
    public void ReadLastLines_NonExistentFile_ReturnsErrorMessage()
    {
        var result = _sut.ReadLastLines("NoSuchService");
        result.Should().ContainSingle()
            .Which.Should().Contain("Log file not found");
    }

    [Fact]
    public void ReadLastLines_EmptyFile_ReturnsEmptyArray()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Empty.log"), "");

        var result = _sut.ReadLastLines("Empty");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ReadLastLines_FileShorterThanCount_ReturnsAllLines()
    {
        var lines = Enumerable.Range(1, 5).Select(i => $"Line {i}").ToArray();
        File.WriteAllLines(Path.Combine(_tempDir, "Short.log"), lines);

        var result = _sut.ReadLastLines("Short", lineCount: 100);
        result.Should().HaveCount(5);
        result[0].Should().Be("Line 1");
        result[4].Should().Be("Line 5");
    }

    [Fact]
    public void ReadLastLines_FileLongerThanCount_ReturnsLastNLines()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"Line {i}").ToArray();
        File.WriteAllLines(Path.Combine(_tempDir, "Long.log"), lines);

        var result = _sut.ReadLastLines("Long", lineCount: 10);
        result.Should().HaveCount(10);
        result[0].Should().Be("Line 191");
        result[9].Should().Be("Line 200");
    }

    [Fact]
    public void ReadLastLines_DefaultCount_Is100()
    {
        var lines = Enumerable.Range(1, 150).Select(i => $"Line {i}").ToArray();
        File.WriteAllLines(Path.Combine(_tempDir, "Default.log"), lines);

        var result = _sut.ReadLastLines("Default");
        result.Should().HaveCount(100);
        result[0].Should().Be("Line 51");
    }

    [Fact]
    public void ReadLastLines_SharedAccess_DoesNotLockFile()
    {
        var path = Path.Combine(_tempDir, "Shared.log");
        // Simulate a writer holding the file open
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var sw = new StreamWriter(writer);
        sw.WriteLine("Line 1");
        sw.WriteLine("Line 2");
        sw.Flush();

        // Should be able to read while writer is open
        var result = _sut.ReadLastLines("Shared");
        result.Should().HaveCount(2);
    }

    // ─── ReadServiceLog (routing) ─────────────────────────────

    [Fact]
    public void ReadServiceLog_DotNetService_UsesFileLog()
    {
        // ChatGateway is a .NET service (no DockerContainer)
        File.WriteAllLines(Path.Combine(_tempDir, "ChatGateway.log"),
            ["[INFO] Gateway started", "[INFO] Listening on 5000"]);

        var result = _sut.ReadServiceLog("ChatGateway", 100);
        result.Should().HaveCount(2);
        result[0].Should().Contain("Gateway started");
    }

    [Fact]
    public void ReadServiceLog_DockerService_AttemptsDockerLogs()
    {
        // NATS is a Docker service with container "infra-nats-1"
        // In test env, docker may not be running, so we expect either
        // docker output or an error message (graceful handling)
        var result = _sut.ReadServiceLog("NATS", 10);
        result.Should().NotBeNull();
        result.Should().NotBeEmpty("should return either docker logs or an error message");
    }

    [Fact]
    public void ReadServiceLog_UnknownService_FallsBackToFileLog()
    {
        // Unknown service has no DockerContainer → falls through to ReadLastLines
        var result = _sut.ReadServiceLog("UnknownService", 10);
        result.Should().ContainSingle()
            .Which.Should().Contain("Log file not found");
    }

    [Fact]
    public void ReadServiceLog_DotNetServiceWithNoFile_ReturnsNotFound()
    {
        // LlmService is .NET, no log file created
        var result = _sut.ReadServiceLog("LlmService", 10);
        result.Should().ContainSingle()
            .Which.Should().Contain("Log file not found");
    }

    [Theory]
    [InlineData("NATS")]
    [InlineData("Redis")]
    [InlineData("Qdrant")]
    [InlineData("Ollama")]
    [InlineData("Kokoro")]
    [InlineData("EdgeTts")]
    public void ReadServiceLog_DockerServices_DoNotUseFileLog(string serviceName)
    {
        // Create a file that would match if file log was used
        File.WriteAllText(Path.Combine(_tempDir, $"{serviceName}.log"), "FILE LOG DATA");

        var result = _sut.ReadServiceLog(serviceName, 10);
        // Docker services should NOT return the file contents
        // They should use docker logs (which may fail in test env)
        var allText = string.Join(" ", result);
        allText.Should().NotContain("FILE LOG DATA",
            $"Docker service '{serviceName}' should use docker logs, not file log");
    }

    // ─── ReadDockerLogs ───────────────────────────────────────

    [Fact]
    public void ReadDockerLogs_InvalidContainer_ReturnsGracefulError()
    {
        var result = _sut.ReadDockerLogs("nonexistent-container-xyz", 10);
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }
}
