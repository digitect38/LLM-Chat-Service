using FabCopilot.Integration.Tests.Infrastructure;
using FluentAssertions;

namespace FabCopilot.Integration.Tests.Tests;

[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
public class BasicChatTests
{
    private readonly FabCopilotServiceFixture _fixture;

    public BasicChatTests(FabCopilotServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Chat_ReturnsNonEmptyResponse()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 장비란 무엇인가요?");

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace(
            "the LLM should return a non-empty answer");
        response.FullText.Length.Should().BeGreaterThan(10,
            "a meaningful response should have at least 10 characters");
    }

    [SkippableFact]
    public async Task Chat_PreservesConversationId()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var conversationId = Guid.NewGuid().ToString();

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 패드 교체 주기를 알려주세요.",
            conversationId: conversationId);

        response.Error.Should().BeNull();
        response.ConversationId.Should().NotBeNullOrWhiteSpace(
            "server should return a conversationId");
        response.ConversationId.Should().Be(conversationId,
            "server should preserve the client-provided conversationId");
    }
}
