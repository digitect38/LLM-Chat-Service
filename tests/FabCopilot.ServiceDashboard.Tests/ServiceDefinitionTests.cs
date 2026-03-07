using FabCopilot.ServiceDashboard.Models;
using FabCopilot.ServiceDashboard.Services;

namespace FabCopilot.ServiceDashboard.Tests;

public class ServiceDefinitionTests
{
    private static readonly List<ServiceDefinition> AllServices = HealthCheckService.AllServices;

    [Fact]
    public void AllServices_ShouldContainExpectedCount()
    {
        // 5 .NET + 4 Infra + 1 STT (Whisper) + 9 TTS = 19
        AllServices.Should().HaveCount(19);
    }

    [Fact]
    public void AllServices_ShouldHaveUniqueNames()
    {
        var names = AllServices.Select(s => s.Name).ToList();
        names.Should().OnlyHaveUniqueItems("each service must have a unique Name");
    }

    [Fact]
    public void AllServices_ShouldHaveNonEmptyDisplayNames()
    {
        foreach (var svc in AllServices)
        {
            svc.DisplayName.Should().NotBeNullOrWhiteSpace($"service '{svc.Name}' must have a DisplayName");
        }
    }

    [Fact]
    public void DotNetServices_ShouldHaveProcessNames()
    {
        var dotNetServices = AllServices.Where(s => s.Category == ServiceCategory.DotNet);
        foreach (var svc in dotNetServices)
        {
            svc.ProcessName.Should().NotBeNullOrWhiteSpace(
                $".NET service '{svc.Name}' must have a ProcessName for health checks");
        }
    }

    [Fact]
    public void DotNetServices_ShouldHaveProjectPaths()
    {
        var dotNetServices = AllServices.Where(s => s.Category == ServiceCategory.DotNet);
        foreach (var svc in dotNetServices)
        {
            svc.ProjectPath.Should().NotBeNullOrWhiteSpace(
                $".NET service '{svc.Name}' must have a ProjectPath for start/stop");
        }
    }

    [Fact]
    public void DotNetServices_Count_ShouldBeFive()
    {
        AllServices.Count(s => s.Category == ServiceCategory.DotNet).Should().Be(5);
    }

    [Fact]
    public void InfraServices_Count_ShouldBeFour()
    {
        // NATS, Redis, Qdrant, Ollama
        AllServices.Count(s => s.Category == ServiceCategory.Infrastructure).Should().Be(4);
    }

    [Fact]
    public void TtsServices_Count_ShouldBeNine()
    {
        AllServices.Count(s => s.Category == ServiceCategory.TtsEngine).Should().Be(9);
    }

    [Fact]
    public void DockerServices_ShouldHaveContainerNames()
    {
        var dockerServices = AllServices.Where(s =>
            s.Category is ServiceCategory.Infrastructure or ServiceCategory.TtsEngine or ServiceCategory.SttEngine);
        foreach (var svc in dockerServices)
        {
            svc.DockerContainer.Should().NotBeNullOrWhiteSpace(
                $"Docker service '{svc.Name}' must have a DockerContainer name");
        }
    }

    [Fact]
    public void DockerServices_ShouldHaveComposeServiceNames()
    {
        var dockerServices = AllServices.Where(s =>
            s.Category is ServiceCategory.Infrastructure or ServiceCategory.TtsEngine or ServiceCategory.SttEngine);
        foreach (var svc in dockerServices)
        {
            svc.DockerComposeService.Should().NotBeNullOrWhiteSpace(
                $"Docker service '{svc.Name}' must have a DockerComposeService name");
        }
    }

    [Fact]
    public void TtsEngines_ShouldHaveTtsProfile()
    {
        var ttsServices = AllServices.Where(s => s.Category == ServiceCategory.TtsEngine);
        foreach (var svc in ttsServices)
        {
            svc.DockerProfile.Should().Be("tts",
                $"TTS engine '{svc.Name}' must use profile 'tts'");
        }
    }

    [Fact]
    public void InfraServices_ShouldNotHaveProfile()
    {
        var infraServices = AllServices.Where(s => s.Category == ServiceCategory.Infrastructure);
        foreach (var svc in infraServices)
        {
            svc.DockerProfile.Should().BeNull(
                $"Infrastructure service '{svc.Name}' should not need a Docker profile");
        }
    }

    [Fact]
    public void SttServices_Count_ShouldBeOne()
    {
        AllServices.Count(s => s.Category == ServiceCategory.SttEngine).Should().Be(1);
    }

    [Fact]
    public void SttEngines_Whisper_ShouldHaveWhisperProfile()
    {
        var whisper = AllServices.First(s => s.Name == "Whisper");
        whisper.Category.Should().Be(ServiceCategory.SttEngine);
        whisper.DockerProfile.Should().Be("whisper");
        whisper.Port.Should().Be(8300);
        whisper.DockerContainer.Should().Be("fab-whisper");
    }

    [Fact]
    public void ServicesWithHttpHealthCheck_ShouldHavePortAndPath()
    {
        var httpServices = AllServices.Where(s => s.HealthCheck == HealthCheckMethod.HttpGet);
        foreach (var svc in httpServices)
        {
            svc.Port.Should().NotBeNull($"HTTP health-check service '{svc.Name}' must have a Port");
            svc.HealthPath.Should().NotBeNullOrWhiteSpace(
                $"HTTP health-check service '{svc.Name}' must have a HealthPath");
        }
    }

    [Fact]
    public void ServicesWithTcpHealthCheck_ShouldHavePort()
    {
        var tcpServices = AllServices.Where(s => s.HealthCheck == HealthCheckMethod.TcpConnect);
        foreach (var svc in tcpServices)
        {
            svc.Port.Should().NotBeNull($"TCP health-check service '{svc.Name}' must have a Port");
        }
    }

    [Fact]
    public void PortNumbers_ShouldBeUnique_AmongServicesWithPorts()
    {
        var ports = AllServices
            .Where(s => s.Port.HasValue)
            .Select(s => s.Port!.Value)
            .ToList();
        ports.Should().OnlyHaveUniqueItems("each service must listen on a unique port");
    }

    [Theory]
    [InlineData("ChatGateway", 5000)]
    [InlineData("WebClient", 5010)]
    [InlineData("KnowledgeService", 5002)]
    [InlineData("NATS", 4222)]
    [InlineData("Redis", 6379)]
    [InlineData("Qdrant", 6333)]
    [InlineData("Ollama", 11434)]
    [InlineData("EdgeTts", 5050)]
    [InlineData("Kokoro", 8401)]
    public void KnownServices_ShouldHaveExpectedPorts(string name, int expectedPort)
    {
        var svc = AllServices.FirstOrDefault(s => s.Name == name);
        svc.Should().NotBeNull($"service '{name}' should exist");
        svc!.Port.Should().Be(expectedPort);
    }

    [Theory]
    [InlineData("EdgeTts", "fab-edge-tts")]
    [InlineData("XTTS", "fab-tts")]
    [InlineData("Kokoro", "fab-kokoro")]
    [InlineData("CosyVoice", "fab-cosyvoice")]
    [InlineData("FishSpeech", "fab-fish-speech")]
    [InlineData("Chatterbox", "fab-chatterbox")]
    [InlineData("Bark", "fab-bark")]
    [InlineData("Piper", "fab-piper")]
    [InlineData("Orpheus", "fab-orpheus")]
    [InlineData("NATS", "infra-nats-1")]
    [InlineData("Redis", "infra-redis-1")]
    [InlineData("Qdrant", "infra-qdrant-1")]
    [InlineData("Ollama", "infra-ollama-1")]
    public void DockerServices_ShouldHaveCorrectContainerNames(string name, string expectedContainer)
    {
        var svc = AllServices.FirstOrDefault(s => s.Name == name);
        svc.Should().NotBeNull($"service '{name}' should exist");
        svc!.DockerContainer.Should().Be(expectedContainer);
    }

    [Theory]
    [InlineData("NATS", "nats")]
    [InlineData("Redis", "redis")]
    [InlineData("Qdrant", "qdrant")]
    [InlineData("Ollama", "ollama")]
    [InlineData("EdgeTts", "edge-tts")]
    [InlineData("XTTS", "tts")]
    [InlineData("Kokoro", "kokoro-tts")]
    public void DockerServices_ShouldHaveCorrectComposeServiceNames(string name, string expectedCompose)
    {
        var svc = AllServices.FirstOrDefault(s => s.Name == name);
        svc.Should().NotBeNull();
        svc!.DockerComposeService.Should().Be(expectedCompose);
    }
}
