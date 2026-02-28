using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// User identity with role-based access control (RBAC).
/// Roles determine access to equipment data, admin features, and unredacted output.
/// </summary>
public sealed class UserIdentity
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("role")]
    public UserRole Role { get; set; } = UserRole.Operator;

    [JsonPropertyName("equipment_access")]
    public List<string> EquipmentAccess { get; set; } = [];

    [JsonPropertyName("fab")]
    public string Fab { get; set; } = "";

    [JsonPropertyName("department")]
    public string Department { get; set; } = "";

    [JsonPropertyName("authenticated_at")]
    public DateTimeOffset AuthenticatedAt { get; set; }

    [JsonPropertyName("session_expires_at")]
    public DateTimeOffset SessionExpiresAt { get; set; }
}

/// <summary>
/// User roles ordered by privilege level (ascending).
/// </summary>
public enum UserRole
{
    /// <summary>Basic operator: read-only access, DLP-redacted output.</summary>
    Operator = 0,

    /// <summary>Maintenance engineer: access to PM history, troubleshooting guides.</summary>
    MaintenanceEngineer = 1,

    /// <summary>Senior engineer: access to all technical data, parameter optimization.</summary>
    SeniorEngineer = 2,

    /// <summary>Equipment owner: full access including unredacted DLP output.</summary>
    EquipmentOwner = 3,

    /// <summary>Administrator: system configuration, user management.</summary>
    Admin = 4
}
