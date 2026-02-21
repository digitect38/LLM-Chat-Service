using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class RagCacheTests
{
    [Fact]
    public void BuildKey_SameInputs_ProduceSameKey()
    {
        var key1 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 5);
        var key2 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 5);

        key1.Should().Be(key2);
    }

    [Fact]
    public void BuildKey_DifferentQuery_ProducesDifferentKey()
    {
        var key1 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 5);
        var key2 = RedisRagCache.BuildKey("슬러리 교체 주기", "CMP-001", "Naive", 5);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentEquipmentId_ProducesDifferentKey()
    {
        var key1 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 5);
        var key2 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-002", "Naive", 5);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentPipeline_ProducesDifferentKey()
    {
        var key1 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 5);
        var key2 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Advanced", 5);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentTopK_ProducesDifferentKey()
    {
        var key1 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 3);
        var key2 = RedisRagCache.BuildKey("패드 교체 방법", "CMP-001", "Naive", 5);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_HasExpectedPrefix()
    {
        var key = RedisRagCache.BuildKey("test", "equip", "Naive", 5);

        key.Should().StartWith("fab:ragcache:");
    }

    [Fact]
    public void BuildKey_HasConsistentLength()
    {
        // SHA256 hex truncated to 16 chars + prefix
        var key = RedisRagCache.BuildKey("test", "equip", "Naive", 5);

        // "fab:ragcache:" (13 chars) + 16 hex chars = 29
        key.Length.Should().Be(29);
    }
}
