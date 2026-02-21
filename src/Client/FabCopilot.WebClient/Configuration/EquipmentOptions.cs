namespace FabCopilot.WebClient.Configuration;

public class EquipmentOptions
{
    public const string SectionName = "Equipment";
    public string Default { get; set; } = "CMP01";
    public List<EquipmentEntry> Available { get; set; } = [];
}

public class EquipmentEntry
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
