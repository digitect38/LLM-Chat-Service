namespace FabCopilot.RagService.Services.Bm25;

/// <summary>
/// In-process BM25 inverted index for keyword-based document retrieval.
/// Abstracted behind an interface to allow future replacement (e.g., Elasticsearch).
/// </summary>
public interface IBm25Index
{
    /// <summary>
    /// Adds or updates a document in the index.
    /// </summary>
    void AddDocument(string documentId, string text);

    /// <summary>
    /// Removes a document from the index.
    /// </summary>
    void RemoveDocument(string documentId);

    /// <summary>
    /// Removes all documents whose ID starts with the given prefix.
    /// Useful for removing all chunks of a single source file.
    /// </summary>
    void RemoveByPrefix(string documentIdPrefix);

    /// <summary>
    /// Searches the index and returns document IDs ranked by BM25 score.
    /// </summary>
    List<(string DocumentId, double Score)> Search(string query, int topK);

    /// <summary>
    /// Clears all documents from the index.
    /// </summary>
    void Clear();

    /// <summary>
    /// Returns the number of documents currently indexed.
    /// </summary>
    int DocumentCount { get; }
}
