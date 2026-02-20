using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class RcaRunResult
{
    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("alarmCode")]
    public string AlarmCode { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("rankedCauses")]
    public List<RcaCause> RankedCauses { get; set; } = [];

    [JsonPropertyName("recommendedActions")]
    public List<string> RecommendedActions { get; set; } = [];
}

public sealed class RcaCause
{
    [JsonPropertyName("cause")]
    public string Cause { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = [];
}
