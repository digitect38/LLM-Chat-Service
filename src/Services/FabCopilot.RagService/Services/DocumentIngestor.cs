using System.Text.RegularExpressions;
using FabCopilot.Llm.Interfaces;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using FabCopilot.RagService.Services.Bm25;
using FabCopilot.RagService.Services.ImageOcr;
using FabCopilot.VectorStore;
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
    private readonly RagOptions _ragOptions;
    private readonly IBm25Index? _bm25Index;
    private readonly IEntityExtractor? _entityExtractor;
    private readonly IKnowledgeGraphStore? _graphStore;
    private readonly DualIndexManager? _dualIndexManager;
    private readonly ILogger<DocumentIngestor> _logger;

    public DocumentIngestor(
        ILlmClient llmClient,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<DocumentIngestor> logger,
        IBm25Index? bm25Index = null,
        IEntityExtractor? entityExtractor = null,
        IKnowledgeGraphStore? graphStore = null,
        DualIndexManager? dualIndexManager = null)
    {
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _bm25Index = bm25Index;
        _entityExtractor = entityExtractor;
        _graphStore = graphStore;
        _dualIndexManager = dualIndexManager;
        _logger = logger;
    }

    private string GetTargetCollection()
    {
        return _dualIndexManager?.GetIndexCollection()
            ?? _qdrantOptions.DefaultCollection;
    }

    /// <summary>
    /// Ingests a PDF document page-by-page, preserving page numbers in chunk metadata.
    /// </summary>
    public async Task IngestPdfPagesAsync(
        string documentId,
        List<(int PageNumber, string Text)> pages,
        string equipmentId,
        Dictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        if (pages.Count == 0)
        {
            _logger.LogWarning("No pages provided for PDF {DocumentId}, skipping ingestion", documentId);
            return;
        }

        var collection = GetTargetCollection();
        var vectorSize = _qdrantOptions.VectorSize;
        await _vectorStore.EnsureCollectionAsync(collection, vectorSize, ct);

        var totalChunks = 0;

        foreach (var (pageNumber, pageText) in pages)
        {
            if (string.IsNullOrWhiteSpace(pageText)) continue;

            var chunks = ChunkTextWithOffsets(pageText, ChunkSize, ChunkOverlap);

            for (var i = 0; i < chunks.Count; i++)
            {
                var (chunkText, charStart, charEnd) = chunks[i];
                var chunkId = $"{documentId}:page:{pageNumber}:chunk:{i}";

                var embeddingText = FlattenMarkdownTables(chunkText);
                var vector = await _llmClient.GetEmbeddingAsync(embeddingText, isQuery: false, ct);

                var (lineStart, lineEnd) = ComputeLineRange(pageText, charStart, charEnd);

                var folderTags = ExtractFolderTags(documentId);
                var payload = new Dictionary<string, object>
                {
                    ["text"] = chunkText,
                    ["document_id"] = documentId,
                    ["equipment_id"] = equipmentId,
                    ["chunk_index"] = totalChunks,
                    ["page_number"] = pageNumber,
                    ["char_offset_start"] = charStart,
                    ["char_offset_end"] = charEnd,
                    ["line_start"] = lineStart,
                    ["line_end"] = lineEnd,
                    ["doc_type"] = InferDocType(documentId),
                    ["language"] = DetectLanguage(chunkText),
                    ["highlight_type"] = InferHighlightType(chunkText)
                };

                if (folderTags.Count > 0)
                    payload["folder_tags"] = folderTags;

                foreach (var kvp in metadata)
                    payload[kvp.Key] = kvp.Value;

                await _vectorStore.UpsertAsync(collection, chunkId, vector, payload, ct);

                if (_bm25Index is not null && _ragOptions.EnableBm25)
                    _bm25Index.AddDocument(chunkId, chunkText);

                totalChunks++;
            }
        }

        _logger.LogInformation(
            "Completed PDF ingestion of {DocumentId}. Pages={PageCount}, TotalChunks={ChunkCount}",
            documentId, pages.Count, totalChunks);
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

        var collection = GetTargetCollection();
        var vectorSize = _qdrantOptions.VectorSize;

        // Ensure the collection exists
        await _vectorStore.EnsureCollectionAsync(collection, vectorSize, ct);

        // Multi-tier chunking: Structure-Based → Semantic → Sliding Window
        var isMarkdown = documentId.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        var tieredChunks = await ChunkTextMultiTierAsync(text, isMarkdown, ChunkSize, ChunkOverlap, ct);
        var chunks = tieredChunks.Select(tc => tc.Text).ToList();
        var chunkStrategies = tieredChunks.Select(tc => tc.Strategy).ToList();

        // Build figure/table caption map for cross-reference enrichment
        var figureCaptionMap = FigureCrossReferenceParser.BuildCaptionMap(text);

        _logger.LogInformation(
            "Ingesting document {DocumentId} for equipment {EquipmentId}. TextLength={TextLength}, ChunkCount={ChunkCount}, Strategy={Strategy}, FigureCaptions={FigureCount}",
            documentId, equipmentId, text.Length, chunks.Count,
            chunkStrategies.Count > 0 ? chunkStrategies[0] : "unknown",
            figureCaptionMap.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var chunkId = $"{documentId}:chunk:{i}";

            // Enrich chunk with figure/table caption context for referenced figures
            var enrichedChunk = FigureCrossReferenceParser.EnrichChunkWithFigureContext(chunk, figureCaptionMap);

            // Flatten markdown tables before embedding to avoid content dilution
            var embeddingText = FlattenMarkdownTables(enrichedChunk);

            // Embed the chunk (using search_document prefix via isQuery: false)
            var vector = await _llmClient.GetEmbeddingAsync(embeddingText, isQuery: false, ct);

            // Build payload with metadata (store original chunk text, not flattened)
            var folderTags = ExtractFolderTags(documentId);
            var payload = new Dictionary<string, object>
            {
                ["text"] = chunk,
                ["document_id"] = documentId,
                ["equipment_id"] = equipmentId,
                ["chunk_index"] = i,
                ["chunk_count"] = chunks.Count,
                ["doc_type"] = InferDocType(documentId),
                ["language"] = DetectLanguage(chunk),
                ["highlight_type"] = InferHighlightType(chunk),
                ["chunk_strategy"] = i < chunkStrategies.Count ? chunkStrategies[i] : "unknown"
            };

            if (folderTags.Count > 0)
                payload["folder_tags"] = folderTags;

            // v3.3: Compute line range from chunk position in full text
            var charStart = text.IndexOf(chunk, StringComparison.Ordinal);
            if (charStart >= 0)
            {
                var (ls, le) = ComputeLineRange(text, charStart, charStart + chunk.Length);
                payload["line_start"] = ls;
                payload["line_end"] = le;
            }

            // Extract section info from chunk header prefix (e.g. "[## Header > ### Sub]")
            var (chapter, section) = ExtractSectionFromChunk(chunk);
            if (!string.IsNullOrEmpty(chapter)) payload["chapter"] = chapter;
            if (!string.IsNullOrEmpty(section)) payload["section"] = section;

            // v3.2: Store full header hierarchy as parent_context
            var parentContext = ExtractParentContext(chunk);
            if (!string.IsNullOrEmpty(parentContext)) payload["parent_context"] = parentContext;

            // v4.83: Store figure/table cross-references
            var figureRefs = FigureCrossReferenceParser.FindReferences(chunk);
            if (figureRefs.Count > 0)
            {
                payload["figure_references"] = string.Join(", ",
                    figureRefs.Select(r => $"{r.ReferenceType}:{r.ReferenceId}"));
            }

            // Merge user-supplied metadata
            foreach (var kvp in metadata)
            {
                payload[kvp.Key] = kvp.Value;
            }

            // Upsert into the vector store
            await _vectorStore.UpsertAsync(collection, chunkId, vector, payload, ct);

            // Index in BM25 for hybrid search
            if (_bm25Index is not null && _ragOptions.EnableBm25)
            {
                _bm25Index.AddDocument(chunkId, chunk);
            }

            _logger.LogDebug(
                "Upserted chunk {ChunkIndex}/{ChunkCount} for document {DocumentId}",
                i + 1, chunks.Count, documentId);
        }

        _logger.LogInformation(
            "Completed vector ingestion of document {DocumentId}. Chunks upserted: {ChunkCount}",
            documentId, chunks.Count);

        // Post-processing: batch entity extraction for knowledge graph (if enabled)
        if (_entityExtractor is not null && _graphStore is not null && _ragOptions.ExtractEntitiesOnIngest)
        {
            try
            {
                _logger.LogInformation(
                    "Starting batch entity extraction for {DocumentId} ({ChunkCount} chunks)",
                    documentId, chunks.Count);

                var (entities, relations) = await _entityExtractor.ExtractFromBatchAsync(chunks, ct);

                foreach (var entity in entities)
                {
                    await _graphStore.UpsertEntityAsync(entity, ct);
                }

                foreach (var relation in relations)
                {
                    await _graphStore.UpsertRelationAsync(relation, ct);
                }

                _logger.LogInformation(
                    "Batch entity extraction completed for {DocumentId}: {EntityCount} entities, {RelationCount} relations",
                    documentId, entities.Count, relations.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Batch entity extraction failed for {DocumentId}, vector data is still available",
                    documentId);
            }
        }
    }

    // ─── Multi-Tier Chunking ──────────────────────────────────────────

    /// <summary>
    /// 3-tier chunking fallback: Structure-Based → Semantic → Sliding Window.
    /// Returns chunks with strategy metadata for traceability.
    /// </summary>
    internal async Task<List<(string Text, string Strategy)>> ChunkTextMultiTierAsync(
        string text, bool isMarkdown, int chunkSize, int overlap, CancellationToken ct = default)
    {
        // Tier 1: Structure-based (for markdown)
        if (isMarkdown)
        {
            var structureChunks = ChunkMarkdown(text, chunkSize, overlap);
            if (structureChunks.Count > 0)
            {
                var validated = ValidateChunkSizes(structureChunks, _ragOptions.MinChunkTokens, _ragOptions.MaxChunkTokens);
                _logger.LogDebug("Chunking Tier 1 (Structure-Based): {Count} chunks", validated.Count);
                return validated.Select(c => (c, "structure")).ToList();
            }
        }

        // Tier 2: Semantic boundary detection
        if (_ragOptions.EnableSemanticChunking)
        {
            var semanticChunks = await ChunkTextSemanticAsync(text, chunkSize, _ragOptions.SemanticBoundaryThreshold, ct);
            if (semanticChunks.Count > 0)
            {
                var validated = ValidateChunkSizes(semanticChunks, _ragOptions.MinChunkTokens, _ragOptions.MaxChunkTokens);
                _logger.LogDebug("Chunking Tier 2 (Semantic): {Count} chunks", validated.Count);
                return validated.Select(c => (c, "semantic")).ToList();
            }
        }

        // Tier 3: Sliding window (fallback)
        var slidingChunks = ChunkText(text, chunkSize, overlap);
        var validatedSliding = ValidateChunkSizes(slidingChunks, _ragOptions.MinChunkTokens, _ragOptions.MaxChunkTokens);
        _logger.LogDebug("Chunking Tier 3 (Sliding Window): {Count} chunks", validatedSliding.Count);
        return validatedSliding.Select(c => (c, "sliding_window")).ToList();
    }

    /// <summary>
    /// Semantic boundary detection: splits text into paragraphs and detects
    /// topic transitions by computing cosine similarity between consecutive
    /// paragraph embeddings. A drop below the threshold triggers a new chunk.
    /// </summary>
    internal async Task<List<string>> ChunkTextSemanticAsync(
        string text, int maxChunkSize, float similarityThreshold, CancellationToken ct = default)
    {
        var paragraphs = SplitIntoParagraphs(text);
        if (paragraphs.Count <= 1)
            return [text.Trim()];

        // Get embeddings for all paragraphs
        var embeddings = new List<float[]>();
        foreach (var para in paragraphs)
        {
            var embedding = await _llmClient.GetEmbeddingAsync(para, isQuery: false, ct);
            embeddings.Add(embedding);
        }

        // Detect semantic boundaries
        var chunks = new List<string>();
        var currentChunk = new List<string> { paragraphs[0] };

        for (var i = 1; i < paragraphs.Count; i++)
        {
            var similarity = CosineSimilarity(embeddings[i - 1], embeddings[i]);
            var currentLength = string.Join("\n\n", currentChunk).Length;

            // Start a new chunk if:
            // 1. Semantic similarity drops below threshold (topic change), OR
            // 2. Current chunk exceeds max size
            if (similarity < similarityThreshold || currentLength + paragraphs[i].Length > maxChunkSize)
            {
                chunks.Add(string.Join("\n\n", currentChunk));
                currentChunk = [paragraphs[i]];
            }
            else
            {
                currentChunk.Add(paragraphs[i]);
            }
        }

        if (currentChunk.Count > 0)
            chunks.Add(string.Join("\n\n", currentChunk));

        return chunks;
    }

    /// <summary>
    /// Splits text into paragraphs (separated by blank lines or 2+ newlines).
    /// </summary>
    internal static List<string> SplitIntoParagraphs(string text)
    {
        var paragraphs = Regex.Split(text, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return paragraphs;
    }

    /// <summary>
    /// Validates and adjusts chunk sizes: merges under-sized chunks, splits over-sized ones.
    /// </summary>
    internal static List<string> ValidateChunkSizes(List<string> chunks, int minTokens, int maxTokens)
    {
        if (chunks.Count == 0) return chunks;

        // Approximate: 1 token ≈ 4 characters (rough estimate for mixed lang)
        const int charsPerToken = 4;
        var minChars = minTokens * charsPerToken;
        var maxChars = maxTokens * charsPerToken;

        var result = new List<string>();
        var pendingMerge = "";

        foreach (var chunk in chunks)
        {
            var current = string.IsNullOrEmpty(pendingMerge) ? chunk : pendingMerge + "\n\n" + chunk;

            if (current.Length < minChars)
            {
                // Too small — accumulate for merge
                pendingMerge = current;
                continue;
            }

            if (current.Length > maxChars)
            {
                // Too large — split with sliding window
                var subChunks = ChunkText(current, maxChars, minChars / 2);
                result.AddRange(subChunks);
                pendingMerge = "";
            }
            else
            {
                result.Add(current);
                pendingMerge = "";
            }
        }

        // Handle remaining pending merge
        if (!string.IsNullOrEmpty(pendingMerge))
        {
            if (result.Count > 0)
            {
                // Merge with the last chunk
                result[^1] = result[^1] + "\n\n" + pendingMerge;
            }
            else
            {
                result.Add(pendingMerge);
            }
        }

        return result;
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
            return 0f;

        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0f : dot / denominator;
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
    /// Chunks text and tracks character offsets (start, end) relative to the original text.
    /// Used for PDF ingestion to enable highlight overlay in the citation pane.
    /// </summary>
    internal static List<(string Text, int Start, int End)> ChunkTextWithOffsets(string text, int chunkSize, int overlap)
    {
        var result = new List<(string Text, int Start, int End)>();

        if (string.IsNullOrEmpty(text))
            return result;

        if (text.Length <= chunkSize)
        {
            result.Add((text, 0, text.Length));
            return result;
        }

        var offset = 0;
        while (offset < text.Length)
        {
            var remaining = text.Length - offset;
            if (remaining <= chunkSize)
            {
                result.Add((text.Substring(offset), offset, text.Length));
                break;
            }

            var candidate = text.Substring(offset, chunkSize);
            var splitAt = FindLastSentenceBoundary(candidate);
            var chunk = text.Substring(offset, splitAt);
            result.Add((chunk, offset, offset + splitAt));

            var advance = splitAt - overlap;
            if (advance <= 0) advance = 1;
            offset += advance;
        }

        return result;
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

    // ─── Table Flattening ──────────────────────────────────────────────

    private static readonly Regex TableRowRegex = new(@"^\|(.+)\|$", RegexOptions.Multiline);
    private static readonly Regex SeparatorRowRegex = new(@"^\|[\s\-:|]+\|$", RegexOptions.Multiline);

    /// <summary>
    /// Converts markdown tables in a chunk to natural language sentences.
    /// This prevents embedding content dilution caused by pipe/dash table syntax.
    /// The header prefix (e.g., "[## Section > ### Sub]") is preserved.
    /// </summary>
    internal static string FlattenMarkdownTables(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('|'))
            return text;

        var lines = text.Split('\n');
        var result = new List<string>();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();

            // Detect table: current line has pipes and next line is a separator row
            if (TableRowRegex.IsMatch(line) && i + 1 < lines.Length && SeparatorRowRegex.IsMatch(lines[i + 1].Trim()))
            {
                // Parse header row
                var headers = ParseTableRow(line);
                i += 2; // skip header + separator

                // Parse data rows
                while (i < lines.Length && TableRowRegex.IsMatch(lines[i].Trim()) && !SeparatorRowRegex.IsMatch(lines[i].Trim()))
                {
                    var cells = ParseTableRow(lines[i].Trim());
                    var parts = new List<string>();
                    for (var c = 0; c < Math.Min(headers.Count, cells.Count); c++)
                    {
                        if (!string.IsNullOrWhiteSpace(cells[c]))
                            parts.Add($"{headers[c]}: {cells[c]}");
                    }
                    if (parts.Count > 0)
                        result.Add(string.Join(", ", parts) + ".");
                    i++;
                }
            }
            else
            {
                result.Add(lines[i]);
                i++;
            }
        }

        return string.Join("\n", result);
    }

    private static List<string> ParseTableRow(string row)
    {
        // Remove leading/trailing pipe and split by pipe
        var trimmed = row.Trim('|');
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    // ─── Metadata Inference ─────────────────────────────────────────────

    /// <summary>
    /// Infers the highlight type for a chunk based on its content.
    /// Returns "table" for table-like content, "image" for image markers, or "text" for plain text.
    /// </summary>
    internal static string InferHighlightType(string chunkText)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
            return "text";

        // Check for table: 3+ lines each containing 3+ pipe characters
        var lines = chunkText.Split('\n');
        var pipeLineCount = 0;
        foreach (var line in lines)
        {
            if (line.Count(c => c == '|') >= 3)
                pipeLineCount++;
        }
        if (pipeLineCount >= 3)
            return "table";

        // Check for image markers
        if (Regex.IsMatch(chunkText, @"\[image\]|<<image>>|\[그림\]|\[Figure\]|\[사진\]", RegexOptions.IgnoreCase))
            return "image";

        return "text";
    }

    /// <summary>
    /// Infers the document type from the file name using keyword patterns.
    /// Falls back to folder name matching if file name doesn't match.
    /// </summary>
    internal static string InferDocType(string fileName)
    {
        var result = InferDocTypeFromName(fileName);
        if (result != "general")
            return result;

        // Fallback: try folder segments in the path
        var segments = ExtractFolderTags(fileName);
        foreach (var segment in segments)
        {
            var folderResult = InferDocTypeFromName(segment);
            if (folderResult != "general")
                return folderResult;
        }

        return "general";
    }

    private static string InferDocTypeFromName(string name)
    {
        var lower = name.ToLowerInvariant();

        if (lower.Contains("alarm") || lower.Contains("error") || lower.Contains("fault"))
            return "alarm";
        if (lower.Contains("troubleshoot") || lower.Contains("진단") || lower.Contains("조치"))
            return "troubleshooting";
        if (lower.Contains("maintenance") || lower.Contains("유지보수") || lower.Contains("점검"))
            return "maintenance";
        if (lower.Contains("procedure") || lower.Contains("절차") || lower.Contains("교체"))
            return "procedure";
        if (lower.Contains("spec") || lower.Contains("사양") || lower.Contains("parameter") || lower.Contains("파라미터"))
            return "specification";
        if (lower.Contains("overview") || lower.Contains("개요") || lower.Contains("소개"))
            return "overview";
        if (lower.Contains("glossary") || lower.Contains("용어"))
            return "glossary";
        if (lower.Contains("part") || lower.Contains("부품") || lower.Contains("consumable") || lower.Contains("소모품"))
            return "parts";

        return "general";
    }

    /// <summary>
    /// Extracts folder segments from a relative path as metadata tags.
    /// Filters out generic folder names (docs, src, etc.) and the file name.
    /// </summary>
    internal static List<string> ExtractFolderTags(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return [];

        // Normalize path separators
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length <= 1)
            return []; // Only file name, no folders

        // Exclude the last segment (file name) and filter generic folder names
        var folderSegments = segments[..^1]
            .Select(s => s.ToLowerInvariant())
            .Where(s => !IsGenericFolderName(s))
            .ToList();

        return folderSegments;
    }

    private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "docs", "doc", "documents", "src", "source", "resources", "res",
        "assets", "data", "files", "content", "static", "public", "lib"
    };

    private static bool IsGenericFolderName(string name)
    {
        return GenericFolderNames.Contains(name);
    }

    /// <summary>
    /// Detects the primary language of a text by counting Hangul vs Latin characters.
    /// </summary>
    internal static string DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "unknown";

        var hangulCount = 0;
        var latinCount = 0;

        foreach (var ch in text)
        {
            if (ch is >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F')
                hangulCount++;
            else if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
                latinCount++;
        }

        if (hangulCount == 0 && latinCount == 0)
            return "unknown";

        var total = hangulCount + latinCount;
        var koreanRatio = (double)hangulCount / total;

        return koreanRatio > 0.5 ? "ko" : "en";
    }

    /// <summary>
    /// Extracts chapter and section from a markdown chunk's header prefix.
    /// Looks for the "[## Header > ### Sub]" format added by ChunkMarkdown.
    /// </summary>
    internal static (string? Chapter, string? Section) ExtractSectionFromChunk(string chunk)
    {
        if (!chunk.StartsWith('['))
            return (null, null);

        var closeBracket = chunk.IndexOf("]\n", StringComparison.Ordinal);
        if (closeBracket < 0)
            closeBracket = chunk.IndexOf(']');
        if (closeBracket < 0)
            return (null, null);

        var headerPath = chunk[1..closeBracket];
        var parts = headerPath.Split(" > ", StringSplitOptions.RemoveEmptyEntries);

        // First part is the top-level chapter, last part is the section
        var chapter = parts.Length > 0 ? CleanHeaderText(parts[0]) : null;
        var section = parts.Length > 1 ? CleanHeaderText(parts[^1]) : null;

        return (chapter, section);
    }

    private static string CleanHeaderText(string header)
    {
        // Remove leading # characters and whitespace
        return Regex.Replace(header, @"^#{1,6}\s*", "").Trim();
    }

    /// <summary>
    /// Extracts the full header hierarchy path from a chunk's "[## H > ### S]" prefix.
    /// Returns a cleaned string like "CMP Overview > Process Steps" for use as parent_context.
    /// </summary>
    internal static string? ExtractParentContext(string chunk)
    {
        if (!chunk.StartsWith('['))
            return null;

        var closeBracket = chunk.IndexOf("]\n", StringComparison.Ordinal);
        if (closeBracket < 0)
            closeBracket = chunk.IndexOf(']');
        if (closeBracket < 0)
            return null;

        var headerPath = chunk[1..closeBracket];
        var parts = headerPath.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var cleaned = string.Join(" > ", parts.Select(CleanHeaderText));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Computes 1-based line numbers for a character range within the full text (v3.3).
    /// </summary>
    internal static (int LineStart, int LineEnd) ComputeLineRange(string fullText, int charStart, int charEnd)
    {
        var lineStart = 1;
        var lineEnd = 1;
        var safeEnd = Math.Min(charEnd, fullText.Length);

        for (var i = 0; i < safeEnd; i++)
        {
            if (fullText[i] == '\n')
            {
                if (i < charStart)
                    lineStart++;
                lineEnd++;
            }
        }

        return (lineStart, lineEnd);
    }
}
