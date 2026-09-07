using PKHeX.Core;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Realistic entities for tests. A Pokémon built by hand from a blank slot has zeros where
/// the games write identifiers, which is enough to confuse format detection; one generated
/// from an encounter — the way the Add Pokémon database does it — looks like the real thing.
/// </summary>
internal static class Fixtures
{
    /// <summary>A legal Pokémon of the given species for this save, with a real trainer behind it.</summary>
    public static PKM Legal(SaveFile sav, Species species)
    {
        if (string.IsNullOrEmpty(sav.OT))
        {
            sav.OT = "Tester";
            sav.TID16 = 12345;
            sav.SID16 = 54321;
        }
        var blank = sav.BlankPKM;
        blank.Species = (ushort)species;
        blank.Gender = blank.GetSaneGender();
        // The generator also offers eggs of the pre-evolution; ask for the species itself.
        var encounter = EncounterMovesetGenerator.GenerateEncounters(blank, sav, ReadOnlyMemory<ushort>.Empty)
            .First(e => e.Species == (ushort)species);
        var pk = encounter.ConvertToPKM(sav);
        pk.RefreshChecksum();
        return pk;
    }
}
