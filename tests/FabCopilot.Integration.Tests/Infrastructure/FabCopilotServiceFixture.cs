namespace FabCopilot.Integration.Tests.Infrastructure;

[CollectionDefinition("FabCopilot Services")]
public class FabCopilotServiceCollection : ICollectionFixture<FabCopilotServiceFixture>;

public sealed class FabCopilotServiceFixture : IAsyncLifetime
{
    public bool ServicesAvailable { get; private set; }
    public string SkipReason { get; private set; } = string.Empty;
    public ChatWebSocketClient Client { get; } = new();

    private const string WsUrl = "ws://localhost:5000/ws/chat/CMP-001";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public async Task InitializeAsync()
    {
        // Check ChatGateway WebSocket endpoint
        var gatewayUp = await ServiceHealthChecker.IsTcpPortOpenAsync(
            "localhost", 5000, ConnectTimeout);

        if (!gatewayUp)
        {
            ServicesAvailable = false;
            SkipReason = "ChatGateway is not running on localhost:5000. Start services with docker-compose and dotnet run.";
            return;
        }

        // Verify WebSocket handshake works
        var wsUp = await ServiceHealthChecker.IsWebSocketEndpointAvailableAsync(
            WsUrl, ConnectTimeout);

        if (!wsUp)
        {
            ServicesAvailable = false;
            SkipReason = "ChatGateway WebSocket endpoint not accepting connections. Verify the service is fully started.";
            return;
        }

        ServicesAvailable = true;
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }
}
