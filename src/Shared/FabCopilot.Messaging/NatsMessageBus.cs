using System.Runtime.CompilerServices;
using System.Text.Json;
using FabCopilot.Contracts.Messages;
using FabCopilot.Messaging.Interfaces;
using NATS.Client.Core;

namespace FabCopilot.Messaging;

public sealed class NatsMessageBus : IMessageBus
{
    private readonly NatsConnection _nats;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NatsMessageBus(NatsConnection nats)
    {
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
    }

    public async Task PublishAsync<T>(string subject, MessageEnvelope<T> envelope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(envelope);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await _nats.PublishAsync(subject, bytes, serializer: NatsRawSerializer<byte[]>.Default, cancellationToken: ct);
    }

    public async IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(
        string subject,
        string? queueGroup = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var sub = await _nats.SubscribeCoreAsync<byte[]>(
            subject,
            queueGroup: queueGroup,
            serializer: NatsRawSerializer<byte[]>.Default,
            cancellationToken: ct);

        await foreach (var msg in sub.Msgs.ReadAllAsync(ct))
        {
            if (msg.Data is null || msg.Data.Length == 0)
            {
                continue;
            }

            var envelope = JsonSerializer.Deserialize<MessageEnvelope<T>>(msg.Data, JsonOptions);

            if (envelope is not null)
            {
                yield return envelope;
            }
        }
    }

    public async Task<MessageEnvelope<TReply>> RequestAsync<TRequest, TReply>(
        string subject,
        MessageEnvelope<TRequest> request,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(request);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);

        var replyMsg = await _nats.RequestAsync<byte[], byte[]>(
            subject,
            requestBytes,
            requestSerializer: NatsRawSerializer<byte[]>.Default,
            replySerializer: NatsRawSerializer<byte[]>.Default,
            cancellationToken: cts.Token);

        if (replyMsg.Data is null || replyMsg.Data.Length == 0)
        {
            throw new InvalidOperationException($"Received empty reply from subject '{subject}'.");
        }

        var envelope = JsonSerializer.Deserialize<MessageEnvelope<TReply>>(replyMsg.Data, JsonOptions);

        return envelope
            ?? throw new InvalidOperationException($"Failed to deserialize reply from subject '{subject}'.");
    }
}
