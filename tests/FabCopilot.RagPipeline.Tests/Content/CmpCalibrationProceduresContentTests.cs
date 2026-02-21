using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Content;

/// <summary>
/// Verifies cmp-calibration-procedures.md with strict document → chapter → section → line mapping.
/// </summary>
public class CmpCalibrationProceduresContentTests
{
    private const string FileName = "cmp-calibration-procedures.md";

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

    // ═══ §1. 캘리브레이션 개요 ═══

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "압력 | Monthly | ± 0.1 psi"
    public void S1_1_PressureSchedule_Monthly_0_1psi()
    {
        var chunk = FindChunk("Monthly", "± 0.1 psi");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Monthly, ± 0.1 psi'");
        AssertContext(chunk!, "캘리브레이션 개요", "목적 및 주기");
    }

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "유량 | Monthly | ± 5%"
    public void S1_1_FlowSchedule_Monthly_5Percent()
    {
        var chunk = FindChunk("Monthly", "± 5%");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Monthly, ± 5%'");
        AssertContext(chunk!, "캘리브레이션 개요");
    }

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "온도 | Quarterly | ± 1°C"
    public void S1_1_TempSchedule_Quarterly_1C()
    {
        var chunk = FindChunk("Quarterly", "± 1°C");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Quarterly, ± 1°C'");
    }

    [Fact] // Ch: 1 | Sec: 1.1 | Line: "속도(RPM) | Quarterly | ± 1 rpm"
    public void S1_1_SpeedSchedule_Quarterly_1rpm()
    {
        var chunk = FindChunk("Quarterly", "± 1 rpm");
        chunk.Should().NotBeNull($"{FileName} > §1.1 should contain 'Quarterly, ± 1 rpm'");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "NIST 추적 가능 압력 게이지(정확도 ± 0.05 psi)"
    public void S1_2_ReferenceStandard_NIST_0_05psi()
    {
        var chunk = FindChunk("NIST", "± 0.05 psi");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain 'NIST, ± 0.05 psi'");
        AssertContext(chunk!, "캘리브레이션 개요", "필요 장비");
    }

    [Fact] // Ch: 1 | Sec: 1.2 | Line: "교정된 RTD 센서(정확도 ± 0.1°C)"
    public void S1_2_RTD_0_1C()
    {
        var chunk = FindChunk("RTD", "± 0.1°C");
        chunk.Should().NotBeNull($"{FileName} > §1.2 should contain 'RTD, ± 0.1°C'");
    }

    // ═══ §2. 압력 캘리브레이션 ═══

    [Fact] // Ch: 2 | Sec: 2.1 | Line: "1.0, 2.0, 3.0, 4.0, 5.0 psi로 단계별 설정"
    public void S2_1_HeadZonePressure_5Steps()
    {
        var chunk = FindChunk("1.0, 2.0, 3.0, 4.0, 5.0 psi");
        chunk.Should().NotBeNull($"{FileName} > §2.1 should contain '1.0~5.0 psi steps'");
        AssertContext(chunk!, "압력 캘리브레이션", "Head Zone");
    }

    [Fact] // Ch: 2 | Sec: 2.2 | Line: "4.0~8.0 psi...Ring 압력 편차는 에지 균일도에 직접 영향"
    public void S2_2_RetainingRingPressure_4_8psi()
    {
        var chunk = FindChunk("4.0~8.0 psi", "에지 균일도");
        chunk.Should().NotBeNull($"{FileName} > §2.2 should contain '4.0~8.0 psi, 에지 균일도'");
        AssertContext(chunk!, "압력 캘리브레이션", "Retaining Ring");
    }

    [Fact] // Ch: 2 | Sec: 2.3 | Line: "설정 범위: -5 ~ +10 psi(진공~양압)"
    public void S2_3_BackPressure_Neg5_Pos10psi()
    {
        var chunk = FindChunk("-5 ~ +10 psi", "진공");
        chunk.Should().NotBeNull($"{FileName} > §2.3 should contain '-5 ~ +10 psi'");
        AssertContext(chunk!, "압력 캘리브레이션", "Back Pressure");
    }

    [Fact] // Ch: 2 | Sec: 2.4 | Line: "30초간 압력 변화를 0.1 psi 단위로 기록...합격: < 0.3 psi"
    public void S2_4_PressureHoldTest_30sec_0_3psi()
    {
        var chunk = FindChunk("30초", "0.3 psi");
        chunk.Should().NotBeNull($"{FileName} > §2.4 should contain '30초, 0.3 psi'");
        AssertContext(chunk!, "압력 캘리브레이션", "Pressure Hold Test");
    }

    // ═══ §3. 유량 캘리브레이션 ═══

    [Fact] // Ch: 3 | Sec: 3.1 | Line: "슬러리 유량 캘리브레이션 절차(중량법)...60초간 배출량 수집"
    public void S3_1_SlurryFlow_Gravimetric_60sec()
    {
        var chunk = FindChunk("중량법", "60초");
        chunk.Should().NotBeNull($"{FileName} > §3.1 should contain '중량법, 60초'");
        AssertContext(chunk!, "유량 캘리브레이션", "슬러리 유량계");
    }

    [Fact] // Ch: 3 | Sec: 3.2 | Line: "200, 500, 1000 ml/min의 3개 포인트에서 교정"
    public void S3_2_DIWaterFlow_3Points()
    {
        var chunk = FindChunk("200, 500, 1000 ml/min");
        chunk.Should().NotBeNull($"{FileName} > §3.2 should contain '200, 500, 1000 ml/min'");
        AssertContext(chunk!, "유량 캘리브레이션", "DI Water");
    }

    // ═══ §4. 온도 캘리브레이션 ═══

    [Fact] // Ch: 4 | Sec: 4.1 | Line: "온도 안정화 대기(10분)"
    public void S4_1_PlatenTemp_10minStabilize()
    {
        var chunk = FindChunk("안정화 대기", "10분");
        chunk.Should().NotBeNull($"{FileName} > §4.1 should contain '안정화 대기, 10분'");
        AssertContext(chunk!, "온도 캘리브레이션", "플래튼 온도");
    }

    [Fact] // Ch: 4 | Sec: 4.2 | Line: "±2°C → MRR ±5% 변동"
    public void S4_2_SlurryTemp_MRR_Effect_2C_5Percent()
    {
        var chunk = FindChunk("±2°C", "±5%");
        chunk.Should().NotBeNull($"{FileName} > §4.2 should contain '±2°C → MRR ±5%'");
        AssertContext(chunk!, "온도 캘리브레이션", "슬러리 온도");
    }

    // ═══ §5. 속도 캘리브레이션 ═══

    [Fact] // Ch: 5 | Sec: 5.1 | Line: "60, 80, 100, 120 rpm으로 단계별 변경"
    public void S5_1_PlatenRPM_4Steps()
    {
        var chunk = FindChunk("60, 80, 100, 120 rpm");
        chunk.Should().NotBeNull($"{FileName} > §5.1 should contain '60, 80, 100, 120 rpm'");
        AssertContext(chunk!, "속도 캘리브레이션", "플래튼 RPM");
    }

    [Fact] // Ch: 5 | Sec: 5.2 | Line: "50, 75, 100 rpm의 3개 포인트"
    public void S5_2_CarrierRPM_3Points()
    {
        var chunk = FindChunk("50, 75, 100 rpm");
        chunk.Should().NotBeNull($"{FileName} > §5.2 should contain '50, 75, 100 rpm'");
        AssertContext(chunk!, "속도 캘리브레이션", "캐리어 RPM");
    }

    [Fact] // Ch: 5 | Sec: 5.3 | Line: "범위 편차 ± 5mm"
    public void S5_3_ConditionerSweep_5mm()
    {
        var chunk = FindChunk("Sweep", "± 5mm");
        chunk.Should().NotBeNull($"{FileName} > §5.3 should contain 'Sweep, ± 5mm'");
        AssertContext(chunk!, "속도 캘리브레이션", "컨디셔너 Arm");
    }

    // ═══ §6. 센서 캘리브레이션 ═══

    [Fact] // Ch: 6 | Sec: 6.1 | Line: "Reference wafer(알려진 막 두께)"
    public void S6_1_EPDSensor_ReferenceWafer()
    {
        var chunk = FindChunk("Reference wafer", "EPD");
        chunk.Should().NotBeNull($"{FileName} > §6.1 should contain 'Reference wafer, EPD'");
        AssertContext(chunk!, "센서 캘리브레이션", "EPD 센서");
    }

    [Fact] // Ch: 6 | Sec: 6.2 | Line: "무부하 상태에서 토크 출력 확인(기준: 0 ± 0.1 N·m)"
    public void S6_2_TorqueSensor_0_1Nm()
    {
        var chunk = FindChunk("토크 센서", "0.1 N·m");
        chunk.Should().NotBeNull($"{FileName} > §6.2 should contain '토크 센서, 0.1 N·m'");
        AssertContext(chunk!, "센서 캘리브레이션", "토크 센서");
    }

    [Fact] // Ch: 6 | Sec: 6.3 | Line: "-200, -400, -600 mmHg의 3개 포인트...± 20 mmHg"
    public void S6_3_VacuumSensor_3Points_20mmHg()
    {
        var chunk = FindChunk("-200, -400, -600 mmHg", "± 20 mmHg");
        chunk.Should().NotBeNull($"{FileName} > §6.3 should contain 'vacuum 3 points, ± 20 mmHg'");
        AssertContext(chunk!, "센서 캘리브레이션", "Vacuum 센서");
    }

    // ═══ §7. 로봇 캘리브레이션 ═══

    [Fact] // Ch: 7 | Sec: 7.1 | Line: "수동(Teach) 모드로 전환...좌표(X, Y, Z, θ)"
    public void S7_1_TeachProcedure_XYZT()
    {
        var chunk = FindChunk("Teach", "X, Y, Z");
        chunk.Should().NotBeNull($"{FileName} > §7.1 should contain 'Teach, X, Y, Z, θ'");
        AssertContext(chunk!, "로봇 캘리브레이션", "티칭 절차");
    }

    [Fact] // Ch: 7 | Sec: 7.2 | Line: "반복 정밀도: 5회 측정의 표준편차 < 0.2mm...정확도 < ± 0.5mm"
    public void S7_2_PositionAccuracy_0_5mm_Repeatability_0_2mm()
    {
        var chunk = FindChunk("± 0.5mm", "0.2mm");
        chunk.Should().NotBeNull($"{FileName} > §7.2 should contain '± 0.5mm, 0.2mm'");
        AssertContext(chunk!, "로봇 캘리브레이션", "위치 정확도");
    }

    // ═══ §8. 캘리브레이션 기록 ═══

    [Fact] // Ch: 8 | Sec: 8.1 | Line: "Zone 압력 | ± 0.1 psi | 레귤레이터 조정/센서 교체"
    public void S8_1_PassFailCriteria_ZonePressure()
    {
        var chunk = FindChunk("PASS", "레귤레이터 조정");
        chunk.Should().NotBeNull($"{FileName} > §8.1 should contain 'PASS/FAIL, 레귤레이터 조정'");
        AssertContext(chunk!, "캘리브레이션 기록", "PASS/FAIL");
    }

    [Fact] // Ch: 8 | Sec: 8.2 | Line: "보관 기간: 최소 3년"
    public void S8_2_RecordRetention_3Years()
    {
        var chunk = FindChunk("3년", "교정 이력");
        chunk.Should().NotBeNull($"{FileName} > §8.2 should contain '3년, 교정 이력'");
        AssertContext(chunk!, "캘리브레이션 기록");
    }

    // ═══ Prompt ═══

    [Fact]
    public void Prompt_Contains_CalibrationInfoAndFileName()
    {
        var prompt = BuildPromptWith("Head Zone 압력 캘리브레이션: 1.0~5.0 psi, 허용 편차 ± 0.1 psi");
        prompt.Should().Contain("± 0.1 psi");
        prompt.Should().Contain(FileName);
    }
}
