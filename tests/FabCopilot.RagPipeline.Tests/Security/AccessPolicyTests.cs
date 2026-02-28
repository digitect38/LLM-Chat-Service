using FabCopilot.Contracts.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Security;

/// <summary>
/// Tests for role-based access control (RBAC) policies.
/// </summary>
public class AccessPolicyTests
{
    // ── Chat Access (All Roles) ──────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.MaintenanceEngineer)]
    [InlineData(UserRole.SeniorEngineer)]
    [InlineData(UserRole.EquipmentOwner)]
    [InlineData(UserRole.Admin)]
    public void ChatQuery_AllRoles_HaveAccess(UserRole role)
    {
        AccessPolicy.HasAccess(role, Feature.ChatQuery).Should().BeTrue();
    }

    // ── Equipment Data (Maintenance+) ────────────────────────────────

    [Theory]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.MaintenanceEngineer, true)]
    [InlineData(UserRole.SeniorEngineer, true)]
    [InlineData(UserRole.EquipmentOwner, true)]
    [InlineData(UserRole.Admin, true)]
    public void ViewSensorData_RequiresMaintenanceOrAbove(UserRole role, bool expected)
    {
        AccessPolicy.HasAccess(role, Feature.ViewSensorData).Should().Be(expected);
    }

    [Theory]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.MaintenanceEngineer, true)]
    public void ViewDashboard_RequiresMaintenanceOrAbove(UserRole role, bool expected)
    {
        AccessPolicy.HasAccess(role, Feature.ViewDashboard).Should().Be(expected);
    }

    // ── Advanced Features (Senior+) ──────────────────────────────────

    [Theory]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.MaintenanceEngineer, false)]
    [InlineData(UserRole.SeniorEngineer, true)]
    [InlineData(UserRole.EquipmentOwner, true)]
    public void RunRca_RequiresSeniorOrAbove(UserRole role, bool expected)
    {
        AccessPolicy.HasAccess(role, Feature.RunRca).Should().Be(expected);
    }

    // ── Unredacted Output (EquipmentOwner+) ──────────────────────────

    [Theory]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.SeniorEngineer, false)]
    [InlineData(UserRole.EquipmentOwner, true)]
    [InlineData(UserRole.Admin, true)]
    public void ViewUnredactedOutput_RequiresEquipmentOwnerOrAbove(UserRole role, bool expected)
    {
        AccessPolicy.HasAccess(role, Feature.ViewUnredactedOutput).Should().Be(expected);
    }

    // ── Admin Features (Admin Only) ──────────────────────────────────

    [Theory]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.EquipmentOwner, false)]
    [InlineData(UserRole.Admin, true)]
    public void ManageUsers_RequiresAdmin(UserRole role, bool expected)
    {
        AccessPolicy.HasAccess(role, Feature.ManageUsers).Should().Be(expected);
    }

    // ── Equipment Access ─────────────────────────────────────────────

    [Fact]
    public void CanAccessEquipment_AdminHasFullAccess()
    {
        var admin = new UserIdentity
        {
            UserId = "admin-1", Role = UserRole.Admin,
            EquipmentAccess = ["CMP-01"] // Even with limited list, admin gets all
        };

        AccessPolicy.CanAccessEquipment(admin, "CMP-99").Should().BeTrue();
    }

    [Fact]
    public void CanAccessEquipment_EmptyAccessList_AllowsAll()
    {
        var user = new UserIdentity
        {
            UserId = "op-1", Role = UserRole.Operator,
            EquipmentAccess = []
        };

        AccessPolicy.CanAccessEquipment(user, "CMP-01").Should().BeTrue();
    }

    [Fact]
    public void CanAccessEquipment_RestrictedList_OnlyAllowsListed()
    {
        var user = new UserIdentity
        {
            UserId = "op-1", Role = UserRole.Operator,
            EquipmentAccess = ["CMP-01", "CMP-02"]
        };

        AccessPolicy.CanAccessEquipment(user, "CMP-01").Should().BeTrue();
        AccessPolicy.CanAccessEquipment(user, "CMP-03").Should().BeFalse();
    }

    [Fact]
    public void CanAccessEquipment_CaseInsensitive()
    {
        var user = new UserIdentity
        {
            UserId = "op-1", Role = UserRole.Operator,
            EquipmentAccess = ["CMP-01"]
        };

        AccessPolicy.CanAccessEquipment(user, "cmp-01").Should().BeTrue();
    }

    // ── Session Validity ─────────────────────────────────────────────

    [Fact]
    public void IsSessionValid_ActiveSession_ReturnsTrue()
    {
        var user = new UserIdentity
        {
            SessionExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        AccessPolicy.IsSessionValid(user).Should().BeTrue();
    }

    [Fact]
    public void IsSessionValid_ExpiredSession_ReturnsFalse()
    {
        var user = new UserIdentity
        {
            SessionExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        AccessPolicy.IsSessionValid(user).Should().BeFalse();
    }

    // ── Timeout Constants ────────────────────────────────────────────

    [Fact]
    public void IdleTimeout_Is30Minutes()
    {
        AccessPolicy.IdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void MaxSessionDuration_Is8Hours()
    {
        AccessPolicy.MaxSessionDuration.Should().Be(TimeSpan.FromHours(8));
    }
}
