using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// Role-based access policy for equipment data and features.
/// Defines what each role can access.
/// </summary>
public static class AccessPolicy
{
    /// <summary>Session timeout: idle 30 minutes.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Session timeout: maximum 8 hours.</summary>
    public static readonly TimeSpan MaxSessionDuration = TimeSpan.FromHours(8);

    /// <summary>
    /// Checks if a user role has access to a specific feature.
    /// </summary>
    public static bool HasAccess(UserRole role, Feature feature)
    {
        return feature switch
        {
            // Read-only features: all roles
            Feature.ChatQuery => true,
            Feature.ViewDocumentation => true,

            // Equipment data: maintenance engineer and above
            Feature.ViewSensorData => role >= UserRole.MaintenanceEngineer,
            Feature.ViewAlarmHistory => role >= UserRole.MaintenanceEngineer,
            Feature.ViewPmHistory => role >= UserRole.MaintenanceEngineer,
            Feature.ViewDashboard => role >= UserRole.MaintenanceEngineer,

            // Advanced features: senior engineer and above
            Feature.ViewParameterOptimization => role >= UserRole.SeniorEngineer,
            Feature.ViewTrendAnalysis => role >= UserRole.SeniorEngineer,
            Feature.ViewAnomalyDetection => role >= UserRole.SeniorEngineer,
            Feature.RunRca => role >= UserRole.SeniorEngineer,

            // Unredacted output: equipment owner only
            Feature.ViewUnredactedOutput => role >= UserRole.EquipmentOwner,

            // Admin features: admin only
            Feature.ManageUsers => role >= UserRole.Admin,
            Feature.ManageEquipment => role >= UserRole.Admin,
            Feature.ViewAuditLog => role >= UserRole.Admin,
            Feature.ConfigureSystem => role >= UserRole.Admin,

            _ => false
        };
    }

    /// <summary>
    /// Checks if a user has access to a specific equipment.
    /// Equipment owners and admins can access any equipment;
    /// others must have the equipment in their access list.
    /// </summary>
    public static bool CanAccessEquipment(UserIdentity user, string equipmentId)
    {
        if (user.Role >= UserRole.Admin) return true;
        if (user.EquipmentAccess.Count == 0) return true; // Empty = all access
        return user.EquipmentAccess.Contains(equipmentId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a session is still valid.
    /// </summary>
    public static bool IsSessionValid(UserIdentity user)
    {
        var now = DateTimeOffset.UtcNow;
        return now < user.SessionExpiresAt;
    }
}

/// <summary>
/// System features that can be gated by role.
/// </summary>
public enum Feature
{
    ChatQuery,
    ViewDocumentation,
    ViewSensorData,
    ViewAlarmHistory,
    ViewPmHistory,
    ViewDashboard,
    ViewParameterOptimization,
    ViewTrendAnalysis,
    ViewAnomalyDetection,
    RunRca,
    ViewUnredactedOutput,
    ManageUsers,
    ManageEquipment,
    ViewAuditLog,
    ConfigureSystem
}
