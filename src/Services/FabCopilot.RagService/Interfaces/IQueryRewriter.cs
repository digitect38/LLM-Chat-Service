namespace FabCopilot.RagService.Interfaces;

public interface IQueryRewriter
{
    Task<string> RewriteAsync(string query, CancellationToken ct);
}
