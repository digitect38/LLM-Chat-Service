using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies cmp-consumable-qualification.md with strict document → chapter → section → line mapping.
/// </summary>
public class CmpConsumableQualificationContentTests
{
    private const string FileName = "cmp-consumable-qualification.md";

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

    // ═══ §1. Qualification 개요 ═══

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "IQ(Incoming Quality)...PQ(Performance Qualification)"
    public void S1_1_IQ_PQ_Types()
    {
        var chunk = FindChunk("IQ", "PQ");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'IQ, PQ'");
        AssertContext(chunk!, "Qualification 개요", "목적 및 유형");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "MRR | 목표값 ± 10% | 파라미터 조정 또는 반품"
    public void S1_2_MRR_10Percent_PassCriteria()
    {
        var chunk = FindChunk("± 10%", "반품");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain '± 10%, 반품'");
        AssertContext(chunk!, "Qualification 개요", "합격/불합격");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "WIWNU | < 5%"
    public void S1_2_WIWNU_5Percent()
    {
        var chunk = FindChunk("< 5%", "WIWNU");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain 'WIWNU < 5%'");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "결함(Scratch) | 0개"
    public void S1_2_Scratch_Zero()
    {
        var chunk = FindChunk("Scratch", "0개");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain 'Scratch 0개'");
    }

    // ═══ §2. Polishing Pad Qualification ═══

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "두께: 마이크로미터로 5점 측정, 스펙 ± 0.05mm"
    public void S2_1_PadThickness_5Point_0_05mm()
    {
        var chunk = FindChunk("5점 측정", "± 0.05mm");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '5점 측정, ± 0.05mm'");
        AssertContext(chunk!, "Polishing Pad Qualification", "입고 검사");
    }

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "경도(Shore D): 경도계로 3점 측정, Oxide 패드 기준 50~60D"
    public void S2_1_PadHardness_ShoreD_50_60()
    {
        var chunk = FindChunk("Shore D", "50~60D");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain 'Shore D 50~60D'");
        AssertContext(chunk!, "Polishing Pad Qualification", "입고 검사");
    }

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "유효 기간: 제조일 기준 12개월 이내"
    public void S2_1_PadExpiry_12Months()
    {
        var chunk = FindChunk("12개월", "유효");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '12개월 유효기간'");
    }

    [Fact] // Ch: 2 | Sec: 2.2 | Line: "Downforce 7 lbf로 20분간 컨디셔닝...더미 웨이퍼 5매"
    public void S2_2_PadBreakin_7lbf_20min_5Dummy()
    {
        var chunk = FindChunk("7 lbf", "20분");
        chunk.Should().NotBeNull($"{FileName} > §2.2 should contain '7 lbf, 20분'");
        AssertContext(chunk!, "Polishing Pad Qualification", "Break-in");
        chunk!.Should().Contain("더미 웨이퍼 5매", "dummy wafer count should be preserved");
    }

    [Fact] // Ch: 2 | Sec: 2.3 | Line: "Test wafer 5매를 연속 연마...MRR이 목표값 ± 10%"
    public void S2_3_PadPQ_5Wafer_MRR_10Percent()
    {
        var chunk = FindChunk("Test wafer 5매", "± 10%");
        chunk.Should().NotBeNull($"{FileName} > §2.3 should contain 'Test wafer 5매, ± 10%'");
        AssertContext(chunk!, "Polishing Pad Qualification", "성능 검증");
    }

    [Fact] // Ch: 2 | Sec: 2.4 | Line: "MRR: 목표값 ± 10%(예: Oxide 3800 ± 380 Å/min)"
    public void S2_4_PadAcceptance_Oxide_3800()
    {
        var chunk = FindChunk("3800", "380");
        chunk.Should().NotBeNull($"{FileName} > §2.4 should contain 'Oxide 3800 ± 380'");
        AssertContext(chunk!, "Polishing Pad Qualification", "합격 기준");
    }

    // ═══ §3. 슬러리 Qualification ═══

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "pH: pH meter로 측정, 타입별 스펙 ± 0.3(예: Oxide pH 10.5 ± 0.3)"
    public void S3_1_SlurrypH_10_5_pm0_3()
    {
        var chunk = FindChunk("pH 10.5", "± 0.3");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain 'pH 10.5 ± 0.3'");
        AssertContext(chunk!, "슬러리 Qualification", "입고 검사");
    }

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "Particle Size: D50 측정, 스펙 ± 15%"
    public void S3_1_SlurryParticleSize_D50_15Percent()
    {
        var chunk = FindChunk("D50", "± 15%");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain 'D50 ± 15%'");
    }

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "유효 기간: 제조일 기준 6개월 이내"
    public void S3_1_SlurryExpiry_6Months()
    {
        var chunk = FindChunk("6개월", "유효");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain '6개월 유효기간'");
    }

    [Fact] // Ch: 3 | Sec: 3.4 | Line: "서로 다른 제조 로트의 슬러리를 동일 탱크에 혼합하지 않는다"
    public void S3_4_LotMixProhibition()
    {
        var chunk = FindChunk("혼합", "로트");
        chunk.Should().NotBeNull($"{FileName} > §3.4 should contain '로트 혼합 금지'");
        AssertContext(chunk!, "슬러리 Qualification", "혼합 금지");
    }

    // ═══ §4. Conditioner Disk Qualification ═══

    [Fact] // Ch: 4 | Sec: 4.1 | Line: "다이아몬드 입자 분포: 현미경으로 균일 분포 확인(탈락 부위 없음)"
    public void S4_1_ConditionerDiamond_Microscope()
    {
        var chunk = FindChunk("다이아몬드", "현미경");
        chunk.Should().NotBeNull($"{FileName} > §4.1 should contain '다이아몬드 현미경'");
        AssertContext(chunk!, "Conditioner Disk Qualification", "입고 검사");
    }

    [Fact] // Ch: 4 | Sec: 4.2 | Line: "Cut Rate가 레퍼런스 디스크 대비 ± 20% 이내"
    public void S4_2_CutRate_20Percent()
    {
        var chunk = FindChunk("Cut Rate", "± 20%");
        chunk.Should().NotBeNull($"{FileName} > §4.2 should contain 'Cut Rate ± 20%'");
        AssertContext(chunk!, "Conditioner Disk Qualification", "성능 검증");
    }

    // ═══ §5. Retaining Ring Qualification ═══

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "두께: 마이크로미터로 4점 측정, 균일도 확인(편차 < 0.1mm)"
    public void S5_1_RingThickness_4Point_0_1mm()
    {
        var chunk = FindChunk("4점 측정", "0.1mm");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain '4점 측정, 0.1mm'");
        AssertContext(chunk!, "Retaining Ring Qualification", "입고 검사");
    }

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "재질: PPS(Polyphenylene Sulfide)"
    public void S5_1_RingMaterial_PPS()
    {
        var chunk = FindChunk("PPS", "Polyphenylene Sulfide");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain 'PPS Polyphenylene Sulfide'");
    }

    [Fact] // Ch: 5 | Sec: 5.2 | Line: "에지 3mm 영역 두께 편차 < 100Å"
    public void S5_2_RingEdgeProfile_3mm_100A()
    {
        var chunk = FindChunk("에지 3mm", "100Å");
        chunk.Should().NotBeNull($"{FileName} > §5.2 should contain '에지 3mm, 100Å'");
        AssertContext(chunk!, "Retaining Ring Qualification", "성능 검증");
    }

    [Fact] // Ch: 5 | Sec: 5.3 | Line: "Pressure Hold Test: 전 Zone 합격(30초간 압력 강하 < 0.3 psi)"
    public void S5_3_RingAcceptance_PressureHoldTest_0_3psi()
    {
        var chunk = FindChunk("Pressure Hold Test", "0.3 psi");
        chunk.Should().NotBeNull($"{FileName} > §5.3 should contain 'Pressure Hold Test, 0.3 psi'");
        AssertContext(chunk!, "Retaining Ring Qualification", "합격 기준");
    }

    // ═══ §6. 기록 관리 ═══

    [Fact] // Ch: 6 | Sec: 6.1 | Line: "보관 기간 최소 2년"
    public void S6_1_RecordRetention_2Years()
    {
        var chunk = FindChunk("2년", "보관");
        chunk.Should().NotBeNull($"{FileName} > §6.1 should contain '2년 보관'");
        AssertContext(chunk!, "Qualification 기록 관리");
    }

    // ═══ Prompt ═══

    [Fact]
    public void Prompt_Contains_QualificationInfoAndFileName()
    {
        var prompt = BuildPromptWith("Pad Qualification: MRR ± 10%, WIWNU < 5%, Scratch 0개");
        prompt.Should().Contain("± 10%");
        prompt.Should().NotContain(FileName);
    }
}
