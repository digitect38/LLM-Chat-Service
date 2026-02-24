using FabCopilot.Contracts.Configuration;
using FabCopilot.Contracts.Models;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Services;
using FabCopilot.Redis.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class RedisKnowledgeGraphStoreTests
{
    private readonly Mock<ISessionStore> _sessionStore;
    private readonly RedisKnowledgeGraphStore _store;

    public RedisKnowledgeGraphStoreTests()
    {
        _sessionStore = new Mock<ISessionStore>();
        var ragOptions = Options.Create(new RagOptions { GraphMaxDepth = 2 });
        _store = new RedisKnowledgeGraphStore(
            _sessionStore.Object,
            ragOptions,
            NullLogger<RedisKnowledgeGraphStore>.Instance);
    }

    [Fact]
    public async Task UpsertRelationAsync_StoresForwardAndReverseIndex()
    {
        // Arrange
        var relation = new GraphRelation
        {
            SourceId = "CMP",
            TargetId = "polishing pad",
            RelationType = "HasComponent"
        };

        _sessionStore.Setup(x => x.GetAsync<List<string>>(It.Is<string>(k => k.StartsWith("graph:src:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
        _sessionStore.Setup(x => x.GetAsync<List<string>>(It.Is<string>(k => k.StartsWith("graph:tgt:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
        _sessionStore.Setup(x => x.GetAsync<HashSet<string>>(It.Is<string>(k => k == "graph:all_relations"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Act
        await _store.UpsertRelationAsync(relation, CancellationToken.None);

        // Assert - forward index stored
        _sessionStore.Verify(x => x.SetAsync(
            "graph:src:cmp",
            It.Is<List<string>>(l => l.Count == 1),
            null, It.IsAny<CancellationToken>()), Times.Once);

        // Assert - reverse index stored
        _sessionStore.Verify(x => x.SetAsync(
            "graph:tgt:polishing pad",
            It.Is<List<string>>(l => l.Count == 1),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRelatedEntitiesAsync_BidirectionalTraversal_FindsSourceFromTarget()
    {
        // Arrange: CMP -[HasComponent]-> polishing pad
        // Starting from "polishing pad" should find "CMP" via reverse traversal
        var cmpEntity = new GraphEntity { Id = "1", Name = "CMP", Type = "Equipment" };
        var padEntity = new GraphEntity { Id = "2", Name = "polishing pad", Type = "Component" };

        var relationKey = "graph:rel:cmp:hascomponent:polishing pad";
        var relation = new GraphRelation
        {
            SourceId = "CMP",
            TargetId = "polishing pad",
            RelationType = "HasComponent"
        };

        // Entity lookups
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:polishing pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(padEntity);
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmpEntity);

        // Forward: polishing pad has no outgoing relations
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:polishing pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Reverse: polishing pad is target of CMP relation
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:polishing pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { relationKey });

        _sessionStore.Setup(x => x.GetAsync<GraphRelation>(relationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relation);

        // CMP has no further relations (stops here since depth 1 reached with maxDepth=1)
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var results = await _store.GetRelatedEntitiesAsync("polishing pad", 1, CancellationToken.None);

        // Assert - should find both entities via reverse traversal
        results.Should().HaveCount(2);
        results.Should().Contain(e => e.Name == "polishing pad");
        results.Should().Contain(e => e.Name == "CMP");
    }

    [Fact]
    public async Task GetRelatedEntitiesAsync_CyclePrevention_DoesNotInfiniteLoop()
    {
        // Arrange: A → B → A (cycle)
        var entityA = new GraphEntity { Id = "1", Name = "A", Type = "Equipment" };
        var entityB = new GraphEntity { Id = "2", Name = "B", Type = "Component" };

        var relAB = new GraphRelation { SourceId = "A", TargetId = "B", RelationType = "RelatesTo" };
        var relBA = new GraphRelation { SourceId = "B", TargetId = "A", RelationType = "RelatesTo" };

        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entityA);
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entityB);

        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "graph:rel:a:relatesto:b" });
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "graph:rel:b:relatesto:a" });

        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "graph:rel:b:relatesto:a" });
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "graph:rel:a:relatesto:b" });

        _sessionStore.Setup(x => x.GetAsync<GraphRelation>("graph:rel:a:relatesto:b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relAB);
        _sessionStore.Setup(x => x.GetAsync<GraphRelation>("graph:rel:b:relatesto:a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relBA);

        // Act - should not infinite loop
        var results = await _store.GetRelatedEntitiesAsync("A", 5, CancellationToken.None);

        // Assert - should find both entities exactly once
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRelatedRelationsAsync_CollectsRelationsFromBothDirections()
    {
        // Arrange: CMP -[HasComponent]-> pad, scratch -[CausedBy]-> CMP
        var relOut = new GraphRelation { SourceId = "CMP", TargetId = "pad", RelationType = "HasComponent" };
        var relIn = new GraphRelation { SourceId = "scratch", TargetId = "CMP", RelationType = "CausedBy" };

        var outKey = "graph:rel:cmp:hascomponent:pad";
        var inKey = "graph:rel:scratch:causedby:cmp";

        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { outKey });
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { inKey });

        _sessionStore.Setup(x => x.GetAsync<GraphRelation>(outKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relOut);
        _sessionStore.Setup(x => x.GetAsync<GraphRelation>(inKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relIn);

        // Neighbors have no further relations
        _sessionStore.Setup(x => x.GetAsync<List<string>>(It.Is<string>(k => k == "graph:src:pad" || k == "graph:src:scratch"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
        _sessionStore.Setup(x => x.GetAsync<List<string>>(It.Is<string>(k => k == "graph:tgt:pad" || k == "graph:tgt:scratch"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var relations = await _store.GetRelatedRelationsAsync("CMP", 1, CancellationToken.None);

        // Assert
        relations.Should().HaveCount(2);
        relations.Should().Contain(r => r.RelationType == "HasComponent");
        relations.Should().Contain(r => r.RelationType == "CausedBy");
    }

    [Fact]
    public async Task BuildGraphContextAsync_IncludesEntitiesAndRelations()
    {
        // Arrange
        var entity = new GraphEntity { Id = "1", Name = "CMP", Type = "Equipment" };
        var relation = new GraphRelation { SourceId = "CMP", TargetId = "pad", RelationType = "HasComponent" };

        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GraphEntity?)null);

        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "graph:rel:cmp:hascomponent:pad" });
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:src:pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
        _sessionStore.Setup(x => x.GetAsync<List<string>>("graph:tgt:pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        _sessionStore.Setup(x => x.GetAsync<GraphRelation>("graph:rel:cmp:hascomponent:pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relation);

        // Act
        var context = await _store.BuildGraphContextAsync("CMP question", new List<string> { "cmp" }, CancellationToken.None);

        // Assert
        context.Should().Contain("[관련 지식 그래프]");
        context.Should().Contain("엔티티:");
        context.Should().Contain("CMP [Equipment]");
        context.Should().Contain("관계:");
        context.Should().Contain("CMP -[HasComponent]-> pad");
    }

    [Fact]
    public async Task BuildGraphContextAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange - no entity found
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GraphEntity?)null);
        _sessionStore.Setup(x => x.GetAsync<List<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var context = await _store.BuildGraphContextAsync("unknown", new List<string> { "unknown" }, CancellationToken.None);

        // Assert
        context.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        _sessionStore.Setup(x => x.GetAsync<HashSet<string>>("graph:all_entities", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "cmp", "pad", "scratch" });
        _sessionStore.Setup(x => x.GetAsync<HashSet<string>>("graph:all_relations", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "rel1", "rel2" });

        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphEntity { Id = "1", Name = "CMP", Type = "Equipment" });
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:pad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphEntity { Id = "2", Name = "pad", Type = "Component" });
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:scratch", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphEntity { Id = "3", Name = "scratch", Type = "Symptom" });

        // Act
        var stats = await _store.GetStatsAsync(CancellationToken.None);

        // Assert
        stats.EntityCount.Should().Be(3);
        stats.RelationCount.Should().Be(2);
        stats.EntitiesByType.Should().ContainKey("equipment").WhoseValue.Should().Be(1);
        stats.EntitiesByType.Should().ContainKey("component").WhoseValue.Should().Be(1);
        stats.EntitiesByType.Should().ContainKey("symptom").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_EmptyGraph_ReturnsZeros()
    {
        // Arrange
        _sessionStore.Setup(x => x.GetAsync<HashSet<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Act
        var stats = await _store.GetStatsAsync(CancellationToken.None);

        // Assert
        stats.EntityCount.Should().Be(0);
        stats.RelationCount.Should().Be(0);
        stats.EntitiesByType.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEntityAsync_RemovesFromAllIndexes()
    {
        // Arrange
        var entity = new GraphEntity { Id = "1", Name = "CMP", Type = "Equipment" };
        _sessionStore.Setup(x => x.GetAsync<GraphEntity>("graph:entity:cmp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _sessionStore.Setup(x => x.GetAsync<HashSet<string>>("graph:idx:equipment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "cmp" });
        _sessionStore.Setup(x => x.GetAsync<HashSet<string>>("graph:all_entities", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "cmp" });

        // Act
        await _store.DeleteEntityAsync("CMP", CancellationToken.None);

        // Assert
        _sessionStore.Verify(x => x.DeleteAsync("graph:entity:cmp", It.IsAny<CancellationToken>()), Times.Once);
    }
}
