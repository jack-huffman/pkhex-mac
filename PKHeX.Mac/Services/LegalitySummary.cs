using System;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Reduces a legality report to the one line worth showing in a list.
/// </summary>
public static class LegalitySummary
{
    /// <summary>
    /// The first real complaint in the report. PKHeX's report opens with a verdict line
    /// ("Valid" / "Invalid"), so the first line after that is the most specific issue.
    /// Returns an empty string for a legal entity, and <paramref name="fallback"/> when the
    /// report has no usable line.
    /// </summary>
    public static string FirstIssue(LegalityAnalysis analysis, string fallback = "Fails a legality check.")
    {
        if (analysis.Valid)
            return string.Empty;
        foreach (var line in analysis.Report().Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("Valid", StringComparison.Ordinal))
                return trimmed;
        }
        return fallback;
    }
}
