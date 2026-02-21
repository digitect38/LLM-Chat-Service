using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies cmp-defect-analysis.md with strict document → chapter → section → line mapping.
/// </summary>
public class CmpDefectAnalysisContentTests
{
    private const string FileName = "cmp-defect-analysis.md";

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

    // ═══ §1. 결함 분류 ═══

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "Micro-Scratch: 폭 < 0.5μm, 현미경으로만 관찰"
    public void S1_1_MicroScratch_0_5um_Microscope()
    {
        var chunk = FindChunk("Micro-Scratch", "0.5μm");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Micro-Scratch, 0.5μm'");
        AssertContext(chunk!, "결함 분류", "스크래치 유형");
    }

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "Chatter Mark: 등간격 반복 스크래치, 진동에 의해 발생"
    public void S1_1_ChatterMark_Vibration()
    {
        var chunk = FindChunk("Chatter Mark", "진동");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Chatter Mark, 진동'");
        AssertContext(chunk!, "결함 분류", "스크래치 유형");
    }

    [Fact] // Ch: 1 | Sec: 1.3 | Line: "BTA(Benzotriazole) 방식제 적용"
    public void S1_3_CuCorrosion_BTA_Benzotriazole()
    {
        var chunk = FindChunk("BTA", "Benzotriazole");
        chunk.Should().NotBeNull($"{FileName} > §1.3 should contain 'BTA Benzotriazole'");
        AssertContext(chunk!, "결함 분류", "부식/변색");
    }

    [Fact] // Ch: 1 | Sec: 1.4 | Line: "Dishing: Cu 배선이 주변 Oxide보다 오목하게 연마됨"
    public void S1_4_Dishing_CuLine()
    {
        var chunk = FindChunk("Dishing", "오목하게");
        chunk.Should().NotBeNull($"{FileName} > §1.4 should contain 'Dishing 오목'");
        AssertContext(chunk!, "결함 분류", "토폴로지");
    }

    // ═══ §2. 결함 패턴 분석 ═══

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "방사형(Radial)...컨디셔너 디스크의 다이아몬드 탈락"
    public void S2_1_RadialPattern_DiamondDislodge()
    {
        var chunk = FindChunk("방사형", "다이아몬드 탈락");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '방사형, 다이아몬드 탈락'");
        AssertContext(chunk!, "결함 패턴 분석", "방사형");
    }

    [Fact] // Ch: 2 | Sec: 2.2 | Line: "동심원(Concentric)...플래튼 진동(베어링 마모), TIR > 25μm"
    public void S2_2_ConcentricPattern_TIR_25um()
    {
        var chunk = FindChunk("동심원", "25μm");
        chunk.Should().NotBeNull($"{FileName} > §2.2 should contain '동심원, TIR 25μm'");
        AssertContext(chunk!, "결함 패턴 분석", "동심원");
    }

    [Fact] // Ch: 2 | Sec: 2.3 | Line: "랜덤(Random)...슬러리 내 대형 입자(응집체)"
    public void S2_3_RandomPattern_LargeParticle()
    {
        var chunk = FindChunk("랜덤", "응집체");
        chunk.Should().NotBeNull($"{FileName} > §2.3 should contain '랜덤, 응집체'");
        AssertContext(chunk!, "결함 패턴 분석", "랜덤");
    }

    [Fact] // Ch: 2 | Sec: 2.4 | Line: "에지(Edge)...Retaining Ring 마모(불균일 마모/편마모)"
    public void S2_4_EdgePattern_RingUneven()
    {
        var chunk = FindChunk("에지", "편마모");
        chunk.Should().NotBeNull($"{FileName} > §2.4 should contain '에지, 편마모'");
        AssertContext(chunk!, "결함 패턴 분석", "에지 집중");
    }

    [Fact] // Ch: 2 | Sec: 2.5 | Line: "Membrane 불균일...Backing Film 박리"
    public void S2_5_ZonePattern_Membrane_BackingFilm()
    {
        var chunk = FindChunk("Membrane", "Backing Film");
        chunk.Should().NotBeNull($"{FileName} > §2.5 should contain 'Membrane, Backing Film'");
        AssertContext(chunk!, "결함 패턴 분석");
    }

    // ═══ §3. 결함별 상세 분석 ═══

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "Wide Line(> 10μm): Dishing 크게 발생"
    public void S3_1_CuDishing_WideLine_10um()
    {
        var chunk = FindChunk("Wide Line", "10μm");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain 'Wide Line > 10μm'");
        AssertContext(chunk!, "결함별 상세 분석", "Cu Dishing");
    }

    [Fact] // Ch: 3 | Sec: 3.2 | Line: "패턴 밀도가 높을수록(> 50%) Erosion이 심하다"
    public void S3_2_OxideErosion_Density_50Percent()
    {
        var chunk = FindChunk("Erosion", "50%");
        chunk.Should().NotBeNull($"{FileName} > §3.2 should contain 'Erosion, 50%'");
        AssertContext(chunk!, "결함별 상세 분석", "Oxide Erosion");
    }

    [Fact] // Ch: 3 | Sec: 3.3 | Line: "린스/세정 지연(연마 후 > 30초 방치)...BTA 방식제"
    public void S3_3_CuCorrosion_30sec_BTA()
    {
        var chunk = FindChunk("30초", "BTA");
        chunk.Should().NotBeNull($"{FileName} > §3.3 should contain '30초 세정 지연, BTA'");
        AssertContext(chunk!, "결함별 상세 분석", "Cu Corrosion");
    }

    [Fact] // Ch: 3 | Sec: 3.4 | Line: "분석 도구: SEM/EDX, TOF-SIMS"
    public void S3_4_PostCMPResidue_TOFSIMS()
    {
        var chunk = FindChunk("TOF-SIMS", "SEM");
        chunk.Should().NotBeNull($"{FileName} > §3.4 should contain 'TOF-SIMS, SEM'");
        AssertContext(chunk!, "결함별 상세 분석", "Residue");
    }

    // ═══ §4. 결함 개선 사례 ═══

    [Fact] // Ch: 4 | Sec: 4.1 | Line: "2단 필터 적용(1.0μm + 0.5μm)...스크래치 0%"
    public void S4_1_ScratchZero_DualFilter()
    {
        var chunk = FindChunk("2단 필터", "0%");
        chunk.Should().NotBeNull($"{FileName} > §4.1 should contain '2단 필터, 스크래치 0%'");
        AssertContext(chunk!, "결함 개선 사례", "스크래치 Zero");
    }

    [Fact] // Ch: 4 | Sec: 4.2 | Line: "Dishing 800Å → 350Å...EPD 튜닝, Over-polish 15초"
    public void S4_2_DishingImprovement_800_to_350A()
    {
        var chunk = FindChunk("800Å", "350Å");
        chunk.Should().NotBeNull($"{FileName} > §4.2 should contain '800Å → 350Å'");
        AssertContext(chunk!, "결함 개선 사례", "Dishing 개선");
    }

    [Fact] // Ch: 4 | Sec: 4.3 | Line: "Zone 1 압력 3.5 → 3.0 psi...WIWNU 2.8%"
    public void S4_3_UniformityImprovement_Zone1_3_0psi_WIWNU_2_8()
    {
        var chunk = FindChunk("3.0 psi", "2.8%");
        chunk.Should().NotBeNull($"{FileName} > §4.3 should contain 'Zone 1 3.0 psi, WIWNU 2.8%'");
        AssertContext(chunk!, "결함 개선 사례", "균일도 개선");
    }

    // ═══ §5. 결함 보고 ═══

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "8D 보고서는 체계적 문제 해결을 위한 8단계 문서"
    public void S5_1_8DReport_8Steps()
    {
        var chunk = FindChunk("8D", "8단계");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain '8D, 8단계'");
        AssertContext(chunk!, "결함 보고", "8D 보고서");
    }

    [Fact] // Ch: 5 | Sec: 5.2 | Line: "Fishbone(Ishikawa)...6대 원인 범주: Man, Machine, Material, Method"
    public void S5_2_Fishbone_6Categories()
    {
        var chunk = FindChunk("Fishbone", "Man");
        chunk.Should().NotBeNull($"{FileName} > §5.2 should contain 'Fishbone, 6M'");
        AssertContext(chunk!, "결함 보고", "Fishbone");
    }

    [Fact] // Ch: 5 | Sec: 5.3 | Line: "CAPA 기록은 최소 3년 보관"
    public void S5_3_CAPA_3Years()
    {
        var chunk = FindChunk("CAPA", "3년");
        chunk.Should().NotBeNull($"{FileName} > §5.3 should contain 'CAPA, 3년'");
        AssertContext(chunk!, "결함 보고", "CAPA");
    }

    // ═══ Prompt ═══

    [Fact]
    public void Prompt_Contains_DefectPatternInfoAndFileName()
    {
        var prompt = BuildPromptWith("방사형 패턴: 컨디셔너 디스크 다이아몬드 탈락 → 패드 스크래치");
        prompt.Should().Contain("방사형");
        prompt.Should().Contain(FileName);
    }
}
