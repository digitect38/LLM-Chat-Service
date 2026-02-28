using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies cmp-equipment-overview.md with strict document → chapter → section → line mapping.
/// </summary>
public class CmpEquipmentOverviewContentTests
{
    private const string FileName = "cmp-equipment-overview.md";

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

    // ═══ §1. 장비 전체 구성 ═══

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "전체 풋프린트는 약 3m × 5m...Platen 3개"
    public void S1_1_Layout_Footprint_3x5m_Platen3()
    {
        var chunk = FindChunk("3m × 5m", "Platen 3개");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain '3m × 5m, Platen 3개'");
        AssertContext(chunk!, "장비 전체 구성", "장비 레이아웃");
    }

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "FOUP Load Port 2~3개"
    public void S1_1_LoadPort_2_3()
    {
        var chunk = FindChunk("FOUP Load Port", "2~3개");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'FOUP Load Port 2~3개'");
    }

    // ═══ §2. Polishing Module ═══

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "직경: 약 600~800mm...TIR < 25 μm...냉각수 온도 18~22°C"
    public void S2_1_Platen_600_800mm_TIR_25um()
    {
        var chunk = FindChunk("600~800mm", "25 μm");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '600~800mm, TIR 25 μm'");
        AssertContext(chunk!, "Polishing Module", "Platen");
    }

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "속도 범위 20~150 rpm...냉각수 온도 18~22°C"
    public void S2_1_Platen_Speed_20_150rpm_Coolant_18_22C()
    {
        var chunk = FindChunk("20~150 rpm", "18~22°C");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '20~150 rpm, 18~22°C'");
    }

    [Fact] // Ch: 2 | Sec: 2.2 | Line: "5~7개 독립 Zone...진공 방식(-600 mmHg)"
    public void S2_2_CarrierHead_5_7Zone_Vacuum_600mmHg()
    {
        var chunk = FindChunk("5~7개", "-600 mmHg");
        chunk.Should().NotBeNull($"{FileName} > §2.2 should contain '5~7 Zone, -600 mmHg'");
        AssertContext(chunk!, "Polishing Module", "Carrier Head");
    }

    [Fact] // Ch: 2 | Sec: 2.3 | Line: "IC1000/SubaIV(경질/연질 복합)...Groove 깊이: 0.3~0.5mm"
    public void S2_3_Pad_IC1000_Groove_0_3_0_5mm()
    {
        var chunk = FindChunk("IC1000", "0.3~0.5mm");
        chunk.Should().NotBeNull($"{FileName} > §2.3 should contain 'IC1000, Groove 0.3~0.5mm'");
        AssertContext(chunk!, "Polishing Module", "Polishing Pad");
    }

    [Fact] // Ch: 2 | Sec: 2.4 | Line: "다이아몬드 입자가 전착된 원형 디스크(직경 약 100mm)...3~7 lbf"
    public void S2_4_Conditioner_100mm_3_7lbf()
    {
        var chunk = FindChunk("100mm", "3~7 lbf");
        chunk.Should().NotBeNull($"{FileName} > §2.4 should contain '100mm disk, 3~7 lbf'");
        AssertContext(chunk!, "Polishing Module", "Conditioner");
    }

    // ═══ §3. Slurry Delivery System ═══

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "용량: 20~50 리터"
    public void S3_1_SlurryTank_20_50L()
    {
        var chunk = FindChunk("20~50 리터", "교반기");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain '20~50 리터, 교반기'");
        AssertContext(chunk!, "Slurry Delivery", "슬러리 탱크");
    }

    [Fact] // Ch: 3 | Sec: 3.2 | Line: "출력 압력: 15~25 psi...공극 크기 0.5~1.0 μm...차압 < 10 psi"
    public void S3_2_Pump_15_25psi_Filter_0_5_1_0um_10psi()
    {
        var chunk = FindChunk("15~25 psi", "0.5~1.0 μm");
        chunk.Should().NotBeNull($"{FileName} > §3.2 should contain '15~25 psi, 0.5~1.0 μm filter'");
        AssertContext(chunk!, "Slurry Delivery", "공급 펌프");
        chunk!.Should().Contain("10 psi", "filter DP threshold should be preserved");
    }

    // ═══ §4. Wafer Handling System ═══

    [Fact] // Ch: 4 | Sec: 4.1 | Line: "위치 정확도: ± 0.5mm. 반복 정밀도: ± 0.2mm"
    public void S4_1_Robot_Accuracy_0_5mm_Repeatability_0_2mm()
    {
        var chunk = FindChunk("± 0.5mm", "± 0.2mm");
        chunk.Should().NotBeNull($"{FileName} > §4.1 should contain '± 0.5mm, ± 0.2mm'");
        AssertContext(chunk!, "Wafer Handling", "로봇 암");
    }

    [Fact] // Ch: 4 | Sec: 4.2 | Line: "FOUP...300mm 웨이퍼 25매 수납"
    public void S4_2_FOUP_25Wafer()
    {
        var chunk = FindChunk("25매", "FOUP");
        chunk.Should().NotBeNull($"{FileName} > §4.2 should contain 'FOUP 25매'");
        AssertContext(chunk!, "Wafer Handling", "FOUP Load Port");
    }

    [Fact] // Ch: 4 | Sec: 4.3 | Line: "정렬 정확도: ± 0.1°"
    public void S4_3_Aligner_0_1degree()
    {
        var chunk = FindChunk("± 0.1°", "정렬");
        chunk.Should().NotBeNull($"{FileName} > §4.3 should contain '± 0.1° 정렬'");
        AssertContext(chunk!, "Wafer Handling", "웨이퍼 정렬기");
    }

    // ═══ §5. Cleaning Module ═══

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "PVA(Polyvinyl Alcohol) 브러시...100~300 rpm...1~3 psi"
    public void S5_1_Brush_PVA_100_300rpm()
    {
        var chunk = FindChunk("PVA", "100~300 rpm");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain 'PVA, 100~300 rpm'");
        AssertContext(chunk!, "Cleaning Module", "Brush Station");
    }

    [Fact] // Ch: 5 | Sec: 5.2 | Line: "고주파 초음파(700kHz~1MHz)...파워: 20~50W"
    public void S5_2_Megasonic_700kHz_1MHz_20_50W()
    {
        var chunk = FindChunk("700kHz~1MHz", "20~50W");
        chunk.Should().NotBeNull($"{FileName} > §5.2 should contain '700kHz~1MHz, 20~50W'");
        AssertContext(chunk!, "Cleaning Module", "Megasonic");
    }

    [Fact] // Ch: 5 | Sec: 5.3 | Line: "2000~4000 rpm...질소(N2) 블로우"
    public void S5_3_SpinDryer_2000_4000rpm_N2()
    {
        var chunk = FindChunk("2000~4000 rpm", "N2");
        chunk.Should().NotBeNull($"{FileName} > §5.3 should contain '2000~4000 rpm, N2'");
        AssertContext(chunk!, "Cleaning Module", "Spin Dryer");
    }

    // ═══ §6. 제어 시스템 ═══

    [Fact] // Ch: 6 | Sec: 6.1 | Line: "PLC...Ethernet/IP 또는 EtherCAT"
    public void S6_1_PLC_EtherCAT()
    {
        var chunk = FindChunk("PLC", "EtherCAT");
        chunk.Should().NotBeNull($"{FileName} > §6.1 should contain 'PLC, EtherCAT'");
        AssertContext(chunk!, "제어 시스템", "PLC/Software");
    }

    [Fact] // Ch: 6 | Sec: 6.3 | Line: "SECS/GEM(SEMI E30/E37)...EDA(SEMI E134)"
    public void S6_3_SECSGEM_EDA()
    {
        var chunk = FindChunk("SECS/GEM", "EDA");
        chunk.Should().NotBeNull($"{FileName} > §6.3 should contain 'SECS/GEM, EDA'");
        AssertContext(chunk!, "제어 시스템", "통신 인터페이스");
    }

    // ═══ Prompt ═══

    [Fact]
    public void Prompt_Contains_EquipmentOverviewAndFileName()
    {
        var prompt = BuildPromptWith("CMP 장비: Polishing Module, Cleaning Module, Wafer Handling System");
        prompt.Should().Contain("Polishing Module");
        prompt.Should().NotContain(FileName);
    }
}
