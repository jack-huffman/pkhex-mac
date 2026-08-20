using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Uniform read/write access to a save's Pokédex.
/// </summary>
/// <remarks>
/// The generation-specific dex blocks (<c>Zukan6XY</c>, <c>Zukan8</c>, <c>Zukan9</c>, …)
/// all derive from a <em>generic</em> base, so there is no shared interface to bind to.
/// Older saves override <see cref="SaveFile.SetSeen"/>/<see cref="SaveFile.SetCaught"/>
/// directly, while Gen 6+ saves do not — writing those requires the dex block's
/// per-species helpers. This adapter hides that split behind one API.
/// </remarks>
public static class DexAccessor
{
    public static bool IsSupported(SaveFile sav) => sav.HasPokeDex;

    public static bool GetSeen(SaveFile sav, ushort species) => sav.GetSeen(species);

    public static bool GetCaught(SaveFile sav, ushort species) => sav.GetCaught(species);

    /// <summary>
    /// Records a species as fully registered (every form/gender it needs), or clears it.
    /// </summary>
    public static void SetEntry(SaveFile sav, ushort species, bool value, bool shinyToo = false)
    {
        switch (sav)
        {
            case SAV9SV s9:
                Apply(s9.Zukan, species, value, shinyToo);
                break;
            case SAV9ZA za:
                Apply(za.Zukan, species, value, shinyToo);
                break;
            case SAV8SWSH s8:
                Apply(s8.Zukan, species, value, shinyToo);
                break;
            case SAV8BS bs:
                Apply(bs.Zukan, species, value, shinyToo);
                break;
            case SAV7b gg:
                Apply(gg.Zukan, species, value, shinyToo);
                break;
            default:
                // Gen 1-6 override SaveFile.SetSeen/SetCaught directly, so the
                // virtuals are the correct path for them.
                sav.SetSeen(species, value);
                sav.SetCaught(species, value);
                break;
        }
    }

    /// <summary>Registers or clears the whole dex.</summary>
    public static void SetAll(SaveFile sav, bool value, bool shinyToo = false)
    {
        for (ushort species = 1; species <= sav.MaxSpeciesID; species++)
        {
            if (sav.Personal.IsSpeciesInGame(species))
                SetEntry(sav, species, value, shinyToo);
        }
    }

    private static void Apply<T>(ZukanBase<T> dex, ushort species, bool value, bool shinyToo)
        where T : SaveFile
    {
        if (value)
            dex.SetDexEntryAll(species, shinyToo);
        else
            dex.ClearDexEntryAll(species);
    }
}
