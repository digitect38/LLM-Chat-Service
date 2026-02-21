using FabCopilot.RagService.Services;
using FabCopilot.RagService.Services.Bm25;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Golden query retrieval tests: verify that specific Korean queries retrieve
/// the correct document chunks. These tests caught a real production bug where
/// "패드 교체 판단 기준이 뭐야?" returned cmp-parameter-optimization.md instead
/// of cmp-slurry-pad-replacement.md.
///
/// Tests use BM25 keyword search (no external services required) to validate
/// that the chunking + indexing pipeline produces retrievable results.
/// </summary>
public class GoldenQueryRetrievalTests : IDisposable
{
    private static readonly string DocsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs");

    private readonly Bm25Index _bm25 = new(k1: 1.2, b: 0.75);
    private readonly Dictionary<string, string> _chunkToDoc = new();

    public GoldenQueryRetrievalTests()
    {
        // Build BM25 index from all knowledge docs using the real ChunkMarkdown pipeline
        foreach (var file in Directory.GetFiles(DocsPath, "*.md"))
        {
            var text = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);
            var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunkId = $"{fileName}:chunk:{i}";
                _bm25.AddDocument(chunkId, chunks[i]);
                _chunkToDoc[chunkId] = fileName;
            }
        }
    }

    public void Dispose() => _bm25.Clear();

    /// <summary>
    /// Asserts that at least one of the top-K results belongs to the expected document.
    /// </summary>
    private void AssertTopKContainsDocument(string query, string expectedDoc, int topK = 5)
    {
        var results = _bm25.Search(query, topK);
        var docs = results.Select(r => _chunkToDoc.GetValueOrDefault(r.DocumentId, "")).ToList();

        docs.Should().Contain(expectedDoc,
            because: $"query '{query}' should retrieve chunks from '{expectedDoc}' " +
                     $"but got: [{string.Join(", ", results.Select(r => $"{_chunkToDoc.GetValueOrDefault(r.DocumentId)}({r.Score:F3})"))}]");
    }

    /// <summary>
    /// Asserts that the top-1 result belongs to the expected document.
    /// </summary>
    private void AssertTop1IsDocument(string query, string expectedDoc)
    {
        var results = _bm25.Search(query, 1);
        results.Should().NotBeEmpty(because: $"query '{query}' should return results");

        var topDoc = _chunkToDoc.GetValueOrDefault(results[0].DocumentId, "");
        topDoc.Should().Be(expectedDoc,
            because: $"query '{query}' top-1 should be '{expectedDoc}'");
    }

    /// <summary>
    /// Asserts that at least one top-K result contains the expected section keyword.
    /// </summary>
    private void AssertTopKContainsSection(string query, string expectedSectionKeyword, int topK = 5)
    {
        var results = _bm25.Search(query, topK);
        var hasSection = results.Any(r => r.DocumentId.Contains(expectedSectionKeyword) ||
            GetChunkText(r.DocumentId).Contains(expectedSectionKeyword, StringComparison.OrdinalIgnoreCase));

        hasSection.Should().BeTrue(
            because: $"query '{query}' should return a chunk containing section '{expectedSectionKeyword}'");
    }

    private string GetChunkText(string chunkId)
    {
        var parts = chunkId.Split(":chunk:");
        if (parts.Length != 2 || !int.TryParse(parts[1], out var idx))
            return "";

        var filePath = Path.Combine(DocsPath, parts[0]);
        if (!File.Exists(filePath)) return "";

        var chunks = DocumentIngestor.ChunkMarkdown(File.ReadAllText(filePath), 512, 128);
        return idx < chunks.Count ? chunks[idx] : "";
    }

    // ========================================================================
    // 1. 패드 교체 (cmp-slurry-pad-replacement.md)
    // ========================================================================

    [Fact]
    public void Query_PadReplacementCriteria_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("패드 교체 판단 기준", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_PadReplacementProcedure_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("패드 교체 절차", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_PadBreakIn_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("패드 break-in 절차", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_SlurryReplacement_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("슬러리 교체 절차", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_SlurryFlush_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("슬러리 라인 플러시", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_RetainingRing_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("Retaining Ring 교체", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_ConditionerDisk_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("컨디셔너 디스크 교체", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_MembranePressureHold_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("Membrane Pressure Hold Test", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_SlurryTypeChange_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("슬러리 타입 변경 Cross-Contamination", "cmp-slurry-pad-replacement.md");

    [Fact]
    public void Query_PadLifetime500Hours_ReturnsSlurryPadDoc()
        => AssertTopKContainsDocument("패드 사용 시간 500시간", "cmp-slurry-pad-replacement.md");

    // ========================================================================
    // 2. 알람 코드 (cmp-alarm-code-reference.md)
    // ========================================================================

    [Fact]
    public void Query_AlarmA100_ReturnsAlarmDoc()
        => AssertTopKContainsDocument("알람 A100 비상 정지", "cmp-alarm-code-reference.md");

    [Fact]
    public void Query_EmergencyStop_ReturnsAlarmDoc()
        => AssertTopKContainsDocument("Emergency Stop 비상 정지", "cmp-alarm-code-reference.md");

    [Fact]
    public void Query_InterlockViolation_ReturnsAlarmDoc()
        => AssertTopKContainsDocument("인터록 위반 Interlock", "cmp-alarm-code-reference.md");

    // ========================================================================
    // 3. 유지보수 가이드 (cmp-maintenance-guide.md)
    // ========================================================================

    [Fact]
    public void Query_DailyPM_ReturnsMaintenanceDoc()
        => AssertTopKContainsDocument("Daily PM 점검 항목", "cmp-maintenance-guide.md");

    [Fact]
    public void Query_WeeklyPM_ReturnsMaintenanceDoc()
        => AssertTopKContainsDocument("Weekly PM 유지보수", "cmp-maintenance-guide.md");

    [Fact]
    public void Query_MonthlyPM_ReturnsMaintenanceDoc()
        => AssertTopKContainsDocument("Monthly PM 점검", "cmp-maintenance-guide.md");

    // ========================================================================
    // 4. 파라미터 최적화 (cmp-parameter-optimization.md)
    // ========================================================================

    [Fact]
    public void Query_DownforcePressure_ReturnsParameterDoc()
        => AssertTopKContainsDocument("Downforce 압력 최적화", "cmp-parameter-optimization.md");

    [Fact]
    public void Query_PlatenRPM_ReturnsParameterDoc()
        => AssertTopKContainsDocument("플래튼 RPM 회전 속도 최적화", "cmp-parameter-optimization.md");

    [Fact]
    public void Query_SlurryFlowRate_ReturnsParameterDoc()
        => AssertTopKContainsDocument("슬러리 유량 flow rate", "cmp-parameter-optimization.md");

    // ========================================================================
    // 5. 결함 분석 (cmp-defect-analysis.md)
    // ========================================================================

    [Fact]
    public void Query_ScratchDefect_ReturnsDefectDoc()
        => AssertTopKContainsDocument("스크래치 결함 분석", "cmp-defect-analysis.md");

    [Fact]
    public void Query_Dishing_ReturnsDefectDoc()
        => AssertTopKContainsDocument("Dishing 결함 원인", "cmp-defect-analysis.md");

    [Fact]
    public void Query_Erosion_ReturnsDefectDoc()
        => AssertTopKContainsDocument("Erosion 침식 결함", "cmp-defect-analysis.md");

    // ========================================================================
    // 6. 트러블슈팅 (cmp-general-troubleshooting.md)
    // ========================================================================

    [Fact]
    public void Query_MRRDrop_ReturnsTroubleshootingDoc()
        => AssertTopKContainsDocument("MRR 저하 트러블슈팅", "cmp-general-troubleshooting.md");

    [Fact]
    public void Query_WIWNUDegradation_ReturnsTroubleshootingDoc()
        => AssertTopKContainsDocument("WIWNU 균일도 악화", "cmp-general-troubleshooting.md");

    // ========================================================================
    // 7. 교정 절차 (cmp-calibration-procedures.md)
    // ========================================================================

    [Fact]
    public void Query_PressureCalibration_ReturnsCalibrationDoc()
        => AssertTopKContainsDocument("압력 센서 교정 calibration", "cmp-calibration-procedures.md");

    [Fact]
    public void Query_TemperatureCalibration_ReturnsCalibrationDoc()
        => AssertTopKContainsDocument("온도 센서 교정", "cmp-calibration-procedures.md");

    // ========================================================================
    // 8. 안전 절차 (cmp-safety-procedures.md)
    // ========================================================================

    [Fact]
    public void Query_PPE_ReturnsSafetyDoc()
        => AssertTopKContainsDocument("PPE 착용 보호 장비", "cmp-safety-procedures.md");

    [Fact]
    public void Query_ChemicalSpill_ReturnsSafetyDoc()
        => AssertTopKContainsDocument("화학물질 누출 비상 대응", "cmp-safety-procedures.md");

    // ========================================================================
    // 9. 소모품 Qualification (cmp-consumable-qualification.md)
    // ========================================================================

    [Fact]
    public void Query_ConsumableQualification_ReturnsConsumableDoc()
        => AssertTopKContainsDocument("소모품 Qualification 검증", "cmp-consumable-qualification.md");

    // ========================================================================
    // 10. 검사 측정 (cmp-metrology-inspection.md)
    // ========================================================================

    [Fact]
    public void Query_ThicknessMeasurement_ReturnsMetrologyDoc()
        => AssertTopKContainsDocument("두께 측정 metrology", "cmp-metrology-inspection.md");

    // ========================================================================
    // 11. 공정 매뉴얼 (cmp-process-manual.md)
    // ========================================================================

    [Fact]
    public void Query_CMPProcessOverview_ReturnsProcessDoc()
        => AssertTopKContainsDocument("CMP 공정 개요 연마 원리", "cmp-process-manual.md");

    // ========================================================================
    // 12. 장비 개요 (cmp-equipment-overview.md)
    // ========================================================================

    [Fact]
    public void Query_EquipmentStructure_ReturnsOverviewDoc()
        => AssertTopKContainsDocument("CMP 장비 구조 구성", "cmp-equipment-overview.md");

    // ========================================================================
    // Cross-document negative tests: verify wrong doc is NOT top-1
    // ========================================================================

    [Fact]
    public void Query_PadReplacement_DoesNotReturnParameterOptimization()
    {
        var results = _bm25.Search("패드 교체 판단 기준", 1);
        results.Should().NotBeEmpty();

        var topDoc = _chunkToDoc.GetValueOrDefault(results[0].DocumentId, "");
        topDoc.Should().NotBe("cmp-parameter-optimization.md",
            because: "pad replacement criteria should not return parameter optimization doc");
    }

    [Fact]
    public void Query_PadReplacement_DoesNotReturnAlarmCode()
    {
        var results = _bm25.Search("패드 교체 판단 기준", 1);
        results.Should().NotBeEmpty();

        var topDoc = _chunkToDoc.GetValueOrDefault(results[0].DocumentId, "");
        topDoc.Should().NotBe("cmp-alarm-code-reference.md",
            because: "pad replacement criteria should not return alarm code doc");
    }

    // ========================================================================
    // Section-level precision tests
    // ========================================================================

    [Fact]
    public void Query_PadReplacementCriteria_ReturnsSectionWithCriteriaTable()
    {
        var results = _bm25.Search("패드 교체 판단 기준", 3);
        var topChunkTexts = results
            .Where(r => _chunkToDoc.GetValueOrDefault(r.DocumentId) == "cmp-slurry-pad-replacement.md")
            .Select(r => GetChunkText(r.DocumentId))
            .ToList();

        topChunkTexts.Should().NotBeEmpty();
        // At least one chunk should contain the actual criteria values
        topChunkTexts.Any(t =>
            t.Contains("500시간") || t.Contains("1.0 mm") || t.Contains("패드 교체 판단 기준")
        ).Should().BeTrue(because: "should return the chunk containing pad replacement criteria");
    }

    [Fact]
    public void Query_SlurryLineFlush_ReturnsSectionWithFlushProcedure()
    {
        var results = _bm25.Search("슬러리 라인 플러시 절차", 3);
        var topChunkTexts = results
            .Where(r => _chunkToDoc.GetValueOrDefault(r.DocumentId) == "cmp-slurry-pad-replacement.md")
            .Select(r => GetChunkText(r.DocumentId))
            .ToList();

        topChunkTexts.Should().NotBeEmpty();
        topChunkTexts.Any(t =>
            t.Contains("500 ml/min") || t.Contains("플러시") || t.Contains("투명해질")
        ).Should().BeTrue(because: "should return the chunk about slurry line flush procedure");
    }

    [Fact]
    public void Query_PadQualification_ReturnsSectionWithQualificationSteps()
    {
        var results = _bm25.Search("패드 교체 Qualification", 5);
        var topChunkTexts = results
            .Where(r => _chunkToDoc.GetValueOrDefault(r.DocumentId) == "cmp-slurry-pad-replacement.md")
            .Select(r => GetChunkText(r.DocumentId))
            .ToList();

        topChunkTexts.Should().NotBeEmpty();
        topChunkTexts.Any(t =>
            t.Contains("더미 웨이퍼") || t.Contains("Test wafer 3매") || t.Contains("Qualification")
        ).Should().BeTrue(because: "should return the chunk about qualification after pad replacement");
    }

    // ========================================================================
    // FlattenMarkdownTables integration: verify flattened text is more searchable
    // ========================================================================

    [Fact]
    public void FlattenedChunks_ContainKeywordsFromTable()
    {
        var docPath = Path.Combine(DocsPath, "cmp-slurry-pad-replacement.md");
        var text = File.ReadAllText(docPath);
        var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);

        // Find the criteria chunk (chunk 0)
        var criteriaChunk = chunks.FirstOrDefault(c => c.Contains("교체 판단 기준") && c.Contains("500시간"));
        criteriaChunk.Should().NotBeNull("chunk with pad replacement criteria should exist");

        var flattened = DocumentIngestor.FlattenMarkdownTables(criteriaChunk!);

        // After flattening, the table data should be in natural language
        flattened.Should().Contain("사용 시간");
        flattened.Should().Contain("500시간");
        flattened.Should().Contain("패드 두께");
        flattened.Should().Contain("MRR 저하");
    }
}
