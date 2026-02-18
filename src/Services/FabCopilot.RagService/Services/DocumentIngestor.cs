using FabCopilot.Llm.Interfaces;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using Microsoft.Extensions.Options;

namespace FabCopilot.RagService.Services;

public sealed class DocumentIngestor
{
    private const int ChunkSize = 512;
    private const int ChunkOverlap = 128;

    private readonly ILlmClient _llmClient;
    private readonly IVectorStore _vectorStore;
    private readonly QdrantOptions _qdrantOptions;
    private readonly ILogger<DocumentIngestor> _logger;

    public DocumentIngestor(
        ILlmClient llmClient,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        ILogger<DocumentIngestor> logger)
    {
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ingests a text document into the vector store by chunking, embedding, and upserting.
    /// </summary>
    public async Task IngestTextAsync(
        string documentId,
        string text,
        string equipmentId,
        Dictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty text provided for document {DocumentId}, skipping ingestion", documentId);
            return;
        }

        var collection = _qdrantOptions.DefaultCollection;
        var vectorSize = _qdrantOptions.VectorSize;

        // Ensure the collection exists
        await _vectorStore.EnsureCollectionAsync(collection, vectorSize, ct);

        // Chunk the text into overlapping segments
        var chunks = ChunkText(text, ChunkSize, ChunkOverlap);

        _logger.LogInformation(
            "Ingesting document {DocumentId} for equipment {EquipmentId}. TextLength={TextLength}, ChunkCount={ChunkCount}",
            documentId, equipmentId, text.Length, chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var chunkId = $"{documentId}:chunk:{i}";

            // Embed the chunk
            var vector = await _llmClient.GetEmbeddingAsync(chunk, ct);

            // Build payload with metadata
            var payload = new Dictionary<string, object>
            {
                ["text"] = chunk,
                ["document_id"] = documentId,
                ["equipment_id"] = equipmentId,
                ["chunk_index"] = i,
                ["chunk_count"] = chunks.Count
            };

            // Merge user-supplied metadata
            foreach (var kvp in metadata)
            {
                payload[kvp.Key] = kvp.Value;
            }

            // Upsert into the vector store
            await _vectorStore.UpsertAsync(collection, chunkId, vector, payload, ct);

            _logger.LogDebug(
                "Upserted chunk {ChunkIndex}/{ChunkCount} for document {DocumentId}",
                i + 1, chunks.Count, documentId);
        }

        _logger.LogInformation(
            "Completed ingestion of document {DocumentId}. Chunks upserted: {ChunkCount}",
            documentId, chunks.Count);
    }

    /// <summary>
    /// Splits text into overlapping chunks of the specified size.
    /// </summary>
    internal static List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();

        if (string.IsNullOrEmpty(text))
            return chunks;

        if (text.Length <= chunkSize)
        {
            chunks.Add(text);
            return chunks;
        }

        var step = chunkSize - overlap;
        if (step <= 0) step = 1;

        for (var offset = 0; offset < text.Length; offset += step)
        {
            var length = Math.Min(chunkSize, text.Length - offset);
            chunks.Add(text.Substring(offset, length));

            // If this chunk reaches the end, stop
            if (offset + length >= text.Length)
                break;
        }

        return chunks;
    }
}
