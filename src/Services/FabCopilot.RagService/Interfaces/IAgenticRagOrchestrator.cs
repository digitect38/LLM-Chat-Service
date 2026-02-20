using FabCopilot.Contracts.Messages;

namespace FabCopilot.RagService.Interfaces;

public interface IAgenticRagOrchestrator
{
    Task<(List<RetrievalResult> Results, int IterationCount)> OrchestrateAsync(
        RagRequest request, CancellationToken ct);
}
