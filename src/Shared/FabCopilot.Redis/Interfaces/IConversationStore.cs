using FabCopilot.Contracts.Models;

namespace FabCopilot.Redis.Interfaces;

public interface IConversationStore
{
    Task<Conversation?> GetAsync(string conversationId, CancellationToken ct = default);
    Task SaveAsync(Conversation conversation, CancellationToken ct = default);
    Task AppendMessageAsync(string conversationId, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetByEquipmentAsync(string equipmentId, int limit = 20, CancellationToken ct = default);
}
