using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace FabCopilot.Observability.Enrichers;

/// <summary>
/// Serilog enricher that masks sensitive data in log properties at INFO level and above.
/// Converts raw queries to query_hash and masks equipment IDs, user IDs, and yield data.
/// </summary>
public sealed partial class SensitiveDataMaskingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Query", "UserQuery", "RawQuery", "SearchQuery",
        "UserId", "Username", "UserName",
        "Password", "Token", "ApiKey", "Secret"
    };

    // Equipment ID pattern: CMP-01, ETCH-03, etc.
    [GeneratedRegex(@"\b([A-Z]{2,6})-(\d{2,4})\b")]
    private static partial Regex EquipmentIdPattern();

    // Yield percentage pattern: 95.3%, yield: 98.1
    [GeneratedRegex(@"(?:yield|수율)\s*[:\-=]?\s*\d+\.?\d*\s*%?", RegexOptions.IgnoreCase)]
    private static partial Regex YieldPattern();

    // Recipe name pattern: proprietary recipe identifiers
    [GeneratedRegex(@"(?:recipe|레시피)\s*[:\-=]?\s*[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex RecipePattern();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Only mask at INFO and above (DEBUG is developer-facing)
        if (logEvent.Level < LogEventLevel.Information)
            return;

        var propertiesToUpdate = new List<(string Key, LogEventPropertyValue Value)>();

        foreach (var property in logEvent.Properties)
        {
            if (SensitivePropertyNames.Contains(property.Key))
            {
                // Hash the value instead of logging it raw
                var rawValue = property.Value.ToString().Trim('"');
                var hashed = HashValue(rawValue);
                var hashProperty = propertyFactory.CreateProperty(
                    property.Key + "_hash", hashed);
                propertiesToUpdate.Add((property.Key, hashProperty.Value));
            }
        }

        foreach (var (key, value) in propertiesToUpdate)
        {
            logEvent.RemovePropertyIfPresent(key);
            logEvent.AddOrUpdateProperty(new LogEventProperty(key + "_hash", value));
        }
    }

    /// <summary>
    /// Masks sensitive patterns in a text string (for use in message templates).
    /// </summary>
    public static string MaskSensitiveText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;

        // Mask equipment IDs: CMP-01 → [EQUIP_A]
        var equipmentMap = new Dictionary<string, string>();
        var equipCounter = 0;
        result = EquipmentIdPattern().Replace(result, m =>
        {
            var fullId = m.Value;
            if (!equipmentMap.TryGetValue(fullId, out var masked))
            {
                masked = $"[EQUIP_{(char)('A' + equipCounter++)}]";
                equipmentMap[fullId] = masked;
            }
            return masked;
        });

        // Mask yield data
        result = YieldPattern().Replace(result, "[YIELD_REDACTED]");

        // Mask recipe names
        result = RecipePattern().Replace(result, "[RECIPE_REDACTED]");

        return result;
    }

    /// <summary>
    /// Computes a short SHA-256 hash of the input for log correlation.
    /// </summary>
    public static string HashValue(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
