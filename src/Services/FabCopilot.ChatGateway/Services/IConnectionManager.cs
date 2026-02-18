using System.Net.WebSockets;
using FabCopilot.Contracts.Messages;

namespace FabCopilot.ChatGateway.Services;

public interface IConnectionManager
{
    Task HandleConnectionAsync(string equipmentId, WebSocket ws, CancellationToken ct);
    Task SendToEquipmentAsync(string equipmentId, string conversationId, ChatStreamChunk chunk);
    int ActiveConnectionCount { get; }
}
