using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Compares the save being edited against an untouched parse of the file it came from.
/// </summary>
/// <remarks>
/// The pristine copy already exists for revert, so this costs one pass: about 60ms for
/// the block table and 1ms for the box slots on a Scarlet/Violet save. Nothing else can
/// answer "what am I about to write", because PKHeX works one entity at a time and has
/// no notion of a session.
/// </remarks>
public static class SaveDiff
{
    public static IReadOnlyList<SaveChange> Compare(SaveFile live, SaveFile pristine,
                                                    GameStrings strings, CancellationToken token = default)
    {
        var changes = new List<SaveChange>();
        CompareEntities(live, pristine, strings, changes, token);
        CompareBlocks(live, pristine, changes, token);
        return changes;
    }

    // =====================================================================
    // Stored Pokémon
    // =====================================================================

    private static void CompareEntities(SaveFile live, SaveFile pristine, GameStrings strings,
                                        List<SaveChange> changes, CancellationToken token)
    {
        if (live.HasBox && pristine.HasBox)
        {
            var boxes = Math.Min(live.BoxCount, pristine.BoxCount);
            for (int box = 0; box < boxes && !token.IsCancellationRequested; box++)
            {
                for (int slot = 0; slot < live.BoxSlotCount; slot++)
                {
                    Compare(live.GetBoxSlotAtIndex(box, slot), pristine.GetBoxSlotAtIndex(box, slot),
                            BoxName(live, box, slot), strings, changes);
                }
            }
        }

        if (live.HasParty && pristine.HasParty)
        {
            for (int i = 0; i < 6; i++)
            {
                if (i >= live.PartyCount && i >= pristine.PartyCount)
                    continue;
                var a = i < live.PartyCount ? live.GetPartySlotAtIndex(i) : live.BlankPKM;
                var b = i < pristine.PartyCount ? pristine.GetPartySlotAtIndex(i) : pristine.BlankPKM;
                Compare(a, b, $"Party, slot {i + 1}", strings, changes);
            }
        }

        // The ride legendary lives past the last reachable box, so it needs asking for.
        if (RideLegendary.IsSupported(live) && RideLegendary.IsSupported(pristine))
        {
            var a = RideLegendary.Read(live) ?? live.BlankPKM;
            var b = RideLegendary.Read(pristine) ?? pristine.BlankPKM;
            Compare(a, b, "Ride slot", strings, changes);
        }
    }

    private static string BoxName(SaveFile sav, int box, int slot)
    {
        var name = sav is IBoxDetailNameRead named ? named.GetBoxName(box) : $"Box {box + 1}";
        return $"{name}, slot {slot + 1}";
    }

    private static void Compare(PKM now, PKM before, string where, GameStrings strings,
                                List<SaveChange> changes)
    {
        if (now.Data.SequenceEqual(before.Data))
            return;

        var nowName = Name(now, strings);
        var beforeName = Name(before, strings);

        if (before.Species == 0)
            changes.Add(new SaveChange(ChangeKind.Entity, where, $"added {nowName}", now));
        else if (now.Species == 0)
            changes.Add(new SaveChange(ChangeKind.Entity, where, $"removed {beforeName}", before));
        else if (now.Species != before.Species)
            changes.Add(new SaveChange(ChangeKind.Entity, where, $"{beforeName} → {nowName}", now));
        else
            changes.Add(new SaveChange(ChangeKind.Entity, where, $"{nowName}: {Describe(now, before)}", now));
    }

    private static string Name(PKM pk, GameStrings strings) =>
        pk.Species == 0 ? "nothing" : strings.SpeciesName(pk);

    /// <summary>Names the fields that actually moved, rather than saying "edited".</summary>
    private static string Describe(PKM now, PKM before)
    {
        var parts = new List<string>();
        if (now.CurrentLevel != before.CurrentLevel)
            parts.Add($"Lv {before.CurrentLevel} → {now.CurrentLevel}");
        if (now.Nickname != before.Nickname)
            parts.Add($"nickname \"{before.Nickname}\" → \"{now.Nickname}\"");
        if (now.IsShiny != before.IsShiny)
            parts.Add(now.IsShiny ? "made shiny" : "shininess removed");
        if (now.HeldItem != before.HeldItem)
            parts.Add("held item");
        if (now.Nature != before.Nature)
            parts.Add("nature");
        if (now.Ability != before.Ability)
            parts.Add("ability");
        if (now.Ball != before.Ball)
            parts.Add("ball");
        if (Moves(now) != Moves(before))
            parts.Add("moves");
        if (Ivs(now) != Ivs(before))
            parts.Add("IVs");
        if (Evs(now) != Evs(before))
            parts.Add("EVs");
        if (now.OriginalTrainerName != before.OriginalTrainerName)
            parts.Add("trainer");

        return parts.Count == 0 ? "edited" : string.Join(", ", parts);
    }

    private static string Moves(PKM pk) => $"{pk.Move1},{pk.Move2},{pk.Move3},{pk.Move4}";
    private static string Ivs(PKM pk) => $"{pk.IV_HP},{pk.IV_ATK},{pk.IV_DEF},{pk.IV_SPA},{pk.IV_SPD},{pk.IV_SPE}";
    private static string Evs(PKM pk) => $"{pk.EV_HP},{pk.EV_ATK},{pk.EV_DEF},{pk.EV_SPA},{pk.EV_SPD},{pk.EV_SPE}";

    // =====================================================================
    // Everything that is not a Pokémon
    // =====================================================================

    private static void CompareBlocks(SaveFile live, SaveFile pristine, List<SaveChange> changes,
                                      CancellationToken token)
    {
        if (live is not ISCBlockArray a || pristine is not ISCBlockArray b)
            return;

        var names = SCBlockNames.For(a);
        var previous = b.AllBlocks.ToDictionary(x => x.Key);
        // Box and party contents are reported per Pokémon above; repeating them as raw
        // blocks would bury the useful rows under two enormous ones.
        var entityBlocks = new HashSet<string> { "BoxInfo", "PartyInfo", "Box", "Party" };

        foreach (var block in a.AllBlocks)
        {
            if (token.IsCancellationRequested)
                return;
            if (!previous.TryGetValue(block.Key, out var was))
                continue;
            if (block.Type == was.Type && block.Data.SequenceEqual(was.Data))
                continue;

            var name = names.GetValueOrDefault(block.Key);
            if (name is not null && entityBlocks.Contains(name))
                continue;

            changes.Add(new SaveChange(ChangeKind.SaveData,
                name ?? $"Block {block.Key:X8}",
                DescribeBlock(block, was), null));
        }
    }

    private static string DescribeBlock(SCBlock now, SCBlock before)
    {
        if (now.Type != before.Type && now.Type.IsBoolean() && before.Type.IsBoolean())
            return before.Type == SCTypeCode.Bool2 ? "on → off" : "off → on";
        try
        {
            if (now.HasValue() && before.HasValue())
                return $"{before.GetValue()} → {now.GetValue()}";
        }
        catch
        {
            // Fall through to the byte summary.
        }
        return $"{now.Data.Length:N0} bytes changed";
    }
}

/// <summary>Whether a change is to a Pokémon or to the rest of the save.</summary>
public enum ChangeKind
{
    Entity,
    SaveData,
}

/// <summary>One difference between the save in memory and the file on disk.</summary>
public sealed record SaveChange(ChangeKind Kind, string Where, string Description, PKM? Entity);
