using System.Text.RegularExpressions;
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

        // Use markdown-aware chunking for .md files, plain chunking otherwise
        var isMarkdown = documentId.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        var chunks = isMarkdown
            ? ChunkMarkdown(text, ChunkSize, ChunkOverlap)
            : ChunkText(text, ChunkSize, ChunkOverlap);

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
    /// Splits text into overlapping chunks of the specified size,
    /// respecting sentence boundaries where possible.
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

        var offset = 0;
        while (offset < text.Length)
        {
            var remaining = text.Length - offset;
            if (remaining <= chunkSize)
            {
                chunks.Add(text.Substring(offset));
                break;
            }

            // Take up to chunkSize characters
            var candidate = text.Substring(offset, chunkSize);

            // Find the best sentence boundary to split at
            var splitAt = FindLastSentenceBoundary(candidate);
            var chunk = text.Substring(offset, splitAt);
            chunks.Add(chunk);

            // Advance with overlap: back up by overlap amount from the split point
            var advance = splitAt - overlap;
            if (advance <= 0) advance = 1;
            offset += advance;
        }

        return chunks;
    }

    /// <summary>
    /// Finds the last sentence boundary in the text, searching backward from the end.
    /// Looks for sentence-ending punctuation (. ! ? or newline) after the 50% mark.
    /// Protects decimal numbers (e.g. 3.14) from being treated as sentence boundaries.
    /// Falls back to the last whitespace, then to the full length.
    /// </summary>
    internal static int FindLastSentenceBoundary(string text)
    {
        var minPos = text.Length / 2;
        var lastBoundary = -1;

        for (var i = text.Length - 1; i >= minPos; i--)
        {
            var ch = text[i];
            if (ch == '\n')
            {
                lastBoundary = i + 1;
                break;
            }

            if (ch is '.' or '!' or '?')
            {
                // Protect decimal numbers: digit.digit is not a sentence end
                if (ch == '.' && i > 0 && i < text.Length - 1
                    && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
                {
                    continue;
                }

                lastBoundary = i + 1;
                break;
            }
        }

        if (lastBoundary > 0)
            return lastBoundary;

        // Fallback: find last whitespace after 50% mark
        for (var i = text.Length - 1; i >= minPos; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i + 1;
        }

        // Final fallback: use full length
        return text.Length;
    }

    /// <summary>
    /// Splits markdown text into chunks, preserving section header hierarchy.
    /// Each chunk is prefixed with its parent section headers for context.
    /// </summary>
    internal static List<string> ChunkMarkdown(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var sections = SplitMarkdownSections(text);
        var chunks = new List<string>();

        foreach (var (headerPath, body) in sections)
        {
            var prefix = string.IsNullOrEmpty(headerPath) ? "" : $"[{headerPath}]\n";
            var prefixLen = prefix.Length;

            // Effective chunk size after accounting for the header prefix
            var effectiveChunkSize = chunkSize - prefixLen;
            if (effectiveChunkSize < 64) effectiveChunkSize = 64;

            var trimmedBody = body.Trim();
            if (string.IsNullOrEmpty(trimmedBody))
                continue;

            if (trimmedBody.Length <= effectiveChunkSize)
            {
                chunks.Add(prefix + trimmedBody);
            }
            else
            {
                // Sub-chunk the section body using existing ChunkText
                var subChunks = ChunkText(trimmedBody, effectiveChunkSize, overlap);
                foreach (var sub in subChunks)
                {
                    chunks.Add(prefix + sub);
                }
            }
        }

        return chunks;
    }

    /// <summary>
    /// Parses markdown into sections, tracking the header hierarchy.
    /// Returns (headerPath, bodyText) tuples where headerPath is like
    /// "## 1. 패드 교체 절차 > ### 1.1 교체 판단 기준".
    /// </summary>
    internal static List<(string HeaderPath, string Body)> SplitMarkdownSections(string text)
    {
        var headerRegex = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);
        var matches = headerRegex.Matches(text);

        var sections = new List<(string HeaderPath, string Body)>();

        // Track current header hierarchy by level
        var headerStack = new Dictionary<int, string>();

        // If there's content before the first header, add it as a section with no header
        var firstMatchStart = matches.Count > 0 ? matches[0].Index : text.Length;
        if (firstMatchStart > 0)
        {
            var preamble = text[..firstMatchStart];
            if (!string.IsNullOrWhiteSpace(preamble))
                sections.Add(("", preamble));
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var level = match.Groups[1].Value.Length; // number of # characters
            var headerLine = match.Value.Trim();

            // Update header stack: set current level, remove deeper levels
            headerStack[level] = headerLine;
            foreach (var key in headerStack.Keys.Where(k => k > level).ToList())
                headerStack.Remove(key);

            // Build the full header path
            var headerPath = string.Join(" > ", headerStack
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value));

            // Extract body text (from end of this header line to start of next header or end of text)
            var bodyStart = match.Index + match.Length;
            var bodyEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : text.Length;
            var body = text[bodyStart..bodyEnd];

            sections.Add((headerPath, body));
        }

        return sections;
    }
}
