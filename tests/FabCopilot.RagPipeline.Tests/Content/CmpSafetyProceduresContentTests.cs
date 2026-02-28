using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies cmp-safety-procedures.md with strict document → chapter → section → line mapping.
/// </summary>
public class CmpSafetyProceduresContentTests
{
    private const string FileName = "cmp-safety-procedures.md";

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

    // ═══ §1. 일반 안전 수칙 ═══

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "클린룸 방진복(Bunny Suit), 안전화(정전기 방지), 보안경(슬러리 비산 방지), 내화학 장갑(니트릴)"
    public void S1_1_PPE_BunnySuit_Nitrile()
    {
        var chunk = FindChunk("Bunny Suit", "니트릴");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Bunny Suit, 니트릴'");
        AssertContext(chunk!, "일반 안전 수칙", "PPE");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "1인 작업 금지...2인 1조"
    public void S1_2_TwoPersonRule()
    {
        var chunk = FindChunk("2인 1조", "1인 작업 금지");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain '2인 1조, 1인 작업 금지'");
        AssertContext(chunk!, "일반 안전 수칙", "클린룸 안전");
    }

    [Fact] // Ch: 1 | Sec: 1.3 | Line: "MSDS 비치 여부, 비상 샤워/세안기"
    public void S1_3_PreWorkCheck_MSDS_EmergencyShower()
    {
        var chunk = FindChunk("MSDS", "비상 샤워");
        chunk.Should().NotBeNull($"{FileName} > §1.3 should contain 'MSDS, 비상 샤워'");
        AssertContext(chunk!, "일반 안전 수칙", "작업 전 안전");
    }

    // ═══ §2. 화학물질 안전 ═══

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "Oxide 슬러리는 pH 10~11(강알칼리), Cu 슬러리는 pH 3~4(산성)"
    public void S2_1_SlurrypH_Oxide_10_11_Cu_3_4()
    {
        var chunk = FindChunk("pH 10~11", "pH 3~4");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain 'pH 10~11, pH 3~4'");
        AssertContext(chunk!, "화학물질 안전", "슬러리 취급");
    }

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "15분간 흐르는 물로 세척"
    public void S2_1_SkinContact_15min_Water()
    {
        var chunk = FindChunk("15분", "세척");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '15분 세척'");
    }

    [Fact] // Ch: 2 | Sec: 2.2 | Line: "HF(불산)는 극독성...칼슘 글루코네이트 겔"
    public void S2_2_HF_CalciumGluconate()
    {
        var chunk = FindChunk("HF", "칼슘 글루코네이트");
        chunk.Should().NotBeNull($"{FileName} > §2.2 should contain 'HF, 칼슘 글루코네이트'");
        AssertContext(chunk!, "화학물질 안전", "세정 화학물질");
    }

    [Fact] // Ch: 2 | Sec: 2.3 | Line: "소량 유출(< 500 ml)...대량 유출(> 500 ml)"
    public void S2_3_SpillResponse_500ml()
    {
        var chunk = FindChunk("500 ml", "흡착재");
        chunk.Should().NotBeNull($"{FileName} > §2.3 should contain '500 ml, 흡착재'");
        AssertContext(chunk!, "화학물질 안전", "유출 대응");
    }

    [Fact] // Ch: 2 | Sec: 2.4 | Line: "80% 차면 환경안전팀에 수거 요청"
    public void S2_4_WasteDisposal_80Percent()
    {
        var chunk = FindChunk("80%", "수거");
        chunk.Should().NotBeNull($"{FileName} > §2.4 should contain '80% 수거'");
        AssertContext(chunk!, "화학물질 안전", "폐액 처리");
    }

    // ═══ §3. 전기 안전 ═══

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "LOTO(Lock-Out/Tag-Out)...잠금 장치(자물쇠) 설치"
    public void S3_1_LOTO_LockTag()
    {
        var chunk = FindChunk("Lock-Out/Tag-Out", "자물쇠");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain 'Lock-Out/Tag-Out, 자물쇠'");
        AssertContext(chunk!, "전기 안전", "LOTO");
    }

    [Fact] // Ch: 3 | Sec: 3.2 | Line: "절연 공구...콘덴서 방전 확인(대기 시간 최소 5분)"
    public void S3_2_InsulatedTools_5min()
    {
        var chunk = FindChunk("절연 공구", "5분");
        chunk.Should().NotBeNull($"{FileName} > §3.2 should contain '절연 공구, 5분 방전 대기'");
        AssertContext(chunk!, "전기 안전", "전기 배선 점검");
    }

    // ═══ §4. 기계 안전 ═══

    [Fact] // Ch: 4 | Sec: 4.1 | Line: "Platen(60~120 rpm), Carrier Head(50~100 rpm)"
    public void S4_1_RotatingParts_Platen_60_120_Carrier_50_100()
    {
        var chunk = FindChunk("60~120 rpm", "50~100 rpm");
        chunk.Should().NotBeNull($"{FileName} > §4.1 should contain 'Platen 60~120, Carrier 50~100'");
        AssertContext(chunk!, "기계 안전", "회전 부품");
    }

    [Fact] // Ch: 4 | Sec: 4.3 | Line: "캐리어 헤드(약 15~25 kg)...20 kg 이상 부품은 2인 이상 취급"
    public void S4_3_HeavyObject_15_25kg_20kgRule()
    {
        var chunk = FindChunk("15~25 kg", "20 kg");
        chunk.Should().NotBeNull($"{FileName} > §4.3 should contain '15~25 kg, 20 kg rule'");
        AssertContext(chunk!, "기계 안전", "중량물");
    }

    // ═══ §5. 비상 대응 ═══

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "E-Stop 버튼은 장비 전면과 후면에 각 1개 이상"
    public void S5_1_EStop_FrontAndRear()
    {
        var chunk = FindChunk("E-Stop", "전면");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain 'E-Stop, 전면/후면'");
        AssertContext(chunk!, "비상 대응", "Emergency Stop");
    }

    [Fact] // Ch: 5 | Sec: 5.2 | Line: "CO2 소화기로 초기 진화(전기 화재에 물 사용 금지)"
    public void S5_2_Fire_CO2_NoWater()
    {
        var chunk = FindChunk("CO2 소화기", "물 사용 금지");
        chunk.Should().NotBeNull($"{FileName} > §5.2 should contain 'CO2 소화기, 물 사용 금지'");
        AssertContext(chunk!, "비상 대응", "화재");
    }

    [Fact] // Ch: 5 | Sec: 5.3 | Line: "지진...E-Stop 즉시 실행...슬러리 라인 누출 확인"
    public void S5_3_Earthquake_EStop_SlurryCheck()
    {
        var chunk = FindChunk("지진", "슬러리 라인");
        chunk.Should().NotBeNull($"{FileName} > §5.3 should contain '지진, 슬러리 라인'");
        AssertContext(chunk!, "비상 대응", "지진");
    }

    [Fact] // Ch: 5 | Sec: 5.4 | Line: "정전...DI water 플러시"
    public void S5_4_PowerOutage_DIWaterFlush()
    {
        var chunk = FindChunk("정전", "DI water 플러시");
        chunk.Should().NotBeNull($"{FileName} > §5.4 should contain '정전, DI water 플러시'");
        AssertContext(chunk!, "비상 대응", "정전");
    }

    [Fact] // Ch: 5 | Sec: 5.5 | Line: "24시간 이내 사고 보고서 작성...Near Miss도 반드시 보고"
    public void S5_5_IncidentReport_24Hours_NearMiss()
    {
        var chunk = FindChunk("24시간", "Near Miss");
        chunk.Should().NotBeNull($"{FileName} > §5.5 should contain '24시간, Near Miss'");
        AssertContext(chunk!, "비상 대응", "사고 보고");
    }

    // ═══ §6. 안전 교육 ═══

    [Fact] // Ch: 6 | Sec: 6.1 | Line: "LOTO 교육(연 1회), 비상 대응 훈련(연 2회)"
    public void S6_1_TrainingList_LOTO_Annual_Emergency_Biannual()
    {
        var chunk = FindChunk("LOTO 교육", "연 2회");
        chunk.Should().NotBeNull($"{FileName} > §6.1 should contain 'LOTO 교육, 연 2회'");
        AssertContext(chunk!, "안전 교육", "필수 교육");
    }

    [Fact] // Ch: 6 | Sec: 6.2 | Line: "정기 교육 분기 6시간 이상...교육 이수 기록은 3년간 보관"
    public void S6_2_TrainingHours_6hr_Retention_3Years()
    {
        var chunk = FindChunk("6시간", "3년");
        chunk.Should().NotBeNull($"{FileName} > §6.2 should contain '분기 6시간, 3년 보관'");
        AssertContext(chunk!, "안전 교육", "교육 갱신 주기");
    }

    // ═══ Prompt ═══

    [Fact]
    public void Prompt_Contains_SafetyInfoAndFileName()
    {
        var prompt = BuildPromptWith("PPE: 클린룸 방진복, 보안경, 내화학 장갑(니트릴)");
        prompt.Should().Contain("PPE");
        prompt.Should().NotContain(FileName);
    }

    [Fact]
    public void Prompt_Contains_LOTODetail()
    {
        var prompt = BuildPromptWith("LOTO: 장비 정지 → 에너지 차단 → 잔류 에너지 확인 → 잠금");
        prompt.Should().Contain("LOTO");
    }
}
