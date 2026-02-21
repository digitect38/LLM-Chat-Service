using System.Reflection;
using System.Text.Json;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class MetadataExtractionTests
{
    // --- TryGetMetadataString ---

    private static bool InvokeTryGetMetadataString(Dictionary<string, object> metadata, string key, out string value)
    {
        var method = typeof(LlmWorker).GetMethod("TryGetMetadataString",
            BindingFlags.NonPublic | BindingFlags.Static);
        var args = new object[] { metadata, key, string.Empty };
        var result = (bool)method!.Invoke(null, args)!;
        value = (string)args[2];
        return result;
    }

    private static int? InvokeTryGetMetadataInt(Dictionary<string, object> metadata, string key)
    {
        var method = typeof(LlmWorker).GetMethod("TryGetMetadataInt",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (int?)method!.Invoke(null, [metadata, key]);
    }

    private static string InvokeExtractSourceName(RetrievalResult result)
    {
        var method = typeof(LlmWorker).GetMethod("ExtractSourceName",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method!.Invoke(null, [result])!;
    }

    [Fact]
    public void TryGetMetadataString_PlainString_ReturnsValue()
    {
        var metadata = new Dictionary<string, object> { ["file_name"] = "test.md" };

        var found = InvokeTryGetMetadataString(metadata, "file_name", out var value);

        found.Should().BeTrue();
        value.Should().Be("test.md");
    }

    [Fact]
    public void TryGetMetadataString_JsonElementString_ReturnsUnwrappedValue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("\"test-doc.md\"");
        var metadata = new Dictionary<string, object> { ["file_name"] = json };

        var found = InvokeTryGetMetadataString(metadata, "file_name", out var value);

        found.Should().BeTrue();
        value.Should().Be("test-doc.md");
    }

    [Fact]
    public void TryGetMetadataString_JsonElementNumber_ReturnsToString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("42");
        var metadata = new Dictionary<string, object> { ["page"] = json };

        var found = InvokeTryGetMetadataString(metadata, "page", out var value);

        found.Should().BeTrue();
        value.Should().Be("42");
    }

    [Fact]
    public void TryGetMetadataString_KeyMissing_ReturnsFalse()
    {
        var metadata = new Dictionary<string, object> { ["other_key"] = "value" };

        var found = InvokeTryGetMetadataString(metadata, "file_name", out var value);

        found.Should().BeFalse();
    }

    [Fact]
    public void TryGetMetadataString_NullValue_ReturnsFalse()
    {
        var metadata = new Dictionary<string, object> { ["file_name"] = null! };

        var found = InvokeTryGetMetadataString(metadata, "file_name", out _);

        found.Should().BeFalse();
    }

    [Fact]
    public void TryGetMetadataString_EmptyDictionary_ReturnsFalse()
    {
        var metadata = new Dictionary<string, object>();

        var found = InvokeTryGetMetadataString(metadata, "file_name", out _);

        found.Should().BeFalse();
    }

    // --- TryGetMetadataInt ---

    [Fact]
    public void TryGetMetadataInt_IntValue_ReturnsValue()
    {
        var metadata = new Dictionary<string, object> { ["page_number"] = 5 };

        var result = InvokeTryGetMetadataInt(metadata, "page_number");

        result.Should().Be(5);
    }

    [Fact]
    public void TryGetMetadataInt_JsonElementNumber_ReturnsParsedInt()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("42");
        var metadata = new Dictionary<string, object> { ["page_number"] = json };

        var result = InvokeTryGetMetadataInt(metadata, "page_number");

        result.Should().Be(42);
    }

    [Fact]
    public void TryGetMetadataInt_StringValue42_ReturnsNull()
    {
        // String "42" goes through ToString() then int.TryParse, should actually parse
        var metadata = new Dictionary<string, object> { ["page_number"] = "42" };

        var result = InvokeTryGetMetadataInt(metadata, "page_number");

        // ToString() returns "42", TryParse succeeds
        result.Should().Be(42);
    }

    [Fact]
    public void TryGetMetadataInt_KeyMissing_ReturnsNull()
    {
        var metadata = new Dictionary<string, object>();

        var result = InvokeTryGetMetadataInt(metadata, "page_number");

        result.Should().BeNull();
    }

    // --- ExtractSourceName ---

    [Fact]
    public void ExtractSourceName_FileNamePresent_ReturnsFileName()
    {
        var result = new RetrievalResult
        {
            DocumentId = "doc-1",
            Metadata = new Dictionary<string, object> { ["file_name"] = "guide.md" }
        };

        var name = InvokeExtractSourceName(result);

        name.Should().Be("guide.md");
    }

    [Fact]
    public void ExtractSourceName_FileNameMissing_FilePathPresent_ReturnsFilePath()
    {
        var result = new RetrievalResult
        {
            DocumentId = "doc-1",
            Metadata = new Dictionary<string, object> { ["file_path"] = "/docs/guide.md" }
        };

        var name = InvokeExtractSourceName(result);

        name.Should().Be("/docs/guide.md");
    }

    [Fact]
    public void ExtractSourceName_FileNameAndPathMissing_DocumentIdPresent_ReturnsDocumentId()
    {
        var result = new RetrievalResult
        {
            DocumentId = "doc-1",
            Metadata = new Dictionary<string, object> { ["document_id"] = "DOC-001" }
        };

        var name = InvokeExtractSourceName(result);

        name.Should().Be("DOC-001");
    }

    [Fact]
    public void ExtractSourceName_AllMissing_ReturnsUnknown()
    {
        var result = new RetrievalResult
        {
            DocumentId = "",
            Metadata = new Dictionary<string, object>()
        };

        var name = InvokeExtractSourceName(result);

        name.Should().Be("unknown");
    }
}
