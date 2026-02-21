using System.Text.Json;
using FabCopilot.RagService.Interfaces;

namespace FabCopilot.RagService.Services;

/// <summary>
/// A file-backed synonym dictionary loaded from a JSON file.
/// JSON format: array of arrays, where each inner array is a synonym group.
/// Example: [["패드", "polishing pad", "pad"], ["슬러리", "slurry", "연마액"]]
/// Every term in a group maps to all other terms in the same group.
/// </summary>
public sealed class SynonymDictionary : ISynonymDictionary
{
    private readonly Dictionary<string, List<string>> _synonymMap = new(StringComparer.OrdinalIgnoreCase);
    private int _groupCount;

    public int GroupCount => _groupCount;

    /// <summary>
    /// Loads synonym groups from a JSON file. Each group is a string array.
    /// </summary>
    public static SynonymDictionary LoadFromFile(string filePath)
    {
        var dict = new SynonymDictionary();

        if (!File.Exists(filePath))
            return dict;

        var json = File.ReadAllText(filePath);
        var groups = JsonSerializer.Deserialize<string[][]>(json);

        if (groups is null)
            return dict;

        foreach (var group in groups)
        {
            dict.AddGroup(group);
        }

        return dict;
    }

    /// <summary>
    /// Creates a dictionary from pre-built groups (for testing).
    /// </summary>
    public static SynonymDictionary FromGroups(IEnumerable<string[]> groups)
    {
        var dict = new SynonymDictionary();
        foreach (var group in groups)
            dict.AddGroup(group);
        return dict;
    }

    private void AddGroup(string[] terms)
    {
        if (terms.Length < 2) return;

        _groupCount++;
        var groupList = terms.ToList();

        foreach (var term in terms)
        {
            var lower = term.ToLowerInvariant();
            if (_synonymMap.TryGetValue(lower, out var existing))
            {
                // Merge: add new terms not already present
                foreach (var t in groupList)
                {
                    if (!existing.Contains(t, StringComparer.OrdinalIgnoreCase))
                        existing.Add(t);
                }
            }
            else
            {
                _synonymMap[lower] = new List<string>(groupList);
            }
        }
    }

    public IReadOnlyList<string> GetSynonyms(string keyword)
    {
        if (_synonymMap.TryGetValue(keyword.ToLowerInvariant(), out var synonyms))
            return synonyms;

        return [];
    }

    public IReadOnlyList<string> ExpandAll(IEnumerable<string> keywords)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kw in keywords)
        {
            expanded.Add(kw);
            var synonyms = GetSynonyms(kw);
            foreach (var s in synonyms)
                expanded.Add(s);
        }

        return expanded.ToList();
    }
}
