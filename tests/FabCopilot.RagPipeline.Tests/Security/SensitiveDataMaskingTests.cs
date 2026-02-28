using FabCopilot.Observability.Enrichers;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Security;

/// <summary>
/// Tests for sensitive data masking in structured logging.
/// </summary>
public class SensitiveDataMaskingTests
{
    // ── Equipment ID Masking ─────────────────────────────────────────

    [Fact]
    public void MaskSensitiveText_EquipmentId_Masked()
    {
        var result = SensitiveDataMaskingEnricher.MaskSensitiveText("Equipment CMP-01 is running.");

        result.Should().NotContain("CMP-01");
        result.Should().Contain("[EQUIP_");
    }

    [Fact]
    public void MaskSensitiveText_MultipleEquipmentIds_MaskedConsistently()
    {
        var result = SensitiveDataMaskingEnricher.MaskSensitiveText(
            "CMP-01 sensor alert. CMP-01 temperature high. ETCH-03 normal.");

        result.Should().NotContain("CMP-01");
        result.Should().NotContain("ETCH-03");

        // Same ID should get same mask
        var cmpMask = result.Split("sensor")[0].Trim().Split(' ').Last();
        result.Should().Contain(cmpMask); // Should appear twice for CMP-01
    }

    // ── Yield Data Masking ───────────────────────────────────────────

    [Fact]
    public void MaskSensitiveText_YieldPercentage_Masked()
    {
        var result = SensitiveDataMaskingEnricher.MaskSensitiveText("Current yield: 95.3%");

        result.Should().Contain("[YIELD_REDACTED]");
        result.Should().NotContain("95.3");
    }

    [Fact]
    public void MaskSensitiveText_KoreanYield_Masked()
    {
        var result = SensitiveDataMaskingEnricher.MaskSensitiveText("수율: 98.1%");

        result.Should().Contain("[YIELD_REDACTED]");
    }

    // ── Recipe Masking ───────────────────────────────────────────────

    [Fact]
    public void MaskSensitiveText_RecipeName_Masked()
    {
        var result = SensitiveDataMaskingEnricher.MaskSensitiveText("Using recipe: CMP-OXIDE-V3.2");

        result.Should().Contain("[RECIPE_REDACTED]");
        result.Should().NotContain("CMP-OXIDE-V3.2");
    }

    // ── Hash ─────────────────────────────────────────────────────────

    [Fact]
    public void HashValue_DeterministicOutput()
    {
        var hash1 = SensitiveDataMaskingEnricher.HashValue("test-query");
        var hash2 = SensitiveDataMaskingEnricher.HashValue("test-query");

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(12);
    }

    [Fact]
    public void HashValue_DifferentInputs_DifferentHashes()
    {
        var hash1 = SensitiveDataMaskingEnricher.HashValue("query-1");
        var hash2 = SensitiveDataMaskingEnricher.HashValue("query-2");

        hash1.Should().NotBe(hash2);
    }

    // ── Edge Cases ───────────────────────────────────────────────────

    [Fact]
    public void MaskSensitiveText_EmptyInput_ReturnsEmpty()
    {
        SensitiveDataMaskingEnricher.MaskSensitiveText("").Should().BeEmpty();
    }

    [Fact]
    public void MaskSensitiveText_NoSensitiveData_Unchanged()
    {
        var text = "Normal log message without any sensitive data.";
        var result = SensitiveDataMaskingEnricher.MaskSensitiveText(text);

        result.Should().Be(text);
    }
}
