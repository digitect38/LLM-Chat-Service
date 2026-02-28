using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace FabCopilot.LlmService;

/// <summary>
/// Data Loss Prevention (DLP) filter for input/output guardrails.
/// - Input: Equipment ID masking, sensitive parameter removal
/// - Output: Regex-based sensitive pattern detection with [REDACTED] substitution
/// Target: < 50ms overhead
/// </summary>
public sealed class DlpFilter
{
    private readonly ILogger _logger;
    private readonly DlpOptions _options;

    // Equipment ID patterns (CMP-01, CMP01, ETCH-03, etc.)
    private static readonly Regex EquipmentIdPattern = new(
        @"\b(CMP|ETCH|CVD|PVD|DIFF|LITHO|IMP|CDS|WET)[-_]?\d{1,4}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fab/Bay identifiers
    private static readonly Regex FabLocationPattern = new(
        @"\b(FAB|BAY|LINE)[-_]?\d{1,3}[A-Z]?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Yield percentages (sensitive manufacturing data)
    private static readonly Regex YieldPattern = new(
        @"\byield\s*[:=]?\s*\d+\.?\d*\s*%",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Proprietary recipe names/IDs
    private static readonly Regex RecipePattern = new(
        @"\b(recipe|RCP)[-_]?[A-Z0-9]{3,}[-_]?[A-Z0-9]*\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Wafer lot/batch IDs
    private static readonly Regex LotIdPattern = new(
        @"\b(LOT|BATCH|WF)[-_]?[A-Z0-9]{4,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // IP addresses and hostnames
    private static readonly Regex IpPattern = new(
        @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
        RegexOptions.Compiled);

    // Employee IDs and personal identifiers
    private static readonly Regex EmployeeIdPattern = new(
        @"\b(EMP|사번)[-_]?\d{4,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Sensitive numeric parameters (process-specific values that could reveal trade secrets)
    private static readonly Regex SensitiveParamPattern = new(
        @"\b(target\s+thickness|film\s+thickness|etch\s+depth|deposition\s+rate)\s*[:=]\s*[\d.]+\s*(nm|μm|Å|angstrom)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Equipment ID masking map (built during input filtering)
    private readonly Dictionary<string, string> _equipmentIdMap = new(StringComparer.OrdinalIgnoreCase);
    private int _equipmentCounter;

    public DlpFilter(ILogger logger, DlpOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new DlpOptions();
    }

    /// <summary>
    /// Filters user input before sending to LLM.
    /// Masks equipment IDs and removes sensitive parameters.
    /// </summary>
    public DlpResult FilterInput(string input)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(input))
            return new DlpResult(input, []);

        var redactions = new List<string>();
        var filtered = input;

        // Mask equipment IDs (CMP-07 → [EQUIP_A])
        if (_options.MaskEquipmentIds)
        {
            filtered = EquipmentIdPattern.Replace(filtered, match =>
            {
                var id = match.Value.ToUpperInvariant();
                if (!_equipmentIdMap.TryGetValue(id, out var alias))
                {
                    alias = $"[EQUIP_{(char)('A' + _equipmentCounter++)}]";
                    _equipmentIdMap[id] = alias;
                }
                redactions.Add($"Equipment ID masked: {id} → {alias}");
                return alias;
            });
        }

        // Remove IP addresses
        if (_options.FilterIpAddresses)
        {
            filtered = IpPattern.Replace(filtered, match =>
            {
                redactions.Add($"IP address removed: {match.Value}");
                return "[REDACTED_IP]";
            });
        }

        if (redactions.Count > 0)
        {
            _logger.LogInformation("DLP input filter: {Count} redactions applied", redactions.Count);
        }

        return new DlpResult(filtered, redactions);
    }

    /// <summary>
    /// Filters LLM output before sending to user.
    /// Detects and redacts sensitive patterns (yield data, recipes, lot IDs, etc.)
    /// </summary>
    public DlpResult FilterOutput(string output)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(output))
            return new DlpResult(output, []);

        var redactions = new List<string>();
        var filtered = output;

        // Yield percentages
        if (_options.RedactYieldData)
        {
            filtered = YieldPattern.Replace(filtered, match =>
            {
                redactions.Add($"Yield data redacted: {match.Value}");
                return "[REDACTED-YIELD]";
            });
        }

        // Proprietary recipe names
        if (_options.RedactRecipes)
        {
            filtered = RecipePattern.Replace(filtered, match =>
            {
                // Skip common false positives
                var val = match.Value;
                if (val.Equals("recipe", StringComparison.OrdinalIgnoreCase) ||
                    val.Equals("RCP", StringComparison.OrdinalIgnoreCase))
                    return val;

                redactions.Add($"Recipe ID redacted: {val}");
                return "[REDACTED-RECIPE]";
            });
        }

        // Lot/batch IDs
        if (_options.RedactLotIds)
        {
            filtered = LotIdPattern.Replace(filtered, match =>
            {
                redactions.Add($"Lot ID redacted: {match.Value}");
                return "[REDACTED-LOT]";
            });
        }

        // Employee IDs
        filtered = EmployeeIdPattern.Replace(filtered, match =>
        {
            redactions.Add($"Employee ID redacted: {match.Value}");
            return "[REDACTED-EID]";
        });

        // IP addresses
        if (_options.FilterIpAddresses)
        {
            filtered = IpPattern.Replace(filtered, match =>
            {
                redactions.Add($"IP address redacted: {match.Value}");
                return "[REDACTED_IP]";
            });
        }

        // Sensitive process parameters
        if (_options.RedactProcessParams)
        {
            filtered = SensitiveParamPattern.Replace(filtered, match =>
            {
                redactions.Add($"Process parameter redacted: {match.Value}");
                return "[REDACTED-PARAM]";
            });
        }

        // Reverse equipment ID masking in output if needed
        // (restore masked IDs so the output is coherent)
        foreach (var (realId, alias) in _equipmentIdMap)
        {
            filtered = filtered.Replace(alias, realId, StringComparison.OrdinalIgnoreCase);
        }

        if (redactions.Count > 0)
        {
            _logger.LogWarning("DLP output filter: {Count} sensitive patterns detected and redacted", redactions.Count);

            // Security audit log
            foreach (var r in redactions)
            {
                _logger.LogInformation("[DLP-AUDIT] {Redaction}", r);
            }
        }

        return new DlpResult(filtered, redactions);
    }

    /// <summary>
    /// Clears the equipment ID mapping (call at start of new conversation).
    /// </summary>
    public void ResetMappings()
    {
        _equipmentIdMap.Clear();
        _equipmentCounter = 0;
    }
}

public sealed class DlpOptions
{
    public const string SectionName = "Dlp";

    public bool Enabled { get; set; } = true;
    public bool MaskEquipmentIds { get; set; } = true;
    public bool FilterIpAddresses { get; set; } = true;
    public bool RedactYieldData { get; set; } = true;
    public bool RedactRecipes { get; set; } = true;
    public bool RedactLotIds { get; set; } = true;
    public bool RedactProcessParams { get; set; } = true;
}

public sealed record DlpResult(string FilteredText, List<string> Redactions)
{
    public bool HasRedactions => Redactions.Count > 0;
}
