using System.Text.Json;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Models;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Redis.Interfaces;
using FabCopilot.VectorStore.Interfaces;

namespace FabCopilot.KnowledgeService.Services;

public sealed class KnowledgeManager
{
    private const string KnowledgeKeyPrefix = "knowledge:";
    private const string PendingSetKey = "knowledge:pending_review";
    private const string KnowledgeCollection = "knowledge";

    private readonly ISessionStore _sessionStore;
    private readonly ILlmClient _llmClient;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<KnowledgeManager> _logger;

    public KnowledgeManager(
        ISessionStore sessionStore,
        ILlmClient llmClient,
        IVectorStore vectorStore,
        ILogger<KnowledgeManager> logger)
    {
        _sessionStore = sessionStore;
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new knowledge draft and stores it in Redis.
    /// </summary>
    public async Task<KnowledgeObject> CreateDraftAsync(
        string type,
        string? equipment,
        string? symptom,
        string? rootCause,
        string? solution,
        CancellationToken ct = default)
    {
        var knowledge = new KnowledgeObject
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Equipment = equipment,
            Symptom = symptom,
            RootCause = rootCause,
            Solution = solution,
            Status = KnowledgeStatus.Draft,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sessionStore.SetAsync(
            $"{KnowledgeKeyPrefix}{knowledge.Id}",
            knowledge,
            TimeSpan.FromDays(30),
            ct);

        _logger.LogInformation(
            "Created knowledge draft. Id={KnowledgeId}, Type={Type}, Equipment={Equipment}",
            knowledge.Id, type, equipment);

        return knowledge;
    }

    /// <summary>
    /// Updates the status of a knowledge object (PendingReview, Approved, Rejected).
    /// When approved, adds the object to the pending-review index for tracking.
    /// </summary>
    public async Task<bool> UpdateStatusAsync(
        string knowledgeId,
        KnowledgeStatus newStatus,
        string? approvedBy = null,
        CancellationToken ct = default)
    {
        var key = $"{KnowledgeKeyPrefix}{knowledgeId}";
        var knowledge = await _sessionStore.GetAsync<KnowledgeObject>(key, ct);

        if (knowledge is null)
        {
            _logger.LogWarning("Knowledge object not found. Id={KnowledgeId}", knowledgeId);
            return false;
        }

        var previousStatus = knowledge.Status;
        knowledge.Status = newStatus;

        if (newStatus == KnowledgeStatus.Approved)
        {
            knowledge.ApprovedBy = approvedBy;
            knowledge.ApprovedAt = DateTimeOffset.UtcNow;
        }

        await _sessionStore.SetAsync(key, knowledge, TimeSpan.FromDays(90), ct);

        // Track pending review items separately
        if (newStatus == KnowledgeStatus.PendingReview)
        {
            var pendingList = await GetPendingListAsync(ct);
            if (!pendingList.Contains(knowledgeId))
            {
                pendingList.Add(knowledgeId);
                await _sessionStore.SetAsync(PendingSetKey, pendingList, ct: ct);
            }
        }
        else
        {
            // Remove from pending if it was there
            var pendingList = await GetPendingListAsync(ct);
            if (pendingList.Remove(knowledgeId))
            {
                await _sessionStore.SetAsync(PendingSetKey, pendingList, ct: ct);
            }
        }

        _logger.LogInformation(
            "Updated knowledge status. Id={KnowledgeId}, From={PreviousStatus}, To={NewStatus}",
            knowledgeId, previousStatus, newStatus);

        return true;
    }

    /// <summary>
    /// Embeds approved knowledge and upserts it to the Qdrant vector store.
    /// </summary>
    public async Task<bool> IndexApprovedAsync(string knowledgeId, CancellationToken ct = default)
    {
        var key = $"{KnowledgeKeyPrefix}{knowledgeId}";
        var knowledge = await _sessionStore.GetAsync<KnowledgeObject>(key, ct);

        if (knowledge is null)
        {
            _logger.LogWarning("Knowledge object not found for indexing. Id={KnowledgeId}", knowledgeId);
            return false;
        }

        if (knowledge.Status != KnowledgeStatus.Approved)
        {
            _logger.LogWarning(
                "Cannot index non-approved knowledge. Id={KnowledgeId}, Status={Status}",
                knowledgeId, knowledge.Status);
            return false;
        }

        // Build the text to embed
        var textToEmbed = BuildEmbeddingText(knowledge);

        // Generate embedding
        var embedding = await _llmClient.GetEmbeddingAsync(textToEmbed, isQuery: false, ct);

        // Build payload for the vector store
        var payload = new Dictionary<string, object>
        {
            ["id"] = knowledge.Id,
            ["type"] = knowledge.Type,
            ["equipment"] = knowledge.Equipment ?? string.Empty,
            ["symptom"] = knowledge.Symptom ?? string.Empty,
            ["rootCause"] = knowledge.RootCause ?? string.Empty,
            ["solution"] = knowledge.Solution ?? string.Empty,
            ["status"] = knowledge.Status.ToString(),
            ["version"] = knowledge.Version,
            ["createdAt"] = knowledge.CreatedAt.ToString("O")
        };

        // Upsert to Qdrant
        await _vectorStore.UpsertAsync(KnowledgeCollection, knowledge.Id, embedding, payload, ct);

        _logger.LogInformation(
            "Indexed approved knowledge to vector store. Id={KnowledgeId}, Collection={Collection}",
            knowledgeId, KnowledgeCollection);

        return true;
    }

    /// <summary>
    /// Lists all knowledge objects that are pending review.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeObject>> ListPendingReviewAsync(CancellationToken ct = default)
    {
        var pendingIds = await GetPendingListAsync(ct);
        var results = new List<KnowledgeObject>();

        foreach (var id in pendingIds)
        {
            var knowledge = await _sessionStore.GetAsync<KnowledgeObject>(
                $"{KnowledgeKeyPrefix}{id}", ct);

            if (knowledge is not null && knowledge.Status == KnowledgeStatus.PendingReview)
            {
                results.Add(knowledge);
            }
        }

        return results;
    }

    private async Task<List<string>> GetPendingListAsync(CancellationToken ct)
    {
        var list = await _sessionStore.GetAsync<List<string>>(PendingSetKey, ct);
        return list ?? [];
    }

    private static string BuildEmbeddingText(KnowledgeObject knowledge)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(knowledge.Equipment))
            parts.Add($"Equipment: {knowledge.Equipment}");

        if (!string.IsNullOrWhiteSpace(knowledge.Symptom))
            parts.Add($"Symptom: {knowledge.Symptom}");

        if (!string.IsNullOrWhiteSpace(knowledge.RootCause))
            parts.Add($"Root Cause: {knowledge.RootCause}");

        if (!string.IsNullOrWhiteSpace(knowledge.Solution))
            parts.Add($"Solution: {knowledge.Solution}");

        return string.Join(". ", parts);
    }
}
