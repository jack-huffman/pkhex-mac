using System;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// The ride legendary — Koraidon in Scarlet, Miraidon in Violet — as an actual Pokémon.
/// </summary>
/// <remarks>
/// Scarlet and Violet keep it in a reserved box beyond the last one the player can open.
/// The BoxInfo block holds 990 slots, thirty more than the 32 x 30 that BoxCount
/// reports, and the ride sits in the first of those extras. PKHeX excludes the reserved
/// box from BoxCount, which is correct for storage editing but means nothing reaches
/// the Pokémon itself.
///
/// It is a real entity: the save under test holds a level 80 Miraidon in a Poké Ball
/// with the player's own OT, imperfect IVs and a static encounter match — quite
/// distinct from the shiny Cherish Ball event gifts of the same species.
///
/// In game it is unregistered as a ride from the party screen, which makes it battle
/// capable; the stored entity is the same either way.
/// </remarks>
public static class RideLegendary
{
    /// <summary>Index of the reserved slot, immediately past the last reachable box.</summary>
    private static int ReservedIndex(SAV9SV sav) => sav.BoxCount * sav.BoxSlotCount;

    /// <summary>True when this save actually has storage beyond the last open box.</summary>
    public static bool IsSupported(SaveFile sav) =>
        sav is SAV9SV sv && Capacity(sv) > ReservedIndex(sv);

    private static int Capacity(SAV9SV sav) => sav.Blocks.BoxInfo.Data.Length / sav.SIZE_BOXSLOT;

    /// <summary>The stored ride, or null when the slot is empty.</summary>
    public static PKM? Read(SaveFile sav)
    {
        if (sav is not SAV9SV sv || !IsSupported(sv))
            return null;
        var pk = sv.GetDecryptedPKM(SliceOf(sv).ToArray());
        return pk.Species == 0 ? null : pk;
    }

    /// <summary>
    /// Replaces the stored ride. Boxes in this generation hold entities in party
    /// format, so the same writer the box grid uses applies here.
    /// </summary>
    public static bool Write(SaveFile sav, PKM pk)
    {
        if (sav is not SAV9SV sv || !IsSupported(sv))
            return false;
        if (pk.GetType() != sv.PKMType)
            return false;
        pk.RefreshChecksum();
        sav.SetSlotFormatParty(pk, SliceOf(sv));
        return true;
    }

    /// <summary>The reserved slot's bytes inside the box block.</summary>
    private static Span<byte> SliceOf(SAV9SV sav)
    {
        var size = sav.SIZE_BOXSLOT;
        return sav.Blocks.BoxInfo.Data.Slice(ReservedIndex(sav) * size, size);
    }
}
