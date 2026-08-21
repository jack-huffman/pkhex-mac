using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// A reusable set of competitive values — nature, IVs, EVs and optionally a ball.
/// </summary>
/// <remarks>
/// Every field is optional so a preset can be narrow: "perfect IVs" should not also
/// impose a nature, and "Adamant sweeper" should not wipe the IVs someone already
/// bred for. Null means leave alone.
/// </remarks>
public sealed class SpreadPreset
{
    public string Name { get; set; } = "Untitled";

    public int? Nature { get; set; }
    public int? Ball { get; set; }

    /// <summary>Six values in HP, Atk, Def, SpA, SpD, Spe order; nulls are skipped.</summary>
    public int?[] Ivs { get; set; } = new int?[6];
    public int?[] Evs { get; set; } = new int?[6];

    /// <summary>A short description of what the preset will actually change.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Nature is { } nature && (uint)nature < GameInfo.Strings.natures.Length)
                parts.Add(GameInfo.Strings.natures[nature]);
            if (Describe(Evs, "EV") is { } evs)
                parts.Add(evs);
            if (Describe(Ivs, "IV") is { } ivs)
                parts.Add(ivs);
            if (Ball is { } ball && (uint)ball < GameInfo.Strings.balllist.Length)
                parts.Add(GameInfo.Strings.balllist[ball]);
            return parts.Count == 0 ? "changes nothing" : string.Join(" · ", parts);
        }
    }

    private static readonly string[] StatNames = ["HP", "Atk", "Def", "SpA", "SpD", "Spe"];

    private static string? Describe(int?[] values, string kind)
    {
        var set = new List<string>();
        for (int i = 0; i < 6 && i < values.Length; i++)
        {
            if (values[i] is { } v)
                set.Add($"{v} {StatNames[i]}");
        }
        if (set.Count == 0)
            return null;
        // Six identical values reads better as one statement than as six.
        if (set.Count == 6)
        {
            var first = values[0];
            bool uniform = true;
            for (int i = 1; i < 6; i++)
            {
                if (values[i] != first)
                    uniform = false;
            }
            if (uniform)
                return $"all {kind}s {first}";
        }
        return string.Join(" / ", set);
    }

    /// <summary>Applies the preset, returning what it actually changed.</summary>
    public IReadOnlyList<string> ApplyTo(PKM pk)
    {
        var changed = new List<string>();

        if (Nature is { } nature && (int)pk.Nature != nature)
        {
            pk.Nature = (Nature)nature;
            // From Gen 8 the battle-facing nature is separate; a mint would otherwise
            // leave the preset's nature cosmetic.
            if (pk.Format >= 8)
                pk.StatAlignment = (Nature)nature;
            changed.Add("nature");
        }
        if (Ball is { } ball && pk.Ball != ball)
        {
            pk.Ball = (byte)ball;
            changed.Add("ball");
        }

        if (SetSix(Ivs, 31, (i, v) => SetIv(pk, i, v), i => GetIv(pk, i)))
            changed.Add("IVs");
        if (SetSix(Evs, 252, (i, v) => SetEv(pk, i, v), i => GetEv(pk, i)))
            changed.Add("EVs");

        if (changed.Count > 0)
            pk.RefreshChecksum();
        return changed;
    }

    private static bool SetSix(int?[] values, int max, Action<int, int> set, Func<int, int> get)
    {
        bool touched = false;
        for (int i = 0; i < 6 && i < values.Length; i++)
        {
            if (values[i] is not { } v)
                continue;
            v = Math.Clamp(v, 0, max);
            if (get(i) == v)
                continue;
            set(i, v);
            touched = true;
        }
        return touched;
    }

    private static int GetIv(PKM pk, int i) => i switch
    {
        0 => pk.IV_HP, 1 => pk.IV_ATK, 2 => pk.IV_DEF,
        3 => pk.IV_SPA, 4 => pk.IV_SPD, _ => pk.IV_SPE,
    };

    private static void SetIv(PKM pk, int i, int v)
    {
        switch (i)
        {
            case 0: pk.IV_HP = v; break;
            case 1: pk.IV_ATK = v; break;
            case 2: pk.IV_DEF = v; break;
            case 3: pk.IV_SPA = v; break;
            case 4: pk.IV_SPD = v; break;
            default: pk.IV_SPE = v; break;
        }
    }

    private static int GetEv(PKM pk, int i) => i switch
    {
        0 => pk.EV_HP, 1 => pk.EV_ATK, 2 => pk.EV_DEF,
        3 => pk.EV_SPA, 4 => pk.EV_SPD, _ => pk.EV_SPE,
    };

    private static void SetEv(PKM pk, int i, int v)
    {
        switch (i)
        {
            case 0: pk.EV_HP = v; break;
            case 1: pk.EV_ATK = v; break;
            case 2: pk.EV_DEF = v; break;
            case 3: pk.EV_SPA = v; break;
            case 4: pk.EV_SPD = v; break;
            default: pk.EV_SPE = v; break;
        }
    }

    /// <summary>Captures the current values of a Pokémon as a new preset.</summary>
    public static SpreadPreset From(PKM pk, string name) => new()
    {
        Name = name,
        Nature = (int)pk.Nature,
        Ball = pk.Ball,
        Ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE],
        Evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE],
    };

    /// <summary>The presets offered before anyone has saved their own.</summary>
    public static List<SpreadPreset> Defaults() =>
    [
        new() { Name = "Perfect IVs", Ivs = [31, 31, 31, 31, 31, 31] },
        new() { Name = "Physical sweeper", Nature = (int)PKHeX.Core.Nature.Adamant,
                Ivs = [31, 31, 31, null, 31, 31], Evs = [0, 252, 0, 0, 4, 252] },
        new() { Name = "Special sweeper", Nature = (int)PKHeX.Core.Nature.Modest,
                Ivs = [31, null, 31, 31, 31, 31], Evs = [0, 0, 0, 252, 4, 252] },
        new() { Name = "Physical wall", Nature = (int)PKHeX.Core.Nature.Bold,
                Ivs = [31, null, 31, 31, 31, 31], Evs = [252, 0, 252, 0, 4, 0] },
        new() { Name = "Special wall", Nature = (int)PKHeX.Core.Nature.Calm,
                Ivs = [31, null, 31, 31, 31, 31], Evs = [252, 0, 4, 0, 252, 0] },
        new() { Name = "Clear all EVs", Evs = [0, 0, 0, 0, 0, 0] },
    ];
}
