using System.Text.Json;
using FabCopilot.Contracts.Messages;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class ChatStreamChunkSerializationTests
{
    [Fact]
    public void ChatStreamChunk_RoundTrip_TokenConversationIdIsComplete()
    {
        var chunk = new ChatStreamChunk
        {
            ConversationId = "conv-123",
            Token = "Hello",
            IsComplete = false
        };

        var json = JsonSerializer.Serialize(chunk);
        var deserialized = JsonSerializer.Deserialize<ChatStreamChunk>(json)!;

        deserialized.ConversationId.Should().Be("conv-123");
        deserialized.Token.Should().Be("Hello");
        deserialized.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void ChatStreamChunk_NullCitations_OmittedOrNullInJson()
    {
        var chunk = new ChatStreamChunk
        {
            ConversationId = "conv-1",
            Token = "test",
            IsComplete = false,
            Citations = null
        };

        var json = JsonSerializer.Serialize(chunk);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("citations", out var citationsProp))
        {
            citationsProp.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public void ChatStreamChunk_ErrorField_SerializedCorrectly()
    {
        var chunk = new ChatStreamChunk
        {
            ConversationId = "conv-1",
            Token = "",
            IsComplete = true,
            Error = "Something went wrong"
        };

        var json = JsonSerializer.Serialize(chunk);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("error").GetString().Should().Be("Something went wrong");
    }

    [Fact]
    public void CitationInfo_AllFields_JsonContainsAllPropertyNames()
    {
        var citation = new CitationInfo
        {
            CitationId = "cite-1",
            DocId = "DOC-001",
            FileName = "test.md",
            ChunkId = "chunk-1",
            ChunkText = "Sample text",
            Chapter = "Ch1",
            Section = "1.1",
            Page = 5,
            CharOffsetStart = 100,
            CharOffsetEnd = 200,
            PdfUrl = "/api/documents/test.pdf",
            ParentContext = "Overview > Details",
            Score = 0.85f,
            HighlightType = "text",
            Revision = "v1.0",
            LineRange = new LineRangeInfo { From = 10, To = 20 },
            DisplayRef = "DOC-001-Ch1-S1.1-{Line:10-20}"
        };

        var json = JsonSerializer.Serialize(citation);

        json.Should().Contain("\"citationId\"");
        json.Should().Contain("\"docId\"");
        json.Should().Contain("\"fileName\"");
        json.Should().Contain("\"chunkId\"");
        json.Should().Contain("\"chunkText\"");
        json.Should().Contain("\"chapter\"");
        json.Should().Contain("\"section\"");
        json.Should().Contain("\"page\"");
        json.Should().Contain("\"charOffsetStart\"");
        json.Should().Contain("\"charOffsetEnd\"");
        json.Should().Contain("\"pdfUrl\"");
        json.Should().Contain("\"parentContext\"");
        json.Should().Contain("\"score\"");
        json.Should().Contain("\"highlightType\"");
        json.Should().Contain("\"revision\"");
        json.Should().Contain("\"lineRange\"");
        json.Should().Contain("\"displayRef\"");
    }

    [Fact]
    public void CitationInfo_NullOptionalFields_OmittedOrNull()
    {
        var citation = new CitationInfo
        {
            CitationId = "cite-1",
            DocId = "DOC-001",
            FileName = "test.md",
            Score = 0.9f
        };

        var json = JsonSerializer.Serialize(citation);
        var doc = JsonDocument.Parse(json);

        // Optional fields should be null when not set
        if (doc.RootElement.TryGetProperty("page", out var pageProp))
        {
            pageProp.ValueKind.Should().Be(JsonValueKind.Null);
        }
        if (doc.RootElement.TryGetProperty("pdfUrl", out var pdfUrlProp))
        {
            pdfUrlProp.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public void CitationInfo_Score_SerializedAsFloat()
    {
        var citation = new CitationInfo
        {
            CitationId = "cite-1",
            DocId = "DOC-001",
            FileName = "test.md",
            Score = 0.875f
        };

        var json = JsonSerializer.Serialize(citation);
        var doc = JsonDocument.Parse(json);

        var score = doc.RootElement.GetProperty("score").GetSingle();
        score.Should().BeApproximately(0.875f, 0.001f);
    }

    [Fact]
    public void LineRangeInfo_RoundTrip_FromAndToPreserved()
    {
        var lineRange = new LineRangeInfo { From = 42, To = 58 };

        var json = JsonSerializer.Serialize(lineRange);
        var deserialized = JsonSerializer.Deserialize<LineRangeInfo>(json)!;

        deserialized.From.Should().Be(42);
        deserialized.To.Should().Be(58);
    }

    [Fact]
    public void LineRangeInfo_SingleLine_FromEqualsTo()
    {
        var lineRange = new LineRangeInfo { From = 10, To = 10 };

        var json = JsonSerializer.Serialize(lineRange);
        var deserialized = JsonSerializer.Deserialize<LineRangeInfo>(json)!;

        deserialized.From.Should().Be(deserialized.To);
    }

    [Fact]
    public void CitationInfo_NestedLineRange_SerializedCorrectly()
    {
        var citation = new CitationInfo
        {
            CitationId = "cite-1",
            DocId = "DOC-001",
            FileName = "test.md",
            Score = 0.9f,
            LineRange = new LineRangeInfo { From = 5, To = 15 }
        };

        var json = JsonSerializer.Serialize(citation);
        var doc = JsonDocument.Parse(json);

        var lineRange = doc.RootElement.GetProperty("lineRange");
        lineRange.GetProperty("from").GetInt32().Should().Be(5);
        lineRange.GetProperty("to").GetInt32().Should().Be(15);
    }

    [Fact]
    public void CitationInfo_DisplayRef_IncludedInJson()
    {
        var citation = new CitationInfo
        {
            CitationId = "cite-1",
            DocId = "DOC-001",
            FileName = "test.md",
            Score = 0.9f,
            DisplayRef = "DOC-001-Ch3-S3.2-{Line:10-20}"
        };

        var json = JsonSerializer.Serialize(citation);

        json.Should().Contain("DOC-001-Ch3-S3.2-{Line:10-20}");
    }

    [Fact]
    public void CitationInfo_Chapter_IncludedInJson()
    {
        var citation = new CitationInfo
        {
            CitationId = "cite-1",
            DocId = "DOC-001",
            FileName = "test.md",
            Score = 0.9f,
            Chapter = "3. Zone 압력 설정"
        };

        var json = JsonSerializer.Serialize(citation);

        json.Should().Contain("3. Zone");
    }

    [Fact]
    public void Deserialized_CitationInfo_HasCorrectPropertyValues()
    {
        var jsonStr = """
        {
            "citationId": "cite-2",
            "docId": "MNL-2025",
            "fileName": "manual.pdf",
            "chunkText": "CMP process",
            "score": 0.95,
            "chapter": "Ch5",
            "section": "5.1",
            "lineRange": { "from": 100, "to": 120 }
        }
        """;

        var citation = JsonSerializer.Deserialize<CitationInfo>(jsonStr)!;

        citation.CitationId.Should().Be("cite-2");
        citation.DocId.Should().Be("MNL-2025");
        citation.FileName.Should().Be("manual.pdf");
        citation.ChunkText.Should().Be("CMP process");
        citation.Score.Should().BeApproximately(0.95f, 0.01f);
        citation.Chapter.Should().Be("Ch5");
        citation.Section.Should().Be("5.1");
        citation.LineRange.Should().NotBeNull();
        citation.LineRange!.From.Should().Be(100);
        citation.LineRange!.To.Should().Be(120);
    }

    [Fact]
    public void ChatStreamChunk_MultipleCitations_ArraySerialized()
    {
        var chunk = new ChatStreamChunk
        {
            ConversationId = "conv-1",
            Token = "",
            IsComplete = true,
            Citations =
            [
                new CitationInfo { CitationId = "cite-1", DocId = "D1", FileName = "a.md", Score = 0.9f },
                new CitationInfo { CitationId = "cite-2", DocId = "D2", FileName = "b.md", Score = 0.8f }
            ]
        };

        var json = JsonSerializer.Serialize(chunk);
        var deserialized = JsonSerializer.Deserialize<ChatStreamChunk>(json)!;

        deserialized.Citations.Should().HaveCount(2);
        deserialized.Citations![0].CitationId.Should().Be("cite-1");
        deserialized.Citations![1].CitationId.Should().Be("cite-2");
    }

    [Fact]
    public void ChatStreamChunk_EmptyToken_SerializedAsEmptyString()
    {
        var chunk = new ChatStreamChunk
        {
            ConversationId = "conv-1",
            Token = "",
            IsComplete = true
        };

        var json = JsonSerializer.Serialize(chunk);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("token").GetString().Should().BeEmpty();
    }
}
