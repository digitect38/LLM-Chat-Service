using FabCopilot.Contracts.Messages;

namespace FabCopilot.Messaging.Interfaces;

public interface IMessageBus
{
    Task PublishAsync<T>(string subject, MessageEnvelope<T> envelope, CancellationToken ct = default);

    IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string subject, string? queueGroup = null, CancellationToken ct = default);

    Task<MessageEnvelope<TReply>> RequestAsync<TRequest, TReply>(string subject, MessageEnvelope<TRequest> request, TimeSpan timeout, CancellationToken ct = default);
}
