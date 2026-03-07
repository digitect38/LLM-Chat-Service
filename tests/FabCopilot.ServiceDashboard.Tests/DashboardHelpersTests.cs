using FabCopilot.ServiceDashboard.Helpers;
using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Tests;

public class DashboardHelpersTests
{
    // ─── GetLogLevelClass ─────────────────────────────────────

    [Theory]
    [InlineData("[ERROR] Something failed")]
    [InlineData("[error] connection refused")]
    [InlineData("Error: timeout")]
    [InlineData("NullReferenceException occurred")]
    [InlineData("System.Exception: test")]
    [InlineData("EXCEPTION in handler")]
    [InlineData("Request failed with status 500")]
    [InlineData("FAIL: test case")]
    public void GetLogLevelClass_ErrorLines_ReturnsLogError(string line)
    {
        DashboardHelpers.GetLogLevelClass(line).Should().Be("log-error");
    }

    [Theory]
    [InlineData("[WARN] Slow query")]
    [InlineData("[warn] deprecation notice")]
    [InlineData("Warning: low memory")]
    [InlineData("WARNING: retrying")]
    public void GetLogLevelClass_WarnLines_ReturnsLogWarn(string line)
    {
        DashboardHelpers.GetLogLevelClass(line).Should().Be("log-warn");
    }

    [Theory]
    [InlineData("[INFO] Service started")]
    [InlineData("[DEBUG] Processing request")]
    [InlineData("Connection established")]
    [InlineData("")]
    [InlineData("200 OK")]
    public void GetLogLevelClass_NormalLines_ReturnsEmpty(string line)
    {
        DashboardHelpers.GetLogLevelClass(line).Should().BeEmpty();
    }

    [Fact]
    public void GetLogLevelClass_ErrorTakesPriorityOverWarn()
    {
        // A line containing both "error" and "warn" should be classified as error
        var line = "[WARN] Error occurred during processing";
        DashboardHelpers.GetLogLevelClass(line).Should().Be("log-error");
    }

    [Fact]
    public void GetLogLevelClass_CaseInsensitive()
    {
        DashboardHelpers.GetLogLevelClass("eRrOr in module").Should().Be("log-error");
        DashboardHelpers.GetLogLevelClass("wArNiNg: disk space").Should().Be("log-warn");
    }

    // ─── GetStateClass ────────────────────────────────────────

    [Fact]
    public void GetStateClass_Up_ReturnsStateUp()
    {
        DashboardHelpers.GetStateClass(ServiceState.Up).Should().Be("state-up");
    }

    [Fact]
    public void GetStateClass_Down_ReturnsStateDown()
    {
        DashboardHelpers.GetStateClass(ServiceState.Down).Should().Be("state-down");
    }

    [Fact]
    public void GetStateClass_Degraded_ReturnsStateDegraded()
    {
        DashboardHelpers.GetStateClass(ServiceState.Degraded).Should().Be("state-degraded");
    }

    [Fact]
    public void GetStateClass_Unknown_ReturnsStateUnknown()
    {
        DashboardHelpers.GetStateClass(ServiceState.Unknown).Should().Be("state-unknown");
    }

    [Fact]
    public void GetStateClass_AllEnumValues_AreHandled()
    {
        foreach (ServiceState state in Enum.GetValues<ServiceState>())
        {
            var result = DashboardHelpers.GetStateClass(state);
            result.Should().NotBeNullOrEmpty($"state '{state}' should map to a CSS class");
            result.Should().StartWith("state-");
        }
    }
}
