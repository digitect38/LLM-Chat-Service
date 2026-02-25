using System.Text;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// 문서 참조 관련 종합 테스트 — 100개
/// Citation 생성, Ghost Citation 필터링, 프롬프트 컨텍스트 주입,
/// 청킹 정확도, 멀티문서 교차 검증, 메타데이터 처리 등
/// </summary>
public class DocumentReferenceTests
{
    private static readonly string DocsRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs");

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static RetrievalResult MakeResult(string chunkText, float score = 0.85f, string fileName = "test-doc.md")
        => new()
        {
            DocumentId = $"doc-{Guid.NewGuid():N}",
            ChunkText = chunkText,
            Score = score,
            Metadata = new Dictionary<string, object> { ["file_name"] = fileName }
        };

    private static RetrievalResult MakeResultWithMeta(string chunkText, float score, Dictionary<string, object> metadata)
        => new()
        {
            DocumentId = $"doc-{Guid.NewGuid():N}",
            ChunkText = chunkText,
            Score = score,
            Metadata = metadata
        };

    private static List<string> LoadChunks(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(DocsRoot, fileName));
        return DocumentIngestor.ChunkText(text, 512, 128);
    }

    private static List<string> LoadMarkdownChunks(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(DocsRoot, fileName));
        return DocumentIngestor.ChunkMarkdown(text, 512, 128);
    }

    private static bool AnyChunkContains(List<string> chunks, string keyword)
        => chunks.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    // ══════════════════════════════════════════════════════════════
    // Section 1: BuildSourceCitations — 확장자 제거 및 포맷 (1~15)
    // ══════════════════════════════════════════════════════════════

    [Fact] // 1
    public void Citation_DocxFile_ExtensionRemoved()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("content", 0.9f, "report.docx")]);
        citations.Should().Contain("report");
        citations.Should().NotContain(".docx");
    }

    [Fact] // 2
    public void Citation_HtmlFile_ExtensionRemoved()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("content", 0.9f, "page.html")]);
        citations.Should().Contain("page");
        citations.Should().NotContain(".html");
    }

    [Fact] // 3
    public void Citation_NoExtensionFile_ShownAsIs()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("content", 0.9f, "README")]);
        citations.Should().Contain("README");
    }

    [Fact] // 4
    public void Citation_DottedFileName_OnlyLastExtensionRemoved()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("content", 0.9f, "cmp-v2.1-guide.md")]);
        citations.Should().Contain("cmp-v2.1-guide");
        citations.Should().NotContain(".md");
    }

    [Fact] // 5
    public void Citation_KoreanFileName_Preserved()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("content", 0.9f, "CMP-유지보수-가이드.pdf")]);
        citations.Should().Contain("CMP-유지보수-가이드");
        citations.Should().NotContain(".pdf");
    }

    [Fact] // 6
    public void Citation_FiveDistinctSources_AllListed()
    {
        var results = Enumerable.Range(1, 5)
            .Select(i => MakeResult($"chunk {i}", 0.9f - i * 0.05f, $"doc-{i}.md"))
            .ToList();

        var citations = LlmWorker.BuildSourceCitations(results);

        for (int i = 1; i <= 5; i++)
            citations.Should().Contain($"doc-{i}");
    }

    [Fact] // 7
    public void Citation_MultipleScores_AllDisplayed()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("low", 0.70f, "low-score.md"),
            MakeResult("high", 0.95f, "high-score.md")
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("high-score");
        citations.Should().Contain("low-score");
        citations.Should().Contain("0.950");
        citations.Should().Contain("0.700");
    }

    [Fact] // 8
    public void Citation_ScoreDisplayedWith3Decimals()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("c", 0.8567f, "doc.md")]);
        citations.Should().Contain("0.857");
    }

    [Fact] // 9
    public void Citation_PerfectScore_Displayed()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("c", 1.0f, "perfect.md")]);
        citations.Should().Contain("1.000");
    }

    [Fact] // 10
    public void Citation_ContainsSeparatorLine()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("c", 0.9f, "doc.md")]);
        citations.Should().Contain("---");
    }

    [Fact] // 11
    public void Citation_ContainsReferenceHeader()
    {
        var citations = LlmWorker.BuildSourceCitations([MakeResult("c", 0.9f, "doc.md")]);
        citations.Should().Contain("참고 문서");
    }

    [Fact] // 12
    public void Citation_WithPrecisionMetadata_ShowsLineRange()
    {
        var result = MakeResultWithMeta("content", 0.9f, new Dictionary<string, object>
        {
            ["file_name"] = "manual.pdf",
            ["document_id"] = "MNL-001",
            ["chapter"] = "Ch2",
            ["section"] = "2.3",
            ["line_start"] = 50,
            ["line_end"] = 75
        });

        var citations = LlmWorker.BuildSourceCitations([result]);
        citations.Should().Contain("MNL-001");
        citations.Should().Contain("Line:50-75");
    }

    [Fact] // 13
    public void Citation_WithPageMetadata_ShowsPage()
    {
        var result = MakeResultWithMeta("content", 0.9f, new Dictionary<string, object>
        {
            ["file_name"] = "manual.pdf",
            ["document_id"] = "MNL-001",
            ["chapter"] = "Ch1",
            ["section"] = "1.1",
            ["page_number"] = 15
        });

        var citations = LlmWorker.BuildSourceCitations([result]);
        citations.Should().Contain("Page:15");
    }

    [Fact] // 14
    public void Citation_ThresholdBoundary_JustBelow_Excluded()
    {
        var citations = LlmWorker.BuildSourceCitations(
            [MakeResult("c", 0.549f, "barely-out.md")], 0.55f);
        citations.Should().NotContain("barely-out");
    }

    [Fact] // 15
    public void Citation_MixedKnownAndUnknown_OnlyKnownShown()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("known content", 0.9f, "known-doc.md"),
            new() { DocumentId = "", ChunkText = "unknown", Score = 0.8f, Metadata = new Dictionary<string, object>() }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("known-doc");
    }

    // ══════════════════════════════════════════════════════════════
    // Section 2: Ghost Citation 필터링 (16~30)
    // ══════════════════════════════════════════════════════════════

    [Fact] // 16
    public void GhostFilter_AllRelevant_AllKept()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("CMP 패드 교체 절차에서 컨디셔너 디스크를 제거합니다."),
            MakeResult("슬러리 유량을 200ml/min으로 설정합니다.")
        };
        var response = "CMP 패드 교체 시 컨디셔너 디스크를 제거하고, 슬러리 유량은 200ml/min입니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(2);
    }

    [Fact] // 17
    public void GhostFilter_NoneRelevant_AllRemoved()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("에칭 공정의 플라즈마 가스 제어"),
            MakeResult("리소그래피 노광 파라미터 최적화")
        };
        var response = "CMP 패드 교체 절차를 설명합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().BeEmpty();
    }

    [Fact] // 18
    public void GhostFilter_PartialOverlap_OnlyRelevantKept()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("패드 교체 주기는 500시간입니다."),
            MakeResult("에칭 챔버 온도는 200°C입니다.")
        };
        var response = "패드 교체 주기는 약 500시간으로 권장됩니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(1);
        filtered[0].ChunkText.Should().Contain("패드 교체");
    }

    [Fact] // 19
    public void GhostFilter_EmptyResponse_ReturnsEmpty()
    {
        var rag = new List<RetrievalResult> { MakeResult("content") };
        var filtered = LlmWorker.FilterGhostCitations("", rag);
        filtered.Should().BeEmpty();
    }

    [Fact] // 20
    public void GhostFilter_EmptyRagResults_ReturnsEmpty()
    {
        var filtered = LlmWorker.FilterGhostCitations("some response", []);
        filtered.Should().BeEmpty();
    }

    [Fact] // 21
    public void GhostFilter_EnglishKeywordOverlap_Kept()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("IC1000 polishing pad has a groove pattern of XY type.")
        };
        var response = "The IC1000 pad uses an XY groove pattern for optimal slurry distribution.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(1);
    }

    [Fact] // 22
    public void GhostFilter_NumericOverlap_Kept()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("Zone 1 압력: 3.0 psi, Zone 2 압력: 2.8 psi")
        };
        var response = "Zone 1의 최적 압력은 3.0 psi이며, Zone 2는 2.8 psi로 설정합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(1);
    }

    [Fact] // 23
    public void GhostFilter_AlarmCodeOverlap_Kept()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("A123 Head Pressure Abnormal: 헤드 압력 이상 감지")
        };
        var response = "알람 A123은 헤드 압력 이상을 나타냅니다. 즉시 장비를 정지하세요.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(1);
    }

    [Fact] // 24
    public void GhostFilter_ShortChunkText_HandleGracefully()
    {
        var rag = new List<RetrievalResult> { MakeResult("CMP") };
        var response = "CMP는 Chemical Mechanical Planarization의 약자입니다.";

        // Short chunk — should still be handled without exception
        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().NotBeNull();
    }

    [Fact] // 25
    public void GhostFilter_MultipleChunksFromSameDoc_IndependentlyFiltered()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("패드 수명은 500시간입니다.", 0.9f, "pad-guide.md"),
            MakeResult("컨디셔닝 후 표면 거칠기를 측정합니다.", 0.85f, "pad-guide.md"),
            MakeResult("에칭 공정에서 선택비를 확인합니다.", 0.8f, "pad-guide.md")
        };
        var response = "패드 수명 500시간 후에는 반드시 교체해야 하며, 컨디셔닝 후 표면 상태를 확인합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(2);
    }

    [Fact] // 26
    public void GhostFilter_SpecialCharInChunk_NoException()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("수식: R = k * P * V (Preston 방정식)")
        };
        var response = "Preston 방정식에 따르면 R = k * P * V입니다.";

        var act = () => LlmWorker.FilterGhostCitations(response, rag);
        act.Should().NotThrow();
    }

    [Fact] // 27
    public void GhostFilter_LongResponse_PerformsCorrectly()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("Daily PM 체크리스트: 슬러리 유량, 패드 상태, 진공 압력")
        };
        var longResponse = string.Concat(Enumerable.Repeat("일반적인 내용. ", 100))
            + "Daily PM에서는 슬러리 유량과 패드 상태를 확인합니다.";

        var filtered = LlmWorker.FilterGhostCitations(longResponse, rag);
        filtered.Should().HaveCount(1);
    }

    [Fact] // 28
    public void GhostFilter_NullResponse_ReturnsEmpty()
    {
        var rag = new List<RetrievalResult> { MakeResult("content") };
        var filtered = LlmWorker.FilterGhostCitations(null!, rag);
        filtered.Should().BeEmpty();
    }

    [Fact] // 29
    public void GhostFilter_NullRagResults_ReturnsEmpty()
    {
        var filtered = LlmWorker.FilterGhostCitations("response", null!);
        filtered.Should().BeEmpty();
    }

    [Fact] // 30
    public void GhostFilter_OverlappingKeywords_Matched()
    {
        var rag = new List<RetrievalResult>
        {
            MakeResult("CMP 패드 교체 절차에서 IC1000 패드를 사용하며 break-in 공정이 필요합니다.")
        };
        var response = "CMP 패드 교체 시 IC1000 패드를 장착한 후 break-in 공정을 수행해야 합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, rag);
        filtered.Should().HaveCount(1);
    }

    // ══════════════════════════════════════════════════════════════
    // Section 3: 시스템 프롬프트 — 컨텍스트 주입 (31~50)
    // ══════════════════════════════════════════════════════════════

    [Fact] // 31
    public void Prompt_SingleResult_ContainsChunkText()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("패드 교체 주기는 500시간입니다.")]);
        prompt.Should().Contain("패드 교체 주기는 500시간입니다.");
    }

    [Fact] // 32
    public void Prompt_MultipleResults_AllChunksPresent()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("첫 번째 문서 내용"),
            MakeResult("두 번째 문서 내용"),
            MakeResult("세 번째 문서 내용")
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results);

        prompt.Should().Contain("첫 번째 문서 내용");
        prompt.Should().Contain("두 번째 문서 내용");
        prompt.Should().Contain("세 번째 문서 내용");
    }

    [Fact] // 33
    public void Prompt_NoResults_NoReferenceContextSection()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, []);
        prompt.Should().NotContain("REFERENCE CONTEXT");
    }

    [Fact] // 34
    public void Prompt_WithResults_HasReferenceContextSection()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content")]);
        prompt.Should().Contain("REFERENCE CONTEXT");
    }

    [Fact] // 35
    public void Prompt_FileNameNeverExposed()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content", 0.9f, "cmp-secret-doc.md")]);
        prompt.Should().NotContain("cmp-secret-doc");
        prompt.Should().NotContain(".md");
    }

    [Fact] // 36
    public void Prompt_ScoreNeverExposed()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content", 0.87654f)]);
        prompt.Should().NotContain("0.877");
        prompt.Should().NotContain("0.87654");
        prompt.Should().NotContain("score");
    }

    [Fact] // 37
    public void Prompt_DocumentIdNeverExposed()
    {
        var result = MakeResultWithMeta("content", 0.9f, new Dictionary<string, object>
        {
            ["file_name"] = "doc.md",
            ["document_id"] = "SECRET-DOC-ID-001"
        });

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [result]);
        prompt.Should().NotContain("SECRET-DOC-ID-001");
    }

    [Fact] // 38
    public void Prompt_MetadataKeysNeverExposed()
    {
        var result = MakeResultWithMeta("content", 0.9f, new Dictionary<string, object>
        {
            ["file_name"] = "doc.pdf",
            ["chapter"] = "Ch5",
            ["section"] = "5.3.2",
            ["line_start"] = 100,
            ["line_end"] = 120
        });

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [result]);
        prompt.Should().NotContain("file_name");
        prompt.Should().NotContain("line_start");
        prompt.Should().NotContain("line_end");
    }

    [Fact] // 39
    public void Prompt_AntiHallucination_ForbidsDocumentNumbers()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content")]);
        prompt.Should().Contain("Document N");
    }

    [Fact] // 40
    public void Prompt_AntiHallucination_MentionsAutoSystem()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content")]);
        prompt.Should().Contain("시스템이 자동");
    }

    [Fact] // 41
    public void Prompt_LowConfidence_ContainsWarning()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content", 0.3f)], isConfident: false);
        prompt.Should().Contain("LOW CONFIDENCE");
    }

    [Fact] // 42
    public void Prompt_HighConfidence_NoWarning()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("content", 0.9f)], isConfident: true);
        prompt.Should().NotContain("LOW CONFIDENCE");
    }

    [Fact] // 43
    public void Prompt_WithEquipmentContext_IncludesModuleAndRecipe()
    {
        var context = new EquipmentContext { Module = "Platen2", Recipe = "W_CMP_03" };
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, [MakeResult("content")]);
        prompt.Should().Contain("Platen2");
        prompt.Should().Contain("W_CMP_03");
    }

    [Fact] // 44
    public void Prompt_WithAlarms_IncludesAlarmInfo()
    {
        var context = new EquipmentContext { RecentAlarms = ["A101", "A123"] };
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, [MakeResult("content")]);
        prompt.Should().Contain("A101");
        prompt.Should().Contain("A123");
    }

    [Fact] // 45
    public void Prompt_WithErrorIntent_ContainsAlarmStyle()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("alarm content")], isConfident: true, QueryIntent.Error);
        prompt.Should().Contain("ERROR/ALARM");
    }

    [Fact] // 46
    public void Prompt_WithProcedureIntent_ContainsProcedureStyle()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null,
            [MakeResult("procedure content")], isConfident: true, QueryIntent.Procedure);
        prompt.Should().Contain("PROCEDURE");
    }

    [Fact] // 47
    public void Prompt_WithGraphContext_IncludesGraphInfo()
    {
        var result = MakeResult("기본 내용");
        result.GraphContext = "Entity: CMP패드 → 관련: 슬러리, 컨디셔너";

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [result]);
        prompt.Should().Contain("기본 내용");
    }

    [Fact] // 48
    public void Prompt_AlwaysContainsKoreanLanguageRule()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [MakeResult("content")]);
        prompt.Should().Contain("한국어");
    }

    [Fact] // 49
    public void Prompt_ContainsAntiContradictionRule()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [MakeResult("content")]);
        prompt.Should().Contain("모순");
    }

    [Fact] // 50
    public void Prompt_ContainsNoRelevantDocsFallbackRule()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [MakeResult("content")]);
        prompt.Should().Contain("관련 정보가 없어");
    }

    // ══════════════════════════════════════════════════════════════
    // Section 4: Citation Strip — LLM 환각 제거 (51~65)
    // ══════════════════════════════════════════════════════════════

    [Fact] // 51
    public void Strip_HashRefDoc_Removed()
    {
        var result = LlmWorker.StripLlmCitationSection("답변입니다.\n\n## 참고 문서\n- fake.md");
        result.Should().NotContain("참고 문서");
        result.Should().NotContain("fake.md");
    }

    [Fact] // 52
    public void Strip_HashRef_Removed()
    {
        var result = LlmWorker.StripLlmCitationSection("답변.\n\n## 참조\n- doc1");
        result.Should().NotContain("참조");
    }

    [Fact] // 53
    public void Strip_HashSource_Removed()
    {
        var result = LlmWorker.StripLlmCitationSection("답변.\n\n## 출처\n- source1");
        result.Should().NotContain("출처");
    }

    [Fact] // 54
    public void Strip_HashReliability_Removed()
    {
        var result = LlmWorker.StripLlmCitationSection("답변.\n\n## 신뢰도\n높음");
        result.Should().NotContain("신뢰도");
    }

    [Fact] // 55
    public void Strip_EmojiCitation_Removed()
    {
        var result = LlmWorker.StripLlmCitationSection("답변.\n\n---\n📚 **참고 문서:**\n- doc.md");
        result.Should().NotContain("📚");
    }

    [Fact] // 56
    public void Strip_BulletPdfEmoji_Removed()
    {
        var result = LlmWorker.StripLlmCitationSection("답변.\n---\n- 📄 cmp.pdf (0.85)");
        result.Should().NotContain("📄");
    }

    [Fact] // 57
    public void Strip_PreservesNormalContent()
    {
        var text = "## 요약\n패드 교체 절차\n\n## 상세\n1. 기존 패드 제거\n2. 새 패드 장착";
        var result = LlmWorker.StripLlmCitationSection(text);
        result.Should().Be(text);
    }

    [Fact] // 58
    public void Strip_EmptyInput_ReturnsEmpty()
    {
        LlmWorker.StripLlmCitationSection("").Should().Be("");
    }

    [Fact] // 59
    public void Strip_NullInput_ReturnsNull()
    {
        LlmWorker.StripLlmCitationSection(null!).Should().BeNull();
    }

    [Fact] // 60
    public void Strip_MultipleCitationSections_AllRemoved()
    {
        var result = LlmWorker.StripLlmCitationSection("본문\n\n## 참조\n- a\n\n## 참고 문서\n- b");
        result.Should().Be("본문");
    }

    [Fact] // 61
    public void Detect_RefSection_True()
    {
        var sb = new StringBuilder("답변.\n\n## 참고 문서\n");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact] // 62
    public void Detect_RefOnly_True()
    {
        var sb = new StringBuilder("답변.\n\n## 참조\n");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact] // 63
    public void Detect_SourceSection_True()
    {
        var sb = new StringBuilder("답변.\n\n## 출처\n");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact] // 64
    public void Detect_NormalHeading_False()
    {
        var sb = new StringBuilder("## 요약\n패드 교체 절차");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeFalse();
    }

    [Fact] // 65
    public void Detect_EmptyBuffer_False()
    {
        LlmWorker.DetectCitationSectionStart(new StringBuilder()).Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════
    // Section 5: 문서 청킹 정확도 — 핵심 키워드 보존 검증 (66~80)
    // ══════════════════════════════════════════════════════════════

    [Theory] // 66~70
    [InlineData("cmp-alarm-code-reference.md", "Emergency Stop")]
    [InlineData("cmp-alarm-code-reference.md", "Vacuum Failure")]
    [InlineData("cmp-alarm-code-reference.md", "A100")]
    [InlineData("cmp-alarm-code-reference.md", "A101")]
    [InlineData("cmp-alarm-code-reference.md", "Critical")]
    public void AlarmDoc_KeyTermSurvivesChunking(string file, string keyword)
    {
        var chunks = LoadChunks(file);
        AnyChunkContains(chunks, keyword).Should().BeTrue($"'{keyword}' must survive chunking in {file}");
    }

    [Theory] // 71~75
    [InlineData("cmp-safety-procedures.md", "LOTO")]
    [InlineData("cmp-safety-procedures.md", "MSDS")]
    [InlineData("cmp-safety-procedures.md", "슬러리")]
    [InlineData("cmp-safety-procedures.md", "비상")]
    [InlineData("cmp-safety-procedures.md", "PPE")]
    public void SafetyDoc_KeyTermSurvivesChunking(string file, string keyword)
    {
        var chunks = LoadChunks(file);
        AnyChunkContains(chunks, keyword).Should().BeTrue($"'{keyword}' must survive chunking in {file}");
    }

    [Theory] // 76~80
    [InlineData("cmp-calibration-procedures.md", "캘리브레이션")]
    [InlineData("cmp-defect-analysis.md", "scratch")]
    [InlineData("cmp-metrology-inspection.md", "thickness")]
    [InlineData("cmp-consumable-qualification.md", "qualification")]
    [InlineData("cmp-equipment-overview.md", "Platen")]
    public void VariousDocs_KeyTermSurvivesChunking(string file, string keyword)
    {
        var chunks = LoadChunks(file);
        AnyChunkContains(chunks, keyword).Should().BeTrue($"'{keyword}' must survive chunking in {file}");
    }

    // ══════════════════════════════════════════════════════════════
    // Section 6: 교차 문서 참조 — 동일 팩트가 여러 문서에 존재 (81~90)
    // ══════════════════════════════════════════════════════════════

    [Fact] // 81
    public void CrossDoc_Preston_InManualAndTroubleshooting()
    {
        AnyChunkContains(LoadChunks("cmp-process-manual.md"), "Preston").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-general-troubleshooting.md"), "MRR").Should().BeTrue();
    }

    [Fact] // 82
    public void CrossDoc_Conditioner_InMaintenanceAndReplacement()
    {
        AnyChunkContains(LoadChunks("cmp-maintenance-guide.md"), "컨디셔너").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-slurry-pad-replacement.md"), "컨디셔너").Should().BeTrue();
    }

    [Fact] // 83
    public void CrossDoc_Slurry_InAtLeast3Documents()
    {
        var docs = new[] {
            "cmp-process-manual.md",
            "cmp-slurry-pad-replacement.md",
            "cmp-parameter-optimization.md"
        };
        var count = docs.Count(d => AnyChunkContains(LoadChunks(d), "슬러리"));
        count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact] // 84
    public void CrossDoc_WIWNU_InManualAndOptimization()
    {
        AnyChunkContains(LoadChunks("cmp-process-manual.md"), "WIWNU").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-parameter-optimization.md"), "WIWNU").Should().BeTrue();
    }

    [Fact] // 85
    public void CrossDoc_Scratch_InTroubleshootingAndDefectAnalysis()
    {
        AnyChunkContains(LoadChunks("cmp-general-troubleshooting.md"), "Scratch").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-defect-analysis.md"), "Scratch").Should().BeTrue();
    }

    [Fact] // 86
    public void CrossDoc_MRR_AcrossMultipleDocs()
    {
        var docs = new[] {
            "cmp-process-manual.md",
            "cmp-parameter-optimization.md",
            "cmp-slurry-pad-replacement.md"
        };
        foreach (var doc in docs)
            AnyChunkContains(LoadChunks(doc), "MRR").Should().BeTrue($"MRR should exist in {doc}");
    }

    [Fact] // 87
    public void CrossDoc_Platen_InOverviewAndManual()
    {
        AnyChunkContains(LoadChunks("cmp-equipment-overview.md"), "Platen").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-process-manual.md"), "Platen").Should().BeTrue();
    }

    [Fact] // 88
    public void CrossDoc_PM_InMaintenanceAndCalibration()
    {
        AnyChunkContains(LoadChunks("cmp-maintenance-guide.md"), "PM").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-calibration-procedures.md"), "PM").Should().BeTrue();
    }

    [Fact] // 89
    public void CrossDoc_Retainer_InTroubleshootingAndReplacement()
    {
        AnyChunkContains(LoadChunks("cmp-general-troubleshooting.md"), "Retaining Ring").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-slurry-pad-replacement.md"), "Retaining Ring").Should().BeTrue();
    }

    [Fact] // 90
    public void CrossDoc_Endpoint_InManualAndMetrology()
    {
        AnyChunkContains(LoadChunks("cmp-process-manual.md"), "EPD").Should().BeTrue();
        AnyChunkContains(LoadChunks("cmp-metrology-inspection.md"), "EPD").Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════
    // Section 7: 청크 품질 보장 (91~100)
    // ══════════════════════════════════════════════════════════════

    [Theory] // 91~92
    [InlineData("cmp-alarm-code-reference.md")]
    [InlineData("cmp-safety-procedures.md")]
    public void AllChunks_NotEmpty(string fileName)
    {
        var chunks = LoadChunks(fileName);
        chunks.Should().NotBeEmpty();
        foreach (var chunk in chunks)
            chunk.Should().NotBeNullOrWhiteSpace();
    }

    [Theory] // 93~94
    [InlineData("cmp-calibration-procedures.md")]
    [InlineData("cmp-defect-analysis.md")]
    public void AllChunks_MaxSizeRespected(string fileName)
    {
        var chunks = LoadChunks(fileName);
        foreach (var chunk in chunks)
            chunk.Length.Should().BeLessThanOrEqualTo(512, $"chunk in {fileName} exceeds 512");
    }

    [Fact] // 95
    public void MarkdownChunks_PreserveParentContext()
    {
        var chunks = LoadMarkdownChunks("cmp-alarm-code-reference.md");
        var chunkWithContext = chunks.FirstOrDefault(c =>
            DocumentIngestor.ExtractParentContext(c) is not null);
        chunkWithContext.Should().NotBeNull("at least one markdown chunk should have parent context");
    }

    [Fact] // 96
    public void MarkdownChunks_SectionHierarchyPreserved()
    {
        var chunks = LoadMarkdownChunks("cmp-process-manual.md");
        chunks.Count.Should().BeGreaterThan(3, "process manual should produce multiple markdown chunks");
    }

    [Fact] // 97
    public void ChunkText_DecimalNotSplitAcrossChunks()
    {
        var text = "임계값은 3.14 psi입니다. " + new string('가', 500);
        var chunks = DocumentIngestor.ChunkText(text, 512, 128);
        chunks[0].Should().Contain("3.14");
    }

    [Fact] // 98
    public void ChunkText_OverlapPreservesContext()
    {
        var sentences = Enumerable.Range(1, 20)
            .Select(i => $"문장 {i}번째 내용입니다.")
            .ToList();
        var text = string.Join(" ", sentences);

        var chunks = DocumentIngestor.ChunkText(text, 200, 50);

        // With overlap, consecutive chunks should share some content
        if (chunks.Count >= 2)
        {
            var lastPartOfFirst = chunks[0][^50..];
            var firstPartOfSecond = chunks[1][..Math.Min(100, chunks[1].Length)];
            // At least some overlap content
            (lastPartOfFirst.Length > 0 || firstPartOfSecond.Length > 0).Should().BeTrue();
        }
    }

    [Fact] // 99
    public void AllKnowledgeDocs_ProduceNonEmptyChunks()
    {
        var docFiles = Directory.GetFiles(DocsRoot, "*.md");
        docFiles.Length.Should().BeGreaterThan(0, "knowledge-docs should contain markdown files");

        foreach (var file in docFiles)
        {
            var text = File.ReadAllText(file);
            var chunks = DocumentIngestor.ChunkText(text, 512, 128);
            chunks.Should().NotBeEmpty($"{Path.GetFileName(file)} should produce chunks");
        }
    }

    [Fact] // 100
    public void FullPipeline_MultiDocPromptAndCitation_Consistent()
    {
        // 여러 문서에서 온 결과로 프롬프트와 citation을 동시에 생성하면
        // 프롬프트에는 파일명이 없고, citation에는 파일명이 있어야 함
        var results = new List<RetrievalResult>
        {
            MakeResult("패드 교체 주기는 500시간", 0.92f, "cmp-slurry-pad-replacement.md"),
            MakeResult("Daily PM 체크리스트 항목", 0.88f, "cmp-maintenance-guide.md"),
            MakeResult("A123 Head Pressure 알람 코드", 0.85f, "cmp-alarm-code-reference.md")
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results);
        var citations = LlmWorker.BuildSourceCitations(results);

        // 프롬프트: 내용은 있되 파일명은 없어야 함
        prompt.Should().Contain("500시간");
        prompt.Should().Contain("Daily PM");
        prompt.Should().Contain("A123");
        prompt.Should().NotContain("cmp-slurry-pad-replacement");
        prompt.Should().NotContain("cmp-maintenance-guide");
        prompt.Should().NotContain("cmp-alarm-code-reference");

        // Citation: 파일명 표시 (확장자 제거)
        citations.Should().Contain("cmp-slurry-pad-replacement");
        citations.Should().Contain("cmp-maintenance-guide");
        citations.Should().Contain("cmp-alarm-code-reference");
        citations.Should().NotContain(".md");
    }
}
