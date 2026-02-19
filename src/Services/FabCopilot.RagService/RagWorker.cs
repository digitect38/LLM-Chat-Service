using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Messages;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.RagService.Configuration;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using FabCopilot.VectorStore.Models;
using Microsoft.Extensions.Options;

namespace FabCopilot.RagService;

public sealed class RagWorker : BackgroundService
{
    private const string QueueGroup = "rag-workers";

    private readonly IMessageBus _messageBus;
    private readonly ILlmClient _llmClient;
    private readonly IVectorStore _vectorStore;
    private readonly QdrantOptions _qdrantOptions;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RagWorker> _logger;

    public RagWorker(
        IMessageBus messageBus,
        ILlmClient llmClient,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<RagWorker> logger)
    {
        _messageBus = messageBus;
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RagWorker started. Subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.RagRequest, QueueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<RagRequest>(
                           NatsSubjects.RagRequest, QueueGroup, stoppingToken))
        {
            var request = envelope.Payload;
            if (request is null)
            {
                _logger.LogWarning(
                    "Received envelope with null payload, skipping. TraceId={TraceId}",
                    envelope.TraceId);
                continue;
            }

            _logger.LogInformation(
                "Processing RAG request. Query={Query}, EquipmentId={EquipmentId}, TopK={TopK}, TraceId={TraceId}",
                request.Query, request.EquipmentId, request.TopK, envelope.TraceId);

            _ = ProcessRagRequestAsync(request, stoppingToken);
        }

        _logger.LogInformation("RagWorker stopped");
    }

    private async Task ProcessRagRequestAsync(RagRequest request, CancellationToken ct)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
        var responseSubject = NatsSubjects.RagResponse(conversationId);

        try
        {
            // 1. Embed the query text
            _logger.LogDebug("Generating embedding for query. ConversationId={ConversationId}", conversationId);
            var queryVector = await _llmClient.GetEmbeddingAsync(request.Query, ct);

            // 2. Search the vector store (no equipment filter — shared + equipment-specific docs are both included,
            //    cosine similarity + MinScore filtering handles relevance)
            var collection = _qdrantOptions.DefaultCollection;
            var searchResults = await _vectorStore.SearchAsync(
                collection, queryVector, request.TopK, filter: null, ct);

            _logger.LogInformation(
                "Vector search completed. ConversationId={ConversationId}, ResultCount={ResultCount}",
                conversationId, searchResults.Count);

            // 3.5 Filter results by minimum score
            var filtered = FilterByScore(searchResults, _ragOptions.MinScore);

            _logger.LogInformation(
                "Score filtering applied. ConversationId={ConversationId}, Before={Before}, After={After}, MinScore={MinScore}",
                conversationId, searchResults.Count, filtered.Count, _ragOptions.MinScore);

            // 4. Map results to response
            var retrievalResults = filtered.Select(r => new RetrievalResult
            {
                DocumentId = r.Id,
                ChunkText = r.Payload.TryGetValue("text", out var text) ? text.ToString() ?? string.Empty : string.Empty,
                Score = r.Score,
                Metadata = new Dictionary<string, object>(r.Payload)
            }).ToList();

            var response = new RagResponse
            {
                ConversationId = conversationId,
                Results = retrievalResults
            };

            // 5. Publish response
            await _messageBus.PublishAsync(
                responseSubject,
                MessageEnvelope<RagResponse>.Create("rag.response", response, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published RAG response. ConversationId={ConversationId}, Subject={Subject}, ResultCount={ResultCount}",
                conversationId, responseSubject, retrievalResults.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "RAG request cancelled. ConversationId={ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing RAG request. ConversationId={ConversationId}", conversationId);

            // Publish an empty response so callers are not left waiting
            try
            {
                var errorResponse = new RagResponse
                {
                    ConversationId = conversationId,
                    Results = []
                };

                await _messageBus.PublishAsync(
                    responseSubject,
                    MessageEnvelope<RagResponse>.Create("rag.response.error", errorResponse, request.EquipmentId),
                    ct);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx,
                    "Failed to publish error response. ConversationId={ConversationId}", conversationId);
            }
        }
    }

    internal static List<VectorSearchResult> FilterByScore(
        IReadOnlyList<VectorSearchResult> results, float minScore)
        => results.Where(r => r.Score >= minScore).ToList();
}
