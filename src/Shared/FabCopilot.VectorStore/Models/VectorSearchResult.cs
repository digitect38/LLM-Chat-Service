namespace FabCopilot.VectorStore.Models;

public sealed record VectorSearchResult(string Id, float Score, Dictionary<string, object> Payload);
