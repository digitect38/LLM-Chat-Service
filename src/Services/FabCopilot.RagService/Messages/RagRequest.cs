namespace FabCopilot.RagService.Messages;

public sealed class RagRequest
{
    public string Query { get; set; } = string.Empty;
    public string EquipmentId { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public string? ConversationId { get; set; }
}
