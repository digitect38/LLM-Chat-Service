using FabCopilot.Contracts.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class GraphStatsTests
{
    [Fact]
    public void GraphStats_DefaultValues_AreZero()
    {
        var stats = new GraphStats();

        stats.EntityCount.Should().Be(0);
        stats.RelationCount.Should().Be(0);
        stats.EntitiesByType.Should().BeEmpty();
    }

    [Fact]
    public void GraphStats_WithValues_ReturnsCorrectly()
    {
        var stats = new GraphStats
        {
            EntityCount = 10,
            RelationCount = 5,
            EntitiesByType = new Dictionary<string, int>
            {
                ["equipment"] = 3,
                ["component"] = 4,
                ["symptom"] = 3
            }
        };

        stats.EntityCount.Should().Be(10);
        stats.RelationCount.Should().Be(5);
        stats.EntitiesByType.Should().HaveCount(3);
        stats.EntitiesByType["equipment"].Should().Be(3);
    }

    [Fact]
    public void GraphStats_EntitiesByType_SumsToEntityCount()
    {
        var stats = new GraphStats
        {
            EntityCount = 7,
            EntitiesByType = new Dictionary<string, int>
            {
                ["equipment"] = 2,
                ["component"] = 3,
                ["process"] = 2
            }
        };

        stats.EntitiesByType.Values.Sum().Should().Be(stats.EntityCount);
    }

    [Fact]
    public void GraphEntity_DefaultProperties_IsEmptyDictionary()
    {
        var entity = new GraphEntity();

        entity.Properties.Should().NotBeNull();
        entity.Properties.Should().BeEmpty();
    }

    [Fact]
    public void GraphRelation_DefaultProperties_IsEmptyDictionary()
    {
        var relation = new GraphRelation();

        relation.Properties.Should().NotBeNull();
        relation.Properties.Should().BeEmpty();
    }

    [Fact]
    public void GraphEntity_WithProperties_CanBeSetAndRead()
    {
        var entity = new GraphEntity
        {
            Id = "test-id",
            Name = "CMP",
            Type = "Equipment",
            Properties = new Dictionary<string, string>
            {
                ["manufacturer"] = "Applied Materials",
                ["model"] = "Mirra"
            }
        };

        entity.Name.Should().Be("CMP");
        entity.Type.Should().Be("Equipment");
        entity.Properties.Should().HaveCount(2);
        entity.Properties["manufacturer"].Should().Be("Applied Materials");
    }

    [Fact]
    public void GraphRelation_FullyPopulated_HasAllFields()
    {
        var relation = new GraphRelation
        {
            SourceId = "CMP",
            TargetId = "polishing pad",
            RelationType = "HasComponent",
            Properties = new Dictionary<string, string>
            {
                ["confidence"] = "0.95"
            }
        };

        relation.SourceId.Should().Be("CMP");
        relation.TargetId.Should().Be("polishing pad");
        relation.RelationType.Should().Be("HasComponent");
        relation.Properties["confidence"].Should().Be("0.95");
    }
}
