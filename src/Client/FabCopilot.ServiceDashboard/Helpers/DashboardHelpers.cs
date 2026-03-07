using FabCopilot.ServiceDashboard.Models;

namespace FabCopilot.ServiceDashboard.Helpers;

public static class DashboardHelpers
{
    /// <summary>
    /// CSS class for log level coloring.
    /// </summary>
    public static string GetLogLevelClass(string line)
    {
        if (line.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exception", StringComparison.OrdinalIgnoreCase))
            return "log-error";
        if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return "log-warn";
        return "";
    }

    /// <summary>
    /// CSS class for service state.
    /// </summary>
    public static string GetStateClass(ServiceState state) => state switch
    {
        ServiceState.Up => "state-up",
        ServiceState.Down => "state-down",
        ServiceState.Degraded => "state-degraded",
        _ => "state-unknown"
    };
}
