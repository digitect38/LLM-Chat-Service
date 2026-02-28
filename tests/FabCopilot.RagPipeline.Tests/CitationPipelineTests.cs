using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// 문서 참고(Citation) 파이프라인 종합 검증
/// - BuildSourceCitations: 임계값, 중복 제거, 메타데이터 추출
/// - FilterGhostCitations: 키워드 매칭, 경계 조건
/// - Gate A/B 상호작용
/// - 시스템 프롬프트 내 참조 문서 삽입
/// </summary>
public class CitationPipelineTests
{
    private static RetrievalResult MakeResult(float score, string fileName, string chunkText = "테스트 내용")
        => new()
        {
            DocumentId = $"doc-{fileName}",
            ChunkText = chunkText,
            Score = score,
            Metadata = new Dictionary<string, object> { ["file_name"] = fileName }
        };

    private static RetrievalResult MakeDetailedResult(float score, string fileName,
        string? chapter = null, string? section = null, int? page = null,
        int? lineStart = null, int? lineEnd = null)
    {
        var meta = new Dictionary<string, object> { ["file_name"] = fileName };
        if (chapter != null) meta["chapter"] = chapter;
        if (section != null) meta["section"] = section;
        if (page != null) meta["page_number"] = page.Value;
        if (lineStart != null) meta["line_start"] = lineStart.Value;
        if (lineEnd != null) meta["line_end"] = lineEnd.Value;
        return new()
        {
            DocumentId = $"doc-{fileName}",
            ChunkText = "CMP polishing pad replacement procedure step by step guide",
            Score = score,
            Metadata = meta
        };
    }

    // ═══════════════════════════════════════════════════════════
    // 핵심 버그 수정 검증: 0.45~0.55 점수 결과가 인용에 표시되는가
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceCitations_ScoreBelow055_StillCited()
    {
        // 이전 버그: _gateAThreshold(0.55)를 citation threshold로 사용하여
        // 0.45~0.55 점수 문서가 시스템 프롬프트에는 포함되지만 인용에서 누락됨
        var results = new List<RetrievalResult>
        {
            MakeResult(0.50f, "cmp-guide.pdf"),
            MakeResult(0.48f, "maintenance-manual.md")
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("참고 문서:");
        citations.Should().Contain("cmp-guide");
        citations.Should().Contain("maintenance-manual");
    }

    [Fact]
    public void BuildSourceCitations_DefaultThreshold_AllResultsCited()
    {
        // scoreThreshold 기본값 0 — 모든 검색 결과 인용
        var results = new List<RetrievalResult>
        {
            MakeResult(0.90f, "high-score.pdf"),
            MakeResult(0.50f, "mid-score.pdf"),
            MakeResult(0.10f, "low-score.pdf")
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("high-score");
        citations.Should().Contain("mid-score");
        citations.Should().Contain("low-score");
    }

    [Fact]
    public void BuildSourceCitations_ExplicitThreshold_FiltersCorrectly()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult(0.80f, "above.pdf"),
            MakeResult(0.40f, "below.pdf")
        };

        var citations = LlmWorker.BuildSourceCitations(results, 0.55f);

        citations.Should().Contain("above");
        citations.Should().NotContain("below");
    }

    // ═══════════════════════════════════════════════════════════
    // 인용 포맷 및 메타데이터 검증
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceCitations_IncludesScoreValue()
    {
        var results = new List<RetrievalResult> { MakeResult(0.876f, "test.pdf") };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("0.876");
    }

    [Fact]
    public void BuildSourceCitations_StripsFileExtensions()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult(0.80f, "guide.pdf"),
            MakeResult(0.75f, "manual.md"),
            MakeResult(0.70f, "readme.txt")
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().NotContain(".pdf");
        citations.Should().NotContain(".md");
        citations.Should().NotContain(".txt");
    }

    [Fact]
    public void BuildSourceCitations_WithChapterAndSection_IncludesDisplayRef()
    {
        var results = new List<RetrievalResult>
        {
            MakeDetailedResult(0.85f, "cmp-guide.pdf", chapter: "Ch3", section: "S2.1")
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("Ch3");
        citations.Should().Contain("S2.1");
    }

    [Fact]
    public void BuildSourceCitations_WithLineRange_IncludesLineInfo()
    {
        var results = new List<RetrievalResult>
        {
            MakeDetailedResult(0.85f, "cmp-guide.pdf", lineStart: 45, lineEnd: 67)
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("45");
        citations.Should().Contain("67");
    }

    [Fact]
    public void BuildSourceCitations_DeduplicatesBySameSourceName()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult(0.90f, "cmp-guide.pdf"),
            MakeResult(0.80f, "cmp-guide.pdf"),
            MakeResult(0.70f, "cmp-guide.pdf")
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        var count = citations.Split("cmp-guide").Length - 1;
        count.Should().Be(1, "같은 문서는 1회만 인용되어야 합니다");
    }

    [Fact]
    public void BuildSourceCitations_EmptyList_ReturnsEmpty()
    {
        LlmWorker.BuildSourceCitations(new List<RetrievalResult>()).Should().BeEmpty();
    }

    [Fact]
    public void BuildSourceCitations_NullList_ReturnsEmpty()
    {
        LlmWorker.BuildSourceCitations(null!).Should().BeEmpty();
    }

    [Fact]
    public void BuildSourceCitations_UnknownSource_Excluded()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "",
                ChunkText = "Some content",
                Score = 0.90f,
                Metadata = new Dictionary<string, object>()
            }
        };

        LlmWorker.BuildSourceCitations(results).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════
    // 메타데이터 폴백 체인: file_name → file_path → document_id
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceCitations_FallsBackToFilePath()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "Content",
                Score = 0.80f,
                Metadata = new Dictionary<string, object> { ["file_path"] = "/docs/troubleshooting.md" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("troubleshooting");
    }

    [Fact]
    public void BuildSourceCitations_FallsBackToDocumentId()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "cmp-safety-procedures",
                ChunkText = "Content",
                Score = 0.80f,
                Metadata = new Dictionary<string, object>()
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("cmp-safety-procedures");
    }

    // ═══════════════════════════════════════════════════════════
    // Ghost Citation 필터링
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FilterGhostCitations_MatchingKeywords_Kept()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "CMP polishing pad replacement slurry conditioning wafer removal rate",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-guide.pdf" }
            }
        };

        // Response that uses 3+ keywords from the chunk
        var response = "CMP 장비의 polishing pad를 교체하고 slurry를 투입합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, results);
        filtered.Should().HaveCount(1);
    }

    [Fact]
    public void FilterGhostCitations_NoMatchingKeywords_Excluded()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "CMP polishing pad replacement slurry conditioning wafer removal rate",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-guide.pdf" }
            }
        };

        // Response that doesn't use any keywords from the chunk
        var response = "네트워크 연결을 확인하고 방화벽 설정을 변경하세요.";

        var filtered = LlmWorker.FilterGhostCitations(response, results);
        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterGhostCitations_EmptyResponse_ReturnsEmpty()
    {
        var results = new List<RetrievalResult> { MakeResult(0.85f, "test.pdf") };
        LlmWorker.FilterGhostCitations("", results).Should().BeEmpty();
    }

    [Fact]
    public void FilterGhostCitations_EmptyResults_ReturnsEmpty()
    {
        LlmWorker.FilterGhostCitations("some response", new List<RetrievalResult>()).Should().BeEmpty();
    }

    [Fact]
    public void FilterGhostCitations_EmptyChunkText_KeptByDefault()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "test.pdf" }
            }
        };

        // Empty chunk text → ExtractKeywords returns [] → kept by default
        var filtered = LlmWorker.FilterGhostCitations("any response", results);
        filtered.Should().HaveCount(1);
    }

    // ═══════════════════════════════════════════════════════════
    // Gate A + Citation 상호작용 (통합 시나리오)
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0.46f)]
    [InlineData(0.50f)]
    [InlineData(0.54f)]
    public void LowConfidenceResults_StillGetCited(float score)
    {
        // RAG returns results in 0.45–0.55 range
        var results = new List<RetrievalResult> { MakeResult(score, "low-confidence-doc.pdf") };

        // Gate A says "not confident"
        var isConfident = LlmWorker.EvaluateConfidence(results, score, 0.55f);
        isConfident.Should().BeFalse();

        // But citations should still appear (with default threshold 0)
        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("low-confidence-doc");
        citations.Should().Contain("참고 문서:");
    }

    [Fact]
    public void SystemPrompt_LowConfidence_ContainsContextButWarning()
    {
        var results = new List<RetrievalResult> { MakeResult(0.50f, "guide.pdf", "CMP pad 교체 절차") };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: false);

        prompt.Should().Contain("LOW CONFIDENCE WARNING");
        prompt.Should().Contain("CMP pad 교체 절차"); // Content still shown to LLM
    }

    [Fact]
    public void SystemPrompt_HighConfidence_ContainsReferenceContext()
    {
        var results = new List<RetrievalResult> { MakeResult(0.80f, "guide.pdf", "CMP pad 교체 절차") };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident: true);

        prompt.Should().Contain("REFERENCE CONTEXT");
        prompt.Should().Contain("CMP pad 교체 절차");
        prompt.Should().NotContain("LOW CONFIDENCE WARNING");
    }

    [Fact]
    public void SystemPrompt_NoResults_ShowsNoDocumentsMessage()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>(), isConfident: true);

        prompt.Should().Contain("NO REFERENCE DOCUMENTS AVAILABLE");
    }

    // ═══════════════════════════════════════════════════════════
    // Strict 모드 vs Hybrid 모드 인용 시나리오
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void StrictMode_Score060_LowConfidence_ButStillCited()
    {
        var results = new List<RetrievalResult> { MakeResult(0.60f, "equipment-manual.pdf") };

        var threshold = LlmWorker.ComputeEffectiveThreshold("strict", 0.55f);
        var isConfident = LlmWorker.EvaluateConfidence(results, 0.60f, threshold);

        // Strict mode: 0.60 < 0.75 → not confident
        isConfident.Should().BeFalse();

        // But citations still generated
        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("equipment-manual");
    }

    [Fact]
    public void HybridMode_Score060_Confident_AndCited()
    {
        var results = new List<RetrievalResult> { MakeResult(0.60f, "equipment-manual.pdf") };

        var threshold = LlmWorker.ComputeEffectiveThreshold("hybrid", 0.55f);
        var isConfident = LlmWorker.EvaluateConfidence(results, 0.60f, threshold);

        // Hybrid mode: 0.60 >= 0.55 → confident
        isConfident.Should().BeTrue();

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("equipment-manual");
    }

    // ═══════════════════════════════════════════════════════════
    // 복합 시나리오: 다중 문서, 다양한 점수
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MultipleDocuments_AllCitedWithScores()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult(0.92f, "cmp-process-manual.pdf"),
            MakeResult(0.78f, "maintenance-guide.md"),
            MakeResult(0.51f, "troubleshooting-faq.txt"),
            MakeResult(0.46f, "safety-procedures.pdf")
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("cmp-process-manual");
        citations.Should().Contain("maintenance-guide");
        citations.Should().Contain("troubleshooting-faq");
        citations.Should().Contain("safety-procedures");
        citations.Should().Contain("0.920");
        citations.Should().Contain("0.780");
        citations.Should().Contain("0.510");
        citations.Should().Contain("0.460");
    }

    [Fact]
    public void MixedMetadata_AllSourcesExtracted()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "content",
                Score = 0.80f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "from-filename.pdf" }
            },
            new()
            {
                DocumentId = "doc-2",
                ChunkText = "content",
                Score = 0.70f,
                Metadata = new Dictionary<string, object> { ["file_path"] = "/docs/from-filepath.md" }
            },
            new()
            {
                DocumentId = "from-docid",
                ChunkText = "content",
                Score = 0.60f,
                Metadata = new Dictionary<string, object>()
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("from-filename");
        citations.Should().Contain("from-filepath"); // extension stripped by StripFileExtension
        citations.Should().Contain("from-docid");
    }
}
