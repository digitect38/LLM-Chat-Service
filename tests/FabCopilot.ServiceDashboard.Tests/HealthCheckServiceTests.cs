using FabCopilot.ServiceDashboard.Models;
using FabCopilot.ServiceDashboard.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FabCopilot.ServiceDashboard.Tests;

public class HealthCheckServiceTests
{
    private readonly HealthCheckService _sut;

    public HealthCheckServiceTests()
    {
        _sut = new HealthCheckService(NullLogger<HealthCheckService>.Instance);
    }

    [Fact]
    public void Constructor_ShouldInitializeAllStatuses_AsUnknown()
    {
        var allStatuses = _sut.GetAllStatuses();
        allStatuses.Should().HaveCount(HealthCheckService.AllServices.Count);

        foreach (var status in allStatuses.Values)
        {
            status.State.Should().Be(ServiceState.Unknown);
        }
    }

    [Fact]
    public void GetStatus_KnownService_ReturnsStoredStatus()
    {
        var status = _sut.GetStatus("ChatGateway");
        status.Should().NotBeNull();
        status.ServiceName.Should().Be("ChatGateway");
    }

    [Fact]
    public void GetStatus_UnknownService_ReturnsDefaultUnknown()
    {
        var status = _sut.GetStatus("NonExistentService");
        status.Should().NotBeNull();
        status.ServiceName.Should().Be("NonExistentService");
        status.State.Should().Be(ServiceState.Unknown);
    }

    [Fact]
    public void GetAllStatuses_ReturnsAllRegisteredServices()
    {
        var statuses = _sut.GetAllStatuses();
        foreach (var svc in HealthCheckService.AllServices)
        {
            statuses.Should().ContainKey(svc.Name);
        }
    }

    [Fact]
    public void ActiveTtsProvider_DefaultsToQuestionMark()
    {
        _sut.ActiveTtsProvider.Should().Be("?");
    }

    [Fact]
    public void ActiveTtsVoice_DefaultsToEmpty()
    {
        _sut.ActiveTtsVoice.Should().BeEmpty();
    }

    [Fact]
    public void AllStatuses_HaveValidLastChecked()
    {
        var statuses = _sut.GetAllStatuses();
        foreach (var status in statuses.Values)
        {
            status.LastChecked.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        }
    }

    [Theory]
    [InlineData("ChatGateway")]
    [InlineData("WebClient")]
    [InlineData("LlmService")]
    [InlineData("KnowledgeService")]
    [InlineData("RagService")]
    [InlineData("NATS")]
    [InlineData("Redis")]
    [InlineData("Qdrant")]
    [InlineData("Ollama")]
    [InlineData("Kokoro")]
    public void GetStatus_EachService_ReturnsValidStatus(string serviceName)
    {
        var status = _sut.GetStatus(serviceName);
        status.ServiceName.Should().Be(serviceName);
        status.State.Should().BeDefined();
    }

    [Fact]
    public void AllServices_StaticList_IsNotEmpty()
    {
        HealthCheckService.AllServices.Should().NotBeEmpty();
    }

    [Fact]
    public void GetAllStatuses_IsReadOnly_Dictionary()
    {
        var statuses = _sut.GetAllStatuses();
        statuses.Should().BeAssignableTo<IReadOnlyDictionary<string, ServiceStatus>>();
    }
}
