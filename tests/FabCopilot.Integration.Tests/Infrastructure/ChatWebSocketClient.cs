using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FabCopilot.Contracts.Messages;

namespace FabCopilot.Integration.Tests.Infrastructure;

public sealed class ChatWebSocketClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseUrl;

    public ChatWebSocketClient(string baseUrl = "ws://localhost:5000")
    {
        _baseUrl = baseUrl;
    }

    public async Task<ChatResponse> SendAndCollectAsync(
        string question,
        string equipmentId = "CMP-001",
        string? conversationId = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(180);
        var result = new ChatResponse();
        var tokens = new List<string>();
        var startTime = DateTime.UtcNow;
        var firstTokenReceived = false;

        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(timeout.Value);

        var uri = new Uri($"{_baseUrl}/ws/chat/{equipmentId}");
        await ws.ConnectAsync(uri, cts.Token);

        // Send request
        var request = new
        {
            userMessage = question,
            equipmentId,
            conversationId = conversationId ?? Guid.NewGuid().ToString()
        };
        var requestJson = JsonSerializer.Serialize(request);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        await ws.SendAsync(
            new ArraySegment<byte>(requestBytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cts.Token);

        // Receive streaming response
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            WebSocketReceiveResult receiveResult;
            try
            {
                receiveResult = await ws.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (receiveResult.MessageType == WebSocketMessageType.Close)
                break;

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, receiveResult.Count));

            if (!receiveResult.EndOfMessage)
                continue;

            var json = messageBuffer.ToString();
            messageBuffer.Clear();

            ChatStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatStreamChunk>(json, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (chunk == null)
                continue;

            result.ConversationId = chunk.ConversationId;

            if (!string.IsNullOrEmpty(chunk.Error))
            {
                result.Error = chunk.Error;
                break;
            }

            if (!string.IsNullOrEmpty(chunk.Token))
            {
                if (!firstTokenReceived)
                {
                    result.TimeToFirstToken = DateTime.UtcNow - startTime;
                    firstTokenReceived = true;
                }
                tokens.Add(chunk.Token);
                result.TokenCount++;
            }

            if (chunk.Citations is { Count: > 0 })
            {
                result.Citations.AddRange(chunk.Citations);
            }

            if (chunk.IsComplete)
                break;
        }

        result.FullText = string.Join("", tokens);
        result.TotalTime = DateTime.UtcNow - startTime;

        // Graceful close
        if (ws.State == WebSocketState.Open)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "done",
                    closeCts.Token);
            }
            catch
            {
                // Ignore close errors
            }
        }

        return result;
    }

    public void Dispose()
    {
        // No persistent resources
    }
}
