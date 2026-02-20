using FabCopilot.VectorStore;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class QdrantFilterTests
{
    [Fact]
    public void BuildQdrantFilter_NullDictionary_ReturnsNull()
    {
        var result = QdrantVectorStore.BuildQdrantFilter(null);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildQdrantFilter_EmptyDictionary_ReturnsNull()
    {
        var filter = new Dictionary<string, object>();

        var result = QdrantVectorStore.BuildQdrantFilter(filter);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildQdrantFilter_SingleKey_CreatesMustCondition()
    {
        var filter = new Dictionary<string, object>
        {
            ["equipment_id"] = "CMP-001"
        };

        var result = QdrantVectorStore.BuildQdrantFilter(filter);

        result.Should().NotBeNull();
        result!.Must.Should().HaveCount(1);
        result.Must[0].Field.Key.Should().Be("equipment_id");
        result.Must[0].Field.Match.Keyword.Should().Be("CMP-001");
    }

    [Fact]
    public void BuildQdrantFilter_MultipleKeys_CreatesMultipleMustConditions()
    {
        var filter = new Dictionary<string, object>
        {
            ["equipment_id"] = "CMP-001",
            ["document_type"] = "manual"
        };

        var result = QdrantVectorStore.BuildQdrantFilter(filter);

        result.Should().NotBeNull();
        result!.Must.Should().HaveCount(2);

        var keys = result.Must.Select(c => c.Field.Key).ToList();
        keys.Should().Contain("equipment_id");
        keys.Should().Contain("document_type");
    }

    [Fact]
    public void BuildQdrantFilter_NullValue_UsesEmptyString()
    {
        var filter = new Dictionary<string, object>
        {
            ["key"] = null!
        };

        var result = QdrantVectorStore.BuildQdrantFilter(filter);

        result.Should().NotBeNull();
        result!.Must[0].Field.Match.Keyword.Should().Be("");
    }
}
