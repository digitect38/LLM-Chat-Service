using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies cmp-metrology-inspection.md with strict document → chapter → section → line mapping.
/// </summary>
public class CmpMetrologyInspectionContentTests
{
    private const string FileName = "cmp-metrology-inspection.md";

    private static readonly string DocPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs", FileName);

    private static readonly Lazy<string> RawText = new(() => File.ReadAllText(DocPath));
    private static readonly Lazy<List<string>> MdChunks = new(() =>
        DocumentIngestor.ChunkMarkdown(RawText.Value, 512, 128));

    private static string? FindChunk(string kw1, string kw2)
        => MdChunks.Value.FirstOrDefault(c =>
            c.Contains(kw1, StringComparison.OrdinalIgnoreCase) &&
            c.Contains(kw2, StringComparison.OrdinalIgnoreCase));

    private static string? FindChunk(string kw)
        => MdChunks.Value.FirstOrDefault(c =>
            c.Contains(kw, StringComparison.OrdinalIgnoreCase));

    private static void AssertContext(string chunk, string expectedChapter, string? expectedSection = null)
    {
        var ctx = DocumentIngestor.ExtractParentContext(chunk);
        ctx.Should().NotBeNull();
        ctx!.Should().Contain(expectedChapter);
        if (expectedSection is not null)
            ctx.Should().Contain(expectedSection);
    }

    private static string BuildPromptWith(string chunkText)
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = chunkText, Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = FileName }
            }
        };
        return LlmWorker.BuildSystemPrompt("CMP-001", null, results);
    }

    // ═══ §1. 두께 측정 ═══

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "Ellipsometer는 투명막(SiO2, SiN) 두께를 광학적으로 측정(정확도 ±1Å)"
    public void S1_1_Ellipsometer_SiO2_1A()
    {
        var chunk = FindChunk("Ellipsometer", "±1Å");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Ellipsometer, ±1Å'");
        AssertContext(chunk!, "두께 측정", "측정 장비");
    }

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "4-point probe는 금속막(Cu, W)의 면저항(Rs)"
    public void S1_1_4PointProbe_Rs()
    {
        var chunk = FindChunk("4-point probe", "Rs");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain '4-point probe, Rs'");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "49점 맵: 양산 모니터링 표준...Edge Exclusion은 3mm"
    public void S1_2_49PointMap_3mmExclusion()
    {
        var chunk = FindChunk("49점 맵", "3mm");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain '49점 맵, Edge Exclusion 3mm'");
        AssertContext(chunk!, "두께 측정", "측정 포인트");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "25점 맵: 일일 Qualification용"
    public void S1_2_25PointMap_DailyQual()
    {
        var chunk = FindChunk("25점 맵", "Qualification");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain '25점 맵, Qualification'");
    }

    [Fact] // Ch: 1 | Sec: 1.3 | Line: "MRR(Å/min) = (Pre-thickness - Post-thickness) / Polish Time"
    public void S1_3_MRR_Formula()
    {
        var chunk = FindChunk("Å/min", "Pre-thickness");
        chunk.Should().NotBeNull($"{FileName} > §1.3 should contain 'Å/min, Pre-thickness formula'");
        AssertContext(chunk!, "두께 측정", "MRR 계산");
    }

    // ═══ §2. 균일도 계산 ═══

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "WIWNU(%) = (σ / Mean) × 100...목표: WIWNU < 3%"
    public void S2_1_WIWNU_Formula_3Percent()
    {
        var chunk = FindChunk("WIWNU", "< 3%");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain 'WIWNU < 3%'");
        AssertContext(chunk!, "균일도 계산", "WIWNU");
    }

    [Fact] // Ch: 2 | Sec: 2.2 | Line: "WTWNU...목표: WTWNU < 2%"
    public void S2_2_WTWNU_2Percent()
    {
        var chunk = FindChunk("WTWNU", "< 2%");
        chunk.Should().NotBeNull($"{FileName} > §2.2 should contain 'WTWNU < 2%'");
        AssertContext(chunk!, "균일도 계산", "WTWNU");
    }

    [Fact] // Ch: 2 | Sec: 2.3 | Line: "Center-Edge Range = |Center avg - Edge avg|"
    public void S2_3_CenterEdgeRange()
    {
        var chunk = FindChunk("Center-Edge Range", "Center avg");
        chunk.Should().NotBeNull($"{FileName} > §2.3 should contain 'Center-Edge Range formula'");
        AssertContext(chunk!, "균일도 계산", "Range");
    }

    // ═══ §3. 결함 검사 ═══

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "KLA Surfscan...Dark Field(파티클 감도 높음), Light Field(패턴 결함)"
    public void S3_1_KLA_DarkField_LightField()
    {
        var chunk = FindChunk("KLA", "Dark Field");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain 'KLA, Dark Field'");
        AssertContext(chunk!, "결함 검사", "Surfscan");
    }

    [Fact] // Ch: 3 | Sec: 3.4 | Line: "Oxide CMP: 총 결함 수 < 20개/웨이퍼...스크래치 0개"
    public void S3_4_OxideCMP_Defect_20_Scratch0()
    {
        var chunk = FindChunk("< 20개", "스크래치 0개");
        chunk.Should().NotBeNull($"{FileName} > §3.4 should contain 'Oxide < 20, 스크래치 0'");
        AssertContext(chunk!, "결함 검사", "결함 허용 기준");
    }

    [Fact] // Ch: 3 | Sec: 3.4 | Line: "Cu CMP: < 30개/웨이퍼"
    public void S3_4_CuCMP_Defect_30()
    {
        var chunk = FindChunk("< 30개", "Cu CMP");
        chunk.Should().NotBeNull($"{FileName} > §3.4 should contain 'Cu CMP < 30'");
    }

    [Fact] // Ch: 3 | Sec: 3.4 | Line: "STI CMP: < 15개/웨이퍼"
    public void S3_4_STICMP_Defect_15()
    {
        var chunk = FindChunk("< 15개", "STI");
        chunk.Should().NotBeNull($"{FileName} > §3.4 should contain 'STI < 15'");
    }

    // ═══ §4. 표면 분석 ═══

    [Fact] // Ch: 4 | Sec: 4.1 | Line: "AFM...Ra < 0.5nm"
    public void S4_1_AFM_Ra_0_5nm()
    {
        var chunk = FindChunk("AFM", "0.5nm");
        chunk.Should().NotBeNull($"{FileName} > §4.1 should contain 'AFM, Ra 0.5nm'");
        AssertContext(chunk!, "표면 분석", "AFM");
    }

    [Fact] // Ch: 4 | Sec: 4.2 | Line: "FIB로 단면 가공...Dishing 목표: < 500Å"
    public void S4_2_SEM_FIB_Dishing_500A()
    {
        var chunk = FindChunk("FIB", "500Å");
        chunk.Should().NotBeNull($"{FileName} > §4.2 should contain 'FIB, Dishing 500Å'");
        AssertContext(chunk!, "표면 분석", "SEM 단면");
    }

    [Fact] // Ch: 4 | Sec: 4.3 | Line: "평탄화율(%) = (1 - Step_after/Step_before) × 100...> 90%"
    public void S4_3_Profilometer_PlanarRate_90Percent()
    {
        var chunk = FindChunk("Profilometer", "90%");
        chunk.Should().NotBeNull($"{FileName} > §4.3 should contain 'Profilometer, 90%'");
        AssertContext(chunk!, "표면 분석", "Profilometer");
    }

    // ═══ §5. EPD 신호 분석 ═══

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "Motor Current EPD...토크 변화율이 설정 임계값(기본 5%)"
    public void S5_1_MotorCurrentEPD_5Percent()
    {
        var chunk = FindChunk("Motor Current EPD", "5%");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain 'Motor Current EPD, 5% threshold'");
        AssertContext(chunk!, "EPD 신호 분석", "Motor Current");
    }

    [Fact] // Ch: 5 | Sec: 5.2 | Line: "Optical EPD...Over-polish 시간(10~30초)"
    public void S5_2_OpticalEPD_OverPolish_10_30sec()
    {
        var chunk = FindChunk("Optical EPD", "10~30초");
        chunk.Should().NotBeNull($"{FileName} > §5.2 should contain 'Optical EPD, 10~30초'");
        AssertContext(chunk!, "EPD 신호 분석", "Optical EPD");
    }

    // ═══ §6. 공정별 품질 판정 기준 ═══

    [Fact] // Ch: 6 | Sec: 6.1 | Line: "Oxide CMP: MRR 3500~4000(목표 3800)...WIWNU < 3%"
    public void S6_1_OxideCMP_MRR_3800_WIWNU_3()
    {
        var chunk = FindChunk("3800", "< 3%");
        chunk.Should().NotBeNull($"{FileName} > §6.1 should contain 'Oxide MRR 3800, WIWNU < 3%'");
        AssertContext(chunk!, "품질 판정 기준", "Oxide CMP");
    }

    [Fact] // Ch: 6 | Sec: 6.2 | Line: "Cu CMP Step 1 MRR: 5000~7000...Dishing < 500Å"
    public void S6_2_CuCMP_MRR_5000_7000_Dishing_500A()
    {
        var chunk = FindChunk("5000~7000", "500Å");
        chunk.Should().NotBeNull($"{FileName} > §6.2 should contain 'Cu MRR 5000~7000, Dishing 500Å'");
        AssertContext(chunk!, "품질 판정 기준", "Cu CMP");
    }

    [Fact] // Ch: 6 | Sec: 6.3 | Line: "W CMP MRR: 2000~3000...W Recess < 300Å"
    public void S6_3_WCMP_MRR_2000_3000_Recess_300A()
    {
        var chunk = FindChunk("2000~3000", "300Å");
        chunk.Should().NotBeNull($"{FileName} > §6.3 should contain 'W MRR 2000~3000, Recess 300Å'");
        AssertContext(chunk!, "품질 판정 기준", "W CMP");
    }

    [Fact] // Ch: 6 | Sec: 6.4 | Line: "SiO2:SiN 선택비 > 30:1(Ceria 슬러리)"
    public void S6_4_STI_Selectivity_30_1_Ceria()
    {
        var chunk = FindChunk("30:1", "Ceria");
        chunk.Should().NotBeNull($"{FileName} > §6.4 should contain 'selectivity 30:1, Ceria'");
        AssertContext(chunk!, "품질 판정 기준", "STI CMP");
    }

    [Fact] // Ch: 6 | Sec: 6.5 | Line: "해당 웨이퍼/로트 즉시 Hold"
    public void S6_5_OutOfSpec_Hold()
    {
        var chunk = FindChunk("Hold", "재측정");
        chunk.Should().NotBeNull($"{FileName} > §6.5 should contain 'Hold, 재측정'");
        AssertContext(chunk!, "품질 판정 기준", "Out-of-Spec");
    }

    // ═══ Prompt ═══

    [Fact]
    public void Prompt_Contains_MetrologyInfoAndFileName()
    {
        var prompt = BuildPromptWith("WIWNU(%) = (σ / Mean) × 100, 목표 < 3%, 양산 경고 > 5%");
        prompt.Should().Contain("WIWNU");
        prompt.Should().NotContain(FileName);
    }
}
