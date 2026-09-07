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
/// so callers snapshot the box on the UI thread with <see cref="Snapshot"/> and run
/// <see cref="Analyze(BoxSnapshot, GameStrings, CancellationToken)"/> off it. The snapshot holds copies, so edits made meanwhile
/// cannot tear a read.
/// </remarks>
public static class BoxInsights
{
    /// <summary>Copies of the box's occupied slots, taken on the thread that owns the save.</summary>
    public static BoxSnapshot Snapshot(SaveFile sav, int box)
    {
        if (!sav.HasBox || (uint)box >= sav.BoxCount)
            return new BoxSnapshot(0, []);
        var present = new List<(PKM Entity, int Slot)>();
        for (int slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            var pk = sav.GetBoxSlotAtIndex(box, slot);
            if (pk.Species != 0)
                present.Add((pk, slot));
        }
        return new BoxSnapshot(sav.BoxSlotCount, present);
    }

    /// <summary>Convenience for callers on the owning thread that do not need to split the work.</summary>
    public static BoxSummary Analyze(SaveFile sav, int box, GameStrings strings, CancellationToken token = default) =>
        Analyze(Snapshot(sav, box), strings, token);

    public static BoxSummary Analyze(BoxSnapshot snapshot, GameStrings strings, CancellationToken token = default)
    {
        var (capacity, present) = snapshot;
        if (capacity == 0)
            return BoxSummary.Empty;
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
            token.ThrowIfCancellationRequested();
            try
            {
                var la = new LegalityAnalysis(entity);
                if (la.Valid)
                    continue;
                problems.Add(new BoxProblem(strings.SpeciesName(entity), slot, LegalitySummary.FirstIssue(la), entity));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An entity the analyser cannot parse is itself worth reporting.
                problems.Add(new BoxProblem(strings.SpeciesName(entity), slot, ex.Message, entity));
            }
        }

        return new BoxSummary(present.Count, capacity, shiny, levelText, originText, problems, true);
    }
}

/// <summary>The occupied slots of one box, copied out of the save.</summary>
public sealed record BoxSnapshot(int Capacity, IReadOnlyList<(PKM Entity, int Slot)> Present);

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
