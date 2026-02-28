using System.Text.Json;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.LlmService;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class LlmWorkerPromptTests
{
    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsReferenceContext()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "CMP polishing pad lifetime is 500 hours.", Score = 0.85f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("REFERENCE CONTEXT");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_DoesNotContainPermissiveLanguage()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Test content", Score = 0.9f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("you may ignore it");
        prompt.Should().NotContain("무시해도 됩니다");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_UsesAnonymizedContext()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Document content here.", Score = 0.9f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        // No document numbers or citation format references — uses <context> tags
        prompt.Should().Contain("<context>");
        prompt.Should().Contain("Document content here.");
        prompt.Should().NotContain("Document 1");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_NoDocumentNumbers()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "First document.", Score = 0.9f },
            new() { ChunkText = "Second document.", Score = 0.8f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("Document 1");
        prompt.Should().NotContain("Document 2");
        prompt.Should().Contain("First document.");
        prompt.Should().Contain("Second document.");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsNoRelevantDocsFallback()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Some content.", Score = 0.5f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("관련 정보가 없어");
    }

    [Fact]
    public void BuildSystemPrompt_WithRagResults_ContainsAntiContradictionRule()
    {
        var ragResults = new List<RetrievalResult>
        {
            new() { ChunkText = "Important data.", Score = 0.85f }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().Contain("모순");
    }

    [Fact]
    public void BuildSystemPrompt_WithoutRagResults_DoesNotContainRagSection()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>());

        prompt.Should().NotContain("REFERENCE CONTEXT");
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysContainsKoreanRule()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, new List<RetrievalResult>());

        prompt.Should().Contain("Korean");
        prompt.Should().Contain("한국어");
    }

    [Fact]
    public void BuildSystemPrompt_WithContext_IncludesEquipmentInfo()
    {
        var context = new EquipmentContext
        {
            Module = "Head1",
            Recipe = "OX_CMP_01"
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", context, new List<RetrievalResult>());

        prompt.Should().Contain("Head1");
        prompt.Should().Contain("OX_CMP_01");
    }

    [Fact]
    public void BuildSystemPrompt_WithJsonElementMetadata_ContentIncludedNotFileName()
    {
        // Simulate NATS JSON deserialization: metadata values arrive as JsonElement, not string
        var json = JsonSerializer.Deserialize<JsonElement>("\"cmp-troubleshooting.md\"");
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "CMP pad lifetime guide.",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = json }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        // Filename should NOT appear (anonymized), but chunk text should
        prompt.Should().NotContain("cmp-troubleshooting.md");
        prompt.Should().Contain("CMP pad lifetime guide.");
    }

    [Fact]
    public void BuildSystemPrompt_WithStringMetadata_ContentIncludedNotFileName()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Some content.",
                Score = 0.9f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "my-doc.txt" }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        prompt.Should().NotContain("my-doc.txt");
        prompt.Should().Contain("Some content.");
    }

    [Fact]
    public void BuildSystemPrompt_WithChapterSectionMetadata_NotExposedInPrompt()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Vacuum seal replacement.",
                Score = 0.9f,
                Metadata = new Dictionary<string, object>
                {
                    ["file_name"] = "manual.pdf",
                    ["chapter"] = "Ch3",
                    ["section"] = "3.2.1",
                    ["line_start"] = 142,
                    ["line_end"] = 158
                }
            }
        };

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults);

        // Metadata should not appear in prompt (anonymized context)
        prompt.Should().NotContain("chapter: Ch3");
        prompt.Should().NotContain("manual.pdf");
        prompt.Should().Contain("Vacuum seal replacement.");
    }

    [Fact]
    public void BuildDisplayRef_WithAllFields_ReturnsFullPrecisionFormat()
    {
        var lineRange = new LineRangeInfo { From = 142, To = 158 };

        var result = LlmWorker.BuildDisplayRef("MNL-2025-001", "Ch3", "3.2.1", lineRange, 42);

        result.Should().Be("MNL-2025-001-Ch3-S3.2.1-{Line:142-158}");
    }

    [Fact]
    public void BuildDisplayRef_WithoutLineRange_FallsBackToPage()
    {
        var result = LlmWorker.BuildDisplayRef("MNL-2025-001", "Ch3", "3.2.1", null, 42);

        result.Should().Be("MNL-2025-001-Ch3-S3.2.1-{Page:42}");
    }

    [Fact]
    public void BuildDisplayRef_WithOnlyDocId_ReturnsDocIdOnly()
    {
        var result = LlmWorker.BuildDisplayRef("MNL-2025-001", null, null, null, null);

        result.Should().Be("MNL-2025-001");
    }

    [Fact]
    public void ComputeLineRange_SingleLine_ReturnsLine1()
    {
        var text = "Hello world, no newlines here.";

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, 0, text.Length);

        lineStart.Should().Be(1);
        lineEnd.Should().Be(1);
    }

    [Fact]
    public void ComputeLineRange_MultipleLines_ReturnsCorrectRange()
    {
        var text = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        // "Line 3" starts at index 14, ends at index 20

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, 14, 20);

        lineStart.Should().Be(3);
        lineEnd.Should().Be(3);
    }

    [Fact]
    public void ComputeLineRange_SpanningMultipleLines_ReturnsSpan()
    {
        var text = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        // Span from "Line 2" (index 7) to end of "Line 4" (index 27)

        var (lineStart, lineEnd) = DocumentIngestor.ComputeLineRange(text, 7, 27);

        lineStart.Should().Be(2);
        lineEnd.Should().Be(4);
    }

    [Fact]
    public void BuildSourceCitations_WithMetadata_UsesPrecisionFormat()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                ChunkText = "Content",
                Score = 0.9f,
                Metadata = new Dictionary<string, object>
                {
                    ["file_name"] = "manual.pdf",
                    ["document_id"] = "MNL-001",
                    ["chapter"] = "Ch1",
                    ["section"] = "1.1",
                    ["line_start"] = 10,
                    ["line_end"] = 20
                }
            }
        };

        var result = LlmWorker.BuildSourceCitations(ragResults);

        result.Should().Contain("MNL-001-Ch1-S1.1-{Line:10-20}");
    }
}
