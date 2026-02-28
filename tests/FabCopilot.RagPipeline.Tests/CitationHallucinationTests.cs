using System.Text;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Citation Hallucination Prevention 테스트 — 유령 참조 방지
/// StripLlmCitationSection, DetectCitationSectionStart, BuildSourceCitations 필터링
/// </summary>
public class CitationHallucinationTests
{
    // ──────────────────────────────────────────────────────────────
    // StripLlmCitationSection — 다양한 citation 섹션 패턴 제거
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void StripLlmCitationSection_WithHashRefSection_StripsFromSection()
    {
        var text = "## 요약\nCMP 설명입니다.\n\n## 참고 문서\n- 가짜문서.md";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Contain("CMP 설명입니다");
        result.Should().NotContain("참고 문서");
        result.Should().NotContain("가짜문서");
    }

    [Fact]
    public void StripLlmCitationSection_WithEmojiCitationHeader_Strips()
    {
        var text = "답변 내용입니다.\n\n---\n📚 **참고 문서:**\n- 📄 fake-doc.md";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Contain("답변 내용입니다");
        result.Should().NotContain("📚");
        result.Should().NotContain("fake-doc");
    }

    [Fact]
    public void StripLlmCitationSection_WithHashRef_Strips()
    {
        var text = "내용\n\n## 참조\n- doc1\n- doc2";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Be("내용");
    }

    [Fact]
    public void StripLlmCitationSection_WithSourceSection_Strips()
    {
        var text = "본문 내용\n\n## 출처\n- 문서1\n- 문서2";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Contain("본문 내용");
        result.Should().NotContain("출처");
    }

    [Fact]
    public void StripLlmCitationSection_WithReliabilitySection_Strips()
    {
        var text = "내용입니다.\n\n## 신뢰도\n높음";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Contain("내용입니다");
        result.Should().NotContain("신뢰도");
    }

    [Fact]
    public void StripLlmCitationSection_WithBulletCitation_Strips()
    {
        var text = "답변 내용\n\n---\n- 📄 cmp-guide.md (score: 0.85)";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Contain("답변 내용");
        result.Should().NotContain("📄");
    }

    [Fact]
    public void StripLlmCitationSection_NoCitationSection_ReturnsOriginal()
    {
        var text = "## 요약\nCMP 설명입니다.\n\n## 상세\n1. 절차...";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Be(text);
    }

    [Fact]
    public void StripLlmCitationSection_EmptyString_ReturnsEmpty()
    {
        LlmWorker.StripLlmCitationSection("").Should().Be("");
    }

    [Fact]
    public void StripLlmCitationSection_Null_ReturnsNull()
    {
        LlmWorker.StripLlmCitationSection(null!).Should().BeNull();
    }

    [Fact]
    public void StripLlmCitationSection_MultipleSections_StripsFromEarliest()
    {
        var text = "본문\n\n## 참조\n- doc1\n\n## 참고 문서\n- doc2";

        var result = LlmWorker.StripLlmCitationSection(text);

        result.Should().Be("본문");
    }

    // ──────────────────────────────────────────────────────────────
    // DetectCitationSectionStart — 실시간 스트리밍 감지
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DetectCitationSectionStart_WithRefSection_ReturnsTrue()
    {
        var sb = new StringBuilder("내용입니다.\n\n## 참고 문서\n");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact]
    public void DetectCitationSectionStart_WithRefOnly_ReturnsTrue()
    {
        var sb = new StringBuilder("내용입니다.\n\n## 참조\n");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact]
    public void DetectCitationSectionStart_WithRefAtEnd_ReturnsTrue()
    {
        var sb = new StringBuilder("내용입니다.\n\n## 참조");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact]
    public void DetectCitationSectionStart_WithSourceSection_ReturnsTrue()
    {
        var sb = new StringBuilder("내용입니다.\n\n## 출처\n");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact]
    public void DetectCitationSectionStart_WithEmojiHeader_ReturnsTrue()
    {
        var sb = new StringBuilder("내용입니다.\n---\n📚 **참고 문서:**");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact]
    public void DetectCitationSectionStart_WithBulletCitation_ReturnsTrue()
    {
        var sb = new StringBuilder("내용입니다.\n---\n- 📄 doc.pdf");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeTrue();
    }

    [Fact]
    public void DetectCitationSectionStart_NormalContent_ReturnsFalse()
    {
        var sb = new StringBuilder("## 요약\nCMP 설명입니다.\n\n## 상세\n1. 절차");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeFalse();
    }

    [Fact]
    public void DetectCitationSectionStart_Empty_ReturnsFalse()
    {
        var sb = new StringBuilder("");
        LlmWorker.DetectCitationSectionStart(sb).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────
    // BuildSourceCitations — score threshold 필터링
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSourceCitations_WithThreshold_FiltersLowScores()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("high-doc.pdf", 0.9f),
            MakeResult("low-doc.pdf", 0.3f)
        };

        var citations = LlmWorker.BuildSourceCitations(results, 0.55f);

        citations.Should().Contain("high-doc");
        citations.Should().NotContain("low-doc");
    }

    [Fact]
    public void BuildSourceCitations_AllBelowThreshold_ReturnsEmpty()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("doc.pdf", 0.3f)
        };

        var citations = LlmWorker.BuildSourceCitations(results, 0.55f);

        citations.Should().BeEmpty();
    }

    [Fact]
    public void BuildSourceCitations_ExactlyAtThreshold_Included()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("threshold-doc.md", 0.55f)
        };

        var citations = LlmWorker.BuildSourceCitations(results, 0.55f);

        citations.Should().Contain("threshold-doc");
    }

    // ──────────────────────────────────────────────────────────────
    // BuildSourceCitations — 모든 RAG 소스 표시 (확장자 제거)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSourceCitations_MdFile_IncludedWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "content", Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "guide.md" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("guide");
        citations.Should().NotContain(".md");
    }

    [Fact]
    public void BuildSourceCitations_TxtFile_IncludedWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "content", Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "notes.txt" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("notes");
        citations.Should().NotContain(".txt");
    }

    [Fact]
    public void BuildSourceCitations_PdfFile_IncludedWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("manual.pdf", 0.9f)
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("manual");
        citations.Should().Contain("참고 문서");
    }

    [Fact]
    public void BuildSourceCitations_WithUrl_Included()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "content", Score = 0.9f,
                Metadata = new Dictionary<string, object>
                {
                    ["file_name"] = "guide.md",
                    ["url"] = "https://example.com/guide"
                }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("참고 문서");
    }

    [Fact]
    public void BuildSourceCitations_WithSourceUrl_Included()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "content", Score = 0.9f,
                Metadata = new Dictionary<string, object>
                {
                    ["file_name"] = "guide.md",
                    ["source_url"] = "https://docs.example.com/page"
                }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("참고 문서");
    }

    [Fact]
    public void BuildSourceCitations_MixedSources_AllShownWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("manual.pdf", 0.9f),
            new()
            {
                ChunkText = "md content", Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "guide.md" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("manual");
        citations.Should().Contain("guide");
        citations.Should().NotContain(".pdf");
        citations.Should().NotContain(".md");
    }

    // ──────────────────────────────────────────────────────────────
    // BuildSystemPrompt — 익명화된 컨텍스트 (Document N, 파일명 제거)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithRagResults_NoDocumentNumbersOrFileNames()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "First content", Score = 0.9f,
                     Metadata = new Dictionary<string, object> { ["file_name"] = "doc.md" } },
            new() { ChunkText = "Second content", Score = 0.8f,
                     Metadata = new Dictionary<string, object> { ["file_name"] = "doc2.md" } }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("Document 1");
        prompt.Should().NotContain("Document 2");
        prompt.Should().NotContain("doc.md");
        prompt.Should().NotContain("doc2.md");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_UsesContextTags()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "CMP pad info here", Score = 0.9f,
                     Metadata = new Dictionary<string, object>() }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("<context>");
        prompt.Should().Contain("</context>");
        prompt.Should().Contain("CMP pad info here");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_NoScoresInPrompt()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Content", Score = 0.857f,
                Metadata = new Dictionary<string, object>
                {
                    ["file_name"] = "manual.pdf",
                    ["chapter"] = "Ch3",
                    ["section"] = "3.2.1"
                }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("0.857");
        prompt.Should().NotContain("score:");
        prompt.Should().NotContain("chapter: Ch3");
        prompt.Should().NotContain("manual.pdf");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsAntiHallucinationRules()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "test", Score = 0.9f, Metadata = new Dictionary<string, object>() }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        // Must forbid fabricating document references
        prompt.Should().Contain("Document N");
        prompt.Should().Contain("절대");
        prompt.Should().Contain("시스템이 자동");
    }

    [Fact]
    public void BuildSystemPrompt_LowConfidence_UsesContextTag()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Low confidence content", Score = 0.3f,
                     Metadata = new Dictionary<string, object> { ["file_name"] = "doc.md" } }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults, isConfident: false);

        prompt.Should().Contain("<context");
        prompt.Should().Contain("Low confidence content");
        prompt.Should().NotContain("Document 1");
        prompt.Should().NotContain("doc.md");
    }

    // ──────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────

    private static RetrievalResult MakeResult(string fileName, float score)
        => new()
        {
            ChunkText = $"Content from {fileName}",
            Score = score,
            Metadata = new Dictionary<string, object> { ["file_name"] = fileName }
        };
}
