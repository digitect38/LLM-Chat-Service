using FabCopilot.ServiceDashboard.Models;
using FabCopilot.ServiceDashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FabCopilot.ServiceDashboard.Tests;

public class DockerStatusServiceTests
{
    private readonly DockerStatusService _sut;

    public DockerStatusServiceTests()
    {
        _sut = new DockerStatusService(NullLogger<DockerStatusService>.Instance);
    }

    [Fact]
    public void IsDockerAvailable_DefaultsToTrue()
    {
        _sut.IsDockerAvailable.Should().BeTrue();
    }

    [Fact]
    public void GetAllContainers_Initially_ReturnsEmpty()
    {
        var containers = _sut.GetAllContainers();
        containers.Should().BeEmpty();
    }

    [Fact]
    public void GetContainer_WhenEmpty_ReturnsNull()
    {
        var result = _sut.GetContainer("some-container");
        result.Should().BeNull();
    }

    [Fact]
    public void GetContainer_ExactMatch_ReturnsContainer()
    {
        // Use reflection to add a container to the internal dictionary
        var containers = GetInternalContainers();
        containers["fab-kokoro"] = new DockerContainerInfo
        {
            Id = "abc123",
            Names = "fab-kokoro",
            Image = "ghcr.io/remsky/kokoro-fastapi-gpu:latest",
            Status = "Up 2 hours",
            State = "running"
        };

        var result = _sut.GetContainer("fab-kokoro");
        result.Should().NotBeNull();
        result!.Names.Should().Be("fab-kokoro");
        result.State.Should().Be("running");
    }

    [Fact]
    public void GetContainer_PartialMatch_ReturnsContainer()
    {
        var containers = GetInternalContainers();
        containers["infra-nats-1"] = new DockerContainerInfo
        {
            Id = "def456",
            Names = "infra-nats-1",
            Image = "nats:2.10-alpine",
            Status = "Up 5 hours",
            State = "running"
        };

        // Partial match: search for "nats-1" should match "infra-nats-1"
        var result = _sut.GetContainer("nats-1");
        result.Should().NotBeNull();
        result!.Names.Should().Be("infra-nats-1");
    }

    [Fact]
    public void GetContainer_NoMatch_ReturnsNull()
    {
        var containers = GetInternalContainers();
        containers["fab-kokoro"] = new DockerContainerInfo
        {
            Id = "abc",
            Names = "fab-kokoro",
            Image = "test",
            Status = "running",
            State = "running"
        };

        var result = _sut.GetContainer("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public void GetContainer_CaseInsensitivePartialMatch()
    {
        var containers = GetInternalContainers();
        containers["fab-edge-tts"] = new DockerContainerInfo
        {
            Id = "ghi789",
            Names = "fab-edge-tts",
            Image = "travisvn/openai-edge-tts:latest",
            Status = "Up 1 hour",
            State = "running"
        };

        // Case-insensitive partial match
        var result = _sut.GetContainer("FAB-EDGE-TTS");
        // Exact match should fail (key is lowercase), but partial should match
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetAllContainers_AfterAdding_ReturnsAll()
    {
        var containers = GetInternalContainers();
        containers["c1"] = new DockerContainerInfo { Id = "1", Names = "c1", State = "running" };
        containers["c2"] = new DockerContainerInfo { Id = "2", Names = "c2", State = "exited" };

        var all = _sut.GetAllContainers();
        all.Should().HaveCount(2);
    }

    private System.Collections.Concurrent.ConcurrentDictionary<string, DockerContainerInfo> GetInternalContainers()
    {
        // Access internal state via reflection for testing
        var field = typeof(DockerStatusService).GetField("_containers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (System.Collections.Concurrent.ConcurrentDictionary<string, DockerContainerInfo>)field!.GetValue(_sut)!;
    }
}
