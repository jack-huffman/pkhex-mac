using System;
using System.Text;

namespace PKHeX.Mac.Services;

/// <summary>
/// Turns PKHeX's internal identifiers into labels a person can read.
/// </summary>
/// <remarks>
/// PKHeX names things for code, not for display: block labels like <c>KUnlockedUpgradeFly</c>,
/// property names like <c>BattleSubwayPlay</c>, record keys like <c>total_capture</c>. They are
/// the only names those values have, so they are worth showing, but not raw.
/// </remarks>
public static class DisplayNames
{
    /// <summary>
    /// "BattleSubwayPlay" → "Battle subway play"; "Club1SmugElegant" → "Club 1 smug elegant".
    /// Runs of capitals stay together, so "PIDIsShiny" becomes "PID is shiny".
    /// </summary>
    /// <param name="name">A PascalCase identifier.</param>
    /// <param name="keepCase">Leave each word's casing alone rather than lower-casing the rest.</param>
    public static string FromPascalCase(string name, bool keepCase = false)
    {
        if (name.Length == 0)
            return name;
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var boundary = i > 0 && IsWordBoundary(name, i);
            if (boundary)
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : keepCase || IsAcronymLetter(name, i, boundary) ? c : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>A capital that continues or starts a run of capitals stays as it is: "PID", "HOME".</summary>
    private static bool IsAcronymLetter(string name, int i, bool boundary)
    {
        if (!char.IsUpper(name[i]))
            return false;
        return boundary
            ? i + 1 < name.Length && char.IsUpper(name[i + 1])
            : char.IsUpper(name[i - 1]);
    }

    private static bool IsWordBoundary(string name, int i)
    {
        var c = name[i];
        var previous = name[i - 1];
        if (char.IsDigit(c) != char.IsDigit(previous))
            return true;
        // A capital starts a word unless it continues an acronym ("PID", "HOME").
        if (!char.IsUpper(c))
            return false;
        if (!char.IsUpper(previous))
            return true;
        return i + 1 < name.Length && char.IsLower(name[i + 1]);
    }

    /// <summary>"total_capture" → "Total capture".</summary>
    public static string FromSnakeCase(string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return key;
        var sb = new StringBuilder(key.Length + 2);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i != 0)
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(parts[i][0]) + parts[i][1..] : parts[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// PKHeX's block labels are identifiers prefixed with K — <c>KMoney</c>, <c>KUnlockedUpgradeFly</c>.
    /// Anything else is returned untouched.
    /// </summary>
    public static string FromBlockLabel(string label) =>
        label.Length >= 2 && label[0] == 'K' && char.IsUpper(label[1])
            ? FromPascalCase(label[1..])
            : label;

    /// <summary>Strips a trailing word before formatting: "SmugPurchased" with "Purchased" → "Smug".</summary>
    public static string WithoutSuffix(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
}
