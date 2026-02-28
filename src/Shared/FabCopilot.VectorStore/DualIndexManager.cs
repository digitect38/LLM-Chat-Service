using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.VectorStore;

/// <summary>
/// Manages Active/Standby Qdrant collections for zero-downtime embedding model transitions.
/// Naming convention: {baseCollection}_{modelId} (e.g., "knowledge_bge-m3", "knowledge_arctic-embed2")
/// </summary>
public sealed class DualIndexManager
{
    private readonly IVectorStore _vectorStore;
    private readonly IOptionsMonitor<QdrantOptions> _qdrantOptions;
    private readonly ILogger<DualIndexManager> _logger;
    private volatile string _activeModelId;
    private volatile string? _standbyModelId;

    public DualIndexManager(
        IVectorStore vectorStore,
        IOptionsMonitor<QdrantOptions> qdrantOptions,
        ILogger<DualIndexManager> logger)
    {
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions;
        _logger = logger;
        _activeModelId = "default";
    }

    public string ActiveCollection => BuildCollectionName(_activeModelId);
    public string? StandbyCollection => _standbyModelId != null ? BuildCollectionName(_standbyModelId) : null;
    public string ActiveModelId => _activeModelId;
    public string? StandbyModelId => _standbyModelId;

    /// <summary>
    /// Initializes with the currently active model ID.
    /// Call this at startup to set the active collection.
    /// </summary>
    public void Initialize(string modelId)
    {
        _activeModelId = SanitizeModelId(modelId);
        _logger.LogInformation("DualIndexManager initialized. Active collection: {Collection}", ActiveCollection);
    }

    /// <summary>
    /// Prepares a standby collection for re-indexing with a new embedding model.
    /// The standby collection will be created if it doesn't exist.
    /// </summary>
    public async Task PrepareStandbyAsync(string newModelId, int vectorSize, CancellationToken ct = default)
    {
        var sanitizedId = SanitizeModelId(newModelId);
        _standbyModelId = sanitizedId;
        var collectionName = BuildCollectionName(sanitizedId);

        _logger.LogInformation(
            "Preparing standby collection {Collection} for model {ModelId} (vectorSize={VectorSize})",
            collectionName, newModelId, vectorSize);

        await _vectorStore.EnsureCollectionAsync(collectionName, vectorSize, ct);
    }

    /// <summary>
    /// Promotes the standby collection to active after evaluation tests pass.
    /// The previous active collection is retained for rollback.
    /// </summary>
    public PromotionResult PromoteStandby()
    {
        if (_standbyModelId is null)
        {
            _logger.LogWarning("No standby collection to promote");
            return new PromotionResult(false, "No standby collection prepared");
        }

        var previousActive = _activeModelId;
        var previousCollection = ActiveCollection;

        _activeModelId = _standbyModelId;
        _standbyModelId = null;

        _logger.LogInformation(
            "Promoted standby to active. Active: {Active} (was: {Previous}). Previous collection {PreviousCollection} retained for rollback.",
            ActiveCollection, previousCollection, previousCollection);

        return new PromotionResult(true, $"Promoted {ActiveCollection}", previousActive, previousCollection);
    }

    /// <summary>
    /// Rolls back to the previous active collection.
    /// </summary>
    public bool Rollback(string previousModelId)
    {
        var currentActive = _activeModelId;
        _activeModelId = SanitizeModelId(previousModelId);

        _logger.LogWarning(
            "Rolling back active collection. Active: {Active} (was: {Previous})",
            ActiveCollection, BuildCollectionName(currentActive));

        return true;
    }

    /// <summary>
    /// Gets the collection name for search queries (always the active collection).
    /// </summary>
    public string GetSearchCollection()
    {
        return ActiveCollection;
    }

    /// <summary>
    /// Gets the collection name for indexing.
    /// If a standby is being prepared, indexes into the standby; otherwise into active.
    /// </summary>
    public string GetIndexCollection()
    {
        return _standbyModelId != null
            ? BuildCollectionName(_standbyModelId)
            : ActiveCollection;
    }

    private string BuildCollectionName(string modelId)
    {
        var baseCollection = _qdrantOptions.CurrentValue.DefaultCollection;
        return modelId == "default"
            ? baseCollection
            : $"{baseCollection}_{modelId}";
    }

    private static string SanitizeModelId(string modelId)
    {
        // Replace characters that are invalid in Qdrant collection names
        return modelId
            .Replace('/', '_')
            .Replace(':', '_')
            .Replace('.', '-')
            .ToLowerInvariant();
    }

    public record PromotionResult(
        bool Success,
        string Message,
        string? PreviousModelId = null,
        string? PreviousCollection = null);
}
