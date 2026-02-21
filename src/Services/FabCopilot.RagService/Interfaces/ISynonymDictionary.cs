namespace FabCopilot.RagService.Interfaces;

public interface ISynonymDictionary
{
    /// <summary>
    /// Gets all synonyms (including the term itself) for the given keyword.
    /// Returns an empty list if no synonyms exist.
    /// </summary>
    IReadOnlyList<string> GetSynonyms(string keyword);

    /// <summary>
    /// Expands a list of keywords by adding all known synonyms.
    /// </summary>
    IReadOnlyList<string> ExpandAll(IEnumerable<string> keywords);

    /// <summary>
    /// Total number of synonym groups loaded.
    /// </summary>
    int GroupCount { get; }
}
