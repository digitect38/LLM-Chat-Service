using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies that key facts from cmp-process-manual.md survive chunking
/// and appear correctly in the LLM system prompt.
/// </summary>
public class CmpProcessManualContentTests
{
    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", "cmp-process-manual.md");

    private static readonly Lazy<string> RawText = new(() => File.ReadAllText(DocPath));
    private static readonly Lazy<List<string>> Chunks = new(() =>
        DocumentIngestor.ChunkText(RawText.Value, 512, 128));

    private static bool AnyChunkContains(string keyword)
        => Chunks.Value.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string BuildPromptWith(string chunkText)
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = chunkText,
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-process-manual.md" }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // === 1. 공정 개요 ===

    [Fact]
    public void Chunk_Contains_CMP_Definition()
        => AnyChunkContains("Chemical Mechanical Planarization").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ILD_Purpose()
        => AnyChunkContains("ILD").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Cu_Purpose()
        => AnyChunkContains("Cu").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_STI_Purpose()
        => AnyChunkContains("STI").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ShallowTrenchIsolation()
        => AnyChunkContains("Shallow Trench Isolation").Should().BeTrue();

    // === 2. Preston 방정식 ===

    [Fact]
    public void Chunk_Contains_Preston_Equation()
        => AnyChunkContains("Preston").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MRR_Formula_Kp()
        => AnyChunkContains("Kp").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PrestonCoefficient()
        => AnyChunkContains("Preston 계수").Should().BeTrue();

    // === 3. 공정 단계 ===

    [Fact]
    public void Chunk_Contains_FOUP()
        => AnyChunkContains("FOUP").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_VacuumChucking()
        => AnyChunkContains("진공 척킹").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RetainingRing()
        => AnyChunkContains("리테이닝 링").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryFlowRate_150_250()
        => AnyChunkContains("150~250 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_PlatenSpeed_60_120()
        => AnyChunkContains("60~120 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CarrierSpeed_50_100()
        => AnyChunkContains("50~100 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_ZonePressure_1_5_to_6_0()
        => AnyChunkContains("1.5~6.0 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EPD()
        => AnyChunkContains("EPD").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EndPointDetection()
        => AnyChunkContains("End Point Detection").Should().BeTrue();

    // === 4. 린스 ===

    [Fact]
    public void Chunk_Contains_Rinse_DIWater_500ml()
        => AnyChunkContains("500 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Rinse_15Sec()
        => AnyChunkContains("15초").Should().BeTrue();

    // === 5. 언로딩 ===

    [Fact]
    public void Chunk_Contains_BlowOff()
        => AnyChunkContains("blow-off").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MegasonicCleaning()
        => AnyChunkContains("메가소닉").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SpinDry()
        => AnyChunkContains("스핀 드라이").Should().BeTrue();

    // === 6. Zone 맵 ===

    [Fact]
    public void Chunk_Contains_Zone1_Center()
        => AnyChunkContains("Zone 1").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone1_Range_2_0_4_0()
        => AnyChunkContains("2.0~4.0 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone3_Middle()
        => AnyChunkContains("Zone 3").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone5_Edge()
        => AnyChunkContains("Zone 5").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Zone5_Range_3_0_6_0()
        => AnyChunkContains("3.0~6.0 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RetainingRing_4_0_8_0()
        => AnyChunkContains("4.0~8.0 psi").Should().BeTrue();

    // === 7. 압력 설정 원칙 ===

    [Fact]
    public void Chunk_Contains_WIWNU_Target_3Percent()
        => AnyChunkContains("3%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_RingPressureRatio_1_2_1_5()
        => AnyChunkContains("1.2~1.5배").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_NewPad_5Percent_Lower()
        => AnyChunkContains("5% 낮게").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_BreakIn_10Wafers()
        => AnyChunkContains("초기 10매").Should().BeTrue();

    // === 8. 슬러리 종류 ===

    [Fact]
    public void Chunk_Contains_OxideSlurry_pH_10_11()
        => AnyChunkContains("10~11").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MetalSlurry_pH_3_4()
        => AnyChunkContains("3~4").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TungstenSlurry_pH_2_3()
        => AnyChunkContains("2~3").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxideSlurry_200ml()
        => AnyChunkContains("200 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_MetalSlurry_150ml()
        => AnyChunkContains("150 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_TungstenSlurry_180ml()
        => AnyChunkContains("180 ml/min").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxideParticle_50_200nm()
        => AnyChunkContains("50~200 nm").Should().BeTrue();

    // === 9. 슬러리 관리 ===

    [Fact]
    public void Chunk_Contains_SlurryTemp_20_25()
        => AnyChunkContains("20~25").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryLinePressure_15_25()
        => AnyChunkContains("15~25 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryExpiry_72Hours()
        => AnyChunkContains("72시간").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_SlurryStir_30Min()
        => AnyChunkContains("30분 교반").Should().BeTrue();

    // === 10. EPD ===

    [Fact]
    public void Chunk_Contains_MotorCurrent_EPD()
        => AnyChunkContains("Motor Current").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_Optical_EPD()
        => AnyChunkContains("Optical").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EPD_MotorCurrentChangeRate_5Percent()
        => AnyChunkContains("5%").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EPD_OverPolish_10_30sec()
        => AnyChunkContains("10~30초").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_EPD_MaxTime_150Percent()
        => AnyChunkContains("150%").Should().BeTrue();

    // === 11. Oxide CMP 표준 레시피 ===

    [Fact]
    public void Chunk_Contains_OxideCMP_PlatenSpeed_93rpm()
        => AnyChunkContains("93 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxideCMP_CarrierSpeed_87rpm()
        => AnyChunkContains("87 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxideCMP_RingPressure_5_0psi()
        => AnyChunkContains("5.0 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_OxideCMP_ConditionerDownforce_5lbf()
        => AnyChunkContains("5 lbf").Should().BeTrue();

    // === 12. Cu CMP 표준 레시피 ===

    [Fact]
    public void Chunk_Contains_CuCMP_PlatenSpeed_80rpm()
        => AnyChunkContains("80 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CuCMP_CarrierSpeed_75rpm()
        => AnyChunkContains("75 rpm").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CuCMP_RingPressure_4_5psi()
        => AnyChunkContains("4.5 psi").Should().BeTrue();

    [Fact]
    public void Chunk_Contains_CuCMP_EPD_Plus_15s()
        => AnyChunkContains("EPD + 15s").Should().BeTrue();

    // === 13. BuildSystemPrompt 검증 ===

    [Fact]
    public void Prompt_Contains_ChunkText_OxideRecipe()
    {
        var prompt = BuildPromptWith("Oxide CMP 표준 레시피: Platen Speed 93 rpm, Carrier Speed 87 rpm");
        prompt.Should().Contain("93 rpm");
        prompt.Should().Contain("87 rpm");
    }

    [Fact]
    public void Prompt_Contains_ChunkText_ZonePressure()
    {
        var prompt = BuildPromptWith("Zone 1~5 Pressure 3.0 psi, Retaining Ring Pressure 5.0 psi");
        prompt.Should().Contain("3.0 psi");
        prompt.Should().Contain("5.0 psi");
    }

    [Fact]
    public void Prompt_Contains_ChunkText_NotFileName()
    {
        var prompt = BuildPromptWith("CMP 공정 매뉴얼 내용");
        prompt.Should().NotContain("cmp-process-manual.md");
        prompt.Should().Contain("CMP 공정 매뉴얼 내용");
    }
}
