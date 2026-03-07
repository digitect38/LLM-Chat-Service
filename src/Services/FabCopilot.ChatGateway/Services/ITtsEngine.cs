using FabCopilot.ChatGateway.Configuration;

namespace FabCopilot.ChatGateway.Services;

public interface ITtsEngine
{
    string Name { get; }
    Task<TtsResult> SynthesizeAsync(string text, string voice, TtsOptions options, CancellationToken ct = default);
}

public record TtsResult(byte[] AudioData, string ContentType, string? Error = null)
{
    public bool IsSuccess => Error is null;
    public static TtsResult Fail(string error) => new([], "audio/wav", error);
}
