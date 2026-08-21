using System;
using System.Collections.Generic;

namespace PKHeX.Mac.Services;

/// <summary>
/// Ranks command-palette entries against what the user has typed.
/// </summary>
/// <remarks>
/// Kept separate from the view model so the ranking can be tested without a UI. The
/// rules are deliberately simple and predictable: an exact title match beats a prefix,
/// a prefix beats a word start, a word start beats a substring, and a subsequence
/// (typing "bbp" for "Blueberry Perks") comes last. Ties fall back to the shorter
/// title, so "Bag" outranks "Bag / Items" for the query "bag".
/// </remarks>
public static class CommandSearch
{
    /// <summary>Score for a single entry, or null when it does not match at all.</summary>
    public static int? Score(string title, string group, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0;

        query = query.Trim();
        var t = title.AsSpan();

        if (title.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 1000;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 900 - Math.Min(title.Length, 99);
        if (StartsAWord(title, query))
            return 800 - Math.Min(title.Length, 99);
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 700 - Math.Min(title.Length, 99);
        // The group is worth matching so "trainer" surfaces everything in that section.
        if (group.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 500 - Math.Min(title.Length, 99);
        if (IsSubsequence(title, query))
            return 300 - Math.Min(title.Length, 99);
        return null;
    }

    /// <summary>True when the query begins any word of the title.</summary>
    private static bool StartsAWord(string title, string query)
    {
        for (int i = 1; i < title.Length; i++)
        {
            if (!char.IsLetterOrDigit(title[i - 1]) && char.IsLetterOrDigit(title[i])
                && title.AsSpan(i).StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>True when the query's letters appear in order, as initials or otherwise.</summary>
    private static bool IsSubsequence(string title, string query)
    {
        int q = 0;
        foreach (var c in title)
        {
            if (q < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[q]))
                q++;
        }
        return q == query.Length;
    }

    /// <summary>Ranks and filters a set of entries, best first.</summary>
    public static List<T> Rank<T>(IEnumerable<T> entries, string query,
                                  Func<T, string> title, Func<T, string> group, int limit = 40)
    {
        var scored = new List<(T Entry, int Score)>();
        foreach (var entry in entries)
        {
            if (Score(title(entry), group(entry), query) is { } score)
                scored.Add((entry, score));
        }
        scored.Sort((a, b) => b.Score != a.Score
            ? b.Score.CompareTo(a.Score)
            : string.Compare(title(a.Entry), title(b.Entry), StringComparison.OrdinalIgnoreCase));

        var result = new List<T>(Math.Min(limit, scored.Count));
        foreach (var (entry, _) in scored)
        {
            if (result.Count == limit)
                break;
            result.Add(entry);
        }
        return result;
    }
}
