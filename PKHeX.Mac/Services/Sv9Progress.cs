using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Resolves named Scarlet/Violet progress blocks — gym/titan/Team Star clears and
/// raid unlocks — and reads or writes them.
/// </summary>
/// <remarks>
/// Two naming worlds meet here. The game's own block names (WEVT_*, FSYS_*) hash
/// directly to their keys via FNV-1a, which is how the game looks them up. PKHeX's
/// K* labels are its own invented identifiers, so they only resolve through its
/// block metadata. This resolver tries the hash first, then the metadata map.
/// </remarks>
public sealed class Sv9Progress
{
    private readonly Dictionary<uint, SCBlock> _byKey = [];
    private readonly Dictionary<string, SCBlock> _byName = new(StringComparer.Ordinal);

    public Sv9Progress(SaveFile sav)
    {
        if (sav is not ISCBlockArray array)
            return;
        foreach (var block in array.AllBlocks)
            _byKey[block.Key] = block;

        // PKHeX's own labels, for blocks whose game-side name we don't know.
        try
        {
            SCBlockMetadata? meta = sav switch
            {
                SAV9SV sv => new SCBlockMetadata(sv.Blocks, [], []),
                SAV9ZA za => new SCBlockMetadata(za.Blocks, [], []),
                _ => null,
            };
            if (meta is null)
                return;
            foreach (var block in array.AllBlocks)
            {
                var name = meta.GetBlockName(block, out _);
                if (name is not null)
                    _byName[name] = block;
            }
        }
        catch
        {
            // Metadata is optional; hashed names still resolve.
        }
    }

    public SCBlock? Find(string blockName)
    {
        var key = (uint)FnvHash.HashFnv1a_64(blockName);
        if (_byKey.TryGetValue(key, out var byHash))
            return byHash;
        return _byName.TryGetValue(blockName, out var byLabel) ? byLabel : null;
    }

    // ---- Boolean flags ----

    public bool GetFlag(string blockName) => Find(blockName) is { Type: SCTypeCode.Bool2 };

    public bool SetFlag(string blockName, bool value)
    {
        if (Find(blockName) is not { } b || !b.Type.IsBoolean())
            return false;
        b.ChangeBooleanType(value ? SCTypeCode.Bool2 : SCTypeCode.Bool1);
        return true;
    }

    // ---- Integer values ----

    public int GetInt(string blockName) =>
        Find(blockName) is { } b && b.HasValue() ? Convert.ToInt32(b.GetValue()) : 0;

    public bool SetInt(string blockName, int value)
    {
        if (Find(blockName) is not { } b || !b.HasValue())
            return false;
        try
        {
            b.SetValue(b.Type switch
            {
                SCTypeCode.Byte => (byte)value,
                SCTypeCode.UInt16 => (ushort)value,
                SCTypeCode.UInt32 => (uint)value,
                SCTypeCode.UInt64 => (ulong)value,
                SCTypeCode.SByte => (sbyte)value,
                SCTypeCode.Int16 => (short)value,
                SCTypeCode.Int64 => (long)value,
                _ => value,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Exists(string blockName) => Find(blockName) is not null;

    // =====================================================================
    // Known progress blocks
    // =====================================================================

    /// <summary>
    /// The 18 badge-equivalent clears. Each block stores the <em>order</em> it was
    /// cleared in (1, 2, 3…); zero means not yet cleared.
    /// </summary>
    public static readonly (string Group, string Label, string Block)[] Badges =
    [
        ("Gyms", "Bug · Katy", "WEVT_GYM_MUSHI_CLEAR"),
        ("Gyms", "Grass · Brassius", "WEVT_GYM_KUSA_CLEAR"),
        ("Gyms", "Electric · Iono", "WEVT_GYM_DENKI_CLEAR"),
        ("Gyms", "Water · Kofu", "WEVT_GYM_MIZU_CLEAR"),
        ("Gyms", "Normal · Larry", "WEVT_GYM_NORMAL_CLEAR"),
        ("Gyms", "Ghost · Ryme", "WEVT_GYM_GHOST_CLEAR"),
        ("Gyms", "Psychic · Tulip", "WEVT_GYM_ESPER_CLEAR"),
        ("Gyms", "Ice · Grusha", "WEVT_GYM_KOORI_CLEAR"),

        ("Titans", "Rock · Klawf", "WEVT_NUSHI_IWA_CLEAR"),
        ("Titans", "Flying · Bombirdier", "WEVT_NUSHI_HIKOU_CLEAR"),
        ("Titans", "Steel · Orthworm", "WEVT_NUSHI_HAGANE_CLEAR"),
        ("Titans", "Ground · Great Tusk", "WEVT_NUSHI_JIMEN_CLEAR"),
        ("Titans", "Dragon · Dondozo", "WEVT_NUSHI_DRAGON_CLEAR"),

        ("Team Star", "Dark · Segin", "WEVT_DAN_AKU_CLEAR"),
        ("Team Star", "Fire · Schedar", "WEVT_DAN_HONOO_CLEAR"),
        ("Team Star", "Poison · Navi", "WEVT_DAN_DOKU_CLEAR"),
        ("Team Star", "Fairy · Ruchbah", "WEVT_DAN_FAIRY_CLEAR"),
        ("Team Star", "Fighting · Caph", "WEVT_DAN_KAKUTOU_CLEAR"),
    ];

    /// <summary>Raid tier unlocks. Difficulty N maps to the (N+1)-star tier.</summary>
    public static readonly (string Label, string Block)[] RaidUnlocks =
    [
        ("4-star raids", "KUnlockedRaidDifficulty3"),
        ("5-star raids", "KUnlockedRaidDifficulty4"),
        ("6-star raids", "KUnlockedRaidDifficulty5"),
        ("7-star raids (event)", "KUnlockedRaidDifficulty6"),
    ];

    public static readonly (string Label, string Block)[] RaidCounters =
    [
        ("5-star raids won", "KRaidsWonDifficulty4"),
        ("6-star raids won", "KRaidsWonDifficulty5"),
        ("7-star raids won", "KRaidsWonDifficulty6"),
    ];
}
