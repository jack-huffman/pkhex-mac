using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Summarises one box: what it holds, and anything in it that fails a legality check.
/// </summary>
/// <remarks>
/// All of this is knowable only by opening all thirty slots one at a time, which is
/// why it is worth surfacing. Legality dominates the cost — roughly 1.5ms per entity —
/// so callers should run this off the UI thread.
/// </remarks>
public static class BoxInsights
{
    public static BoxSummary Analyze(SaveFile sav, int box, GameStrings strings,
                                     CancellationToken token = default)
    {
        if (!sav.HasBox || (uint)box >= sav.BoxCount)
            return BoxSummary.Empty;

        var capacity = sav.BoxSlotCount;
        var present = new List<(PKM Entity, int Slot)>();
        for (int slot = 0; slot < capacity; slot++)
        {
            if (token.IsCancellationRequested)
                return BoxSummary.Empty;
            var pk = sav.GetBoxSlotAtIndex(box, slot);
            if (pk.Species != 0)
                present.Add((pk, slot));
        }

        if (present.Count == 0)
            return new BoxSummary(0, capacity, 0, string.Empty, string.Empty, [], false);

        var shiny = present.Count(p => p.Entity.IsShiny);
        var levels = present.Select(p => (int)p.Entity.CurrentLevel).ToList();
        var levelText = levels.Min() == levels.Max()
            ? $"all Lv. {levels[0]}"
            : $"Lv. {levels.Min()}–{levels.Max()}";

        // Where the contents came from, most common first.
        var origins = present
            .GroupBy(p => p.Entity.Version)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{GameInfo.GetVersionName(g.Key)} {g.Count()}");
        var originText = string.Join(" · ", origins);

        var problems = new List<BoxProblem>();
        foreach (var (entity, slot) in present)
        {
            if (token.IsCancellationRequested)
                return BoxSummary.Empty;
            try
            {
                var la = new LegalityAnalysis(entity);
                if (la.Valid)
                    continue;
                problems.Add(new BoxProblem(Name(entity, strings), slot, FirstIssue(la), entity));
            }
            catch (Exception ex)
            {
                // An entity the analyser cannot parse is itself worth reporting.
                problems.Add(new BoxProblem(Name(entity, strings), slot, ex.Message, entity));
            }
        }

        return new BoxSummary(present.Count, capacity, shiny, levelText, originText, problems, true);
    }

    private static string Name(PKM pk, GameStrings strings) =>
        (uint)pk.Species < strings.specieslist.Length ? strings.specieslist[pk.Species] : $"#{pk.Species}";

    /// <summary>The first real line of the report, which is the most specific complaint.</summary>
    private static string FirstIssue(LegalityAnalysis la)
    {
        foreach (var line in la.Report().Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("Valid", StringComparison.Ordinal))
                return trimmed;
        }
        return "Fails a legality check.";
    }
}

/// <summary>What a box contains, and what is wrong with it.</summary>
public sealed record BoxSummary(int Filled, int Capacity, int Shiny, string LevelText,
                               string OriginText, IReadOnlyList<BoxProblem> Problems, bool HasContents)
{
    public static readonly BoxSummary Empty = new(0, 0, 0, string.Empty, string.Empty, [], false);

    public string FillText => Capacity == 0 ? string.Empty : $"{Filled} of {Capacity} filled";
}

/// <summary>One Pokémon in the box that does not pass legality.</summary>
public sealed record BoxProblem(string Species, int Slot, string Issue, PKM Entity)
{
    public string SlotText => $"slot {Slot + 1}";
}
