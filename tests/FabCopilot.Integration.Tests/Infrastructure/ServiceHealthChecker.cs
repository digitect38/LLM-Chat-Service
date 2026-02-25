using System.Net.Sockets;

namespace FabCopilot.Integration.Tests.Infrastructure;

public static class ServiceHealthChecker
{
    public static async Task<bool> IsTcpPortOpenAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsWebSocketEndpointAvailableAsync(string wsUrl, TimeSpan timeout)
    {
        try
        {
            using var client = new System.Net.WebSockets.ClientWebSocket();
            using var cts = new CancellationTokenSource(timeout);
            var uri = new Uri(wsUrl);
            await client.ConnectAsync(uri, cts.Token);
            await client.CloseAsync(
                System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                "health check",
                cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
