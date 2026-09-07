using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Read/write access to the individual fields of a Scarlet/Violet Pokédex entry —
/// registration state, per-form seen/obtained flags, genders seen, and shiny.
/// </summary>
/// <remarks>
/// Scarlet/Violet stores the dex in two differently-shaped blocks: the base game's
/// Paldea entries (0x18 bytes: a single state value plus form flags) and the DLC's
/// Kitakami entries (0x20 bytes: separate heard/seen/obtained form bitfields and
/// per-region display data). Both are <c>ref struct</c>s, so nothing can be cached —
/// every accessor re-resolves the owning block and re-fetches the entry.
/// </remarks>
public static class Dex9Detail
{
    /// <summary>Which block holds a species, if any.</summary>
    public enum Block { None, Paldea, Kitakami }

    public static Block GetBlock(SAV9SV sv, ushort species)
    {
        var (group, index) = sv.Zukan.GetDexIndex(species);
        if (index == 0)
            return Block.None;
        // Group 1 = Paldea; groups 2 (Kitakami) and 3 (Blueberry) share the DLC block.
        return group == 1 ? Block.Paldea : Block.Kitakami;
    }

    public static bool IsSupported(SaveFile sav, ushort species) =>
        sav is SAV9SV sv && GetBlock(sv, species) != Block.None;

    /// <summary>Whether this entry exposes language-obtained flags (Paldea entries only).</summary>
    public static bool HasLanguageFlags(SAV9SV sv, ushort species) => GetBlock(sv, species) == Block.Paldea;

    /// <summary>
    /// Paldea entries store one form bitfield ("seen") and a single overall
    /// caught state, so per-form "obtained" only exists on DLC entries.
    /// </summary>
    public static bool HasPerFormObtained(SAV9SV sv, ushort species) => GetBlock(sv, species) == Block.Kitakami;

    // ---- Seen / caught ----

    public static bool GetSeen(SAV9SV sv, ushort species) => sv.Zukan.GetSeen(species);
    public static bool GetCaught(SAV9SV sv, ushort species) => sv.Zukan.GetCaught(species);

    // ---- Per-form flags ----

    public static bool GetFormSeen(SAV9SV sv, ushort species, byte form) => GetBlock(sv, species) switch
    {
        Block.Paldea => sv.Zukan.DexPaldea.Get(species).GetIsFormSeen(form),
        Block.Kitakami => sv.Zukan.DexKitakami.Get(species).GetSeenForm(form),
        _ => false,
    };

    public static void SetFormSeen(SAV9SV sv, ushort species, byte form, bool value)
    {
        switch (GetBlock(sv, species))
        {
            case Block.Paldea:
                sv.Zukan.DexPaldea.Get(species).SetIsFormSeen(form, value);
                break;
            case Block.Kitakami:
                sv.Zukan.DexKitakami.Get(species).SetSeenForm(form, value);
                break;
        }
    }

    public static bool GetFormObtained(SAV9SV sv, ushort species, byte form) => GetBlock(sv, species) switch
    {
        // Paldea has no per-form obtained bit — the entry's caught state covers all forms.
        Block.Paldea => sv.Zukan.DexPaldea.Get(species).IsCaught,
        Block.Kitakami => sv.Zukan.DexKitakami.Get(species).GetObtainedForm(form),
        _ => false,
    };

    public static void SetFormObtained(SAV9SV sv, ushort species, byte form, bool value)
    {
        switch (GetBlock(sv, species))
        {
            case Block.Paldea:
                sv.Zukan.DexPaldea.Get(species).SetCaught(value);
                break;
            case Block.Kitakami:
                sv.Zukan.DexKitakami.Get(species).SetObtainedForm(form, value);
                break;
        }
    }

    // ---- Genders seen (bit 0 male, 1 female, 2 genderless) ----

    public static bool GetGenderSeen(SAV9SV sv, ushort species, byte gender) => GetBlock(sv, species) switch
    {
        Block.Paldea => sv.Zukan.DexPaldea.Get(species).GetIsGenderSeen(gender),
        Block.Kitakami => (sv.Zukan.DexKitakami.Get(species).FlagsGenderSeen & (1 << gender)) != 0,
        _ => false,
    };

    public static void SetGenderSeen(SAV9SV sv, ushort species, byte gender, bool value)
    {
        switch (GetBlock(sv, species))
        {
            case Block.Paldea:
                sv.Zukan.DexPaldea.Get(species).SetIsGenderSeen(gender, value);
                break;
            case Block.Kitakami:
                {
                    var entry = sv.Zukan.DexKitakami.Get(species);
                    var mask = entry.FlagsGenderSeen;
                    entry.FlagsGenderSeen = value
                        ? (byte)(mask | (1 << gender))
                        : (byte)(mask & ~(1 << gender));
                    break;
                }
        }
    }

    // ---- Shiny ----

    public static bool GetShinySeen(SAV9SV sv, ushort species) => GetBlock(sv, species) switch
    {
        Block.Paldea => sv.Zukan.DexPaldea.Get(species).GetSeenIsShiny(),
        Block.Kitakami => (sv.Zukan.DexKitakami.Get(species).FlagsShinySeen & 0b10) != 0,
        _ => false,
    };

    public static void SetShinySeen(SAV9SV sv, ushort species, bool value)
    {
        switch (GetBlock(sv, species))
        {
            case Block.Paldea:
                sv.Zukan.DexPaldea.Get(species).SetSeenIsShiny(value);
                break;
            case Block.Kitakami:
                {
                    var entry = sv.Zukan.DexKitakami.Get(species);
                    var mask = entry.FlagsShinySeen;
                    entry.FlagsShinySeen = value ? (byte)(mask | 0b10) : (byte)(mask & ~0b10);
                    break;
                }
        }
    }

    // ---- Languages (Paldea entries only) ----

    public static bool GetLanguage(SAV9SV sv, ushort species, int langIndex) =>
        GetBlock(sv, species) == Block.Paldea && sv.Zukan.DexPaldea.GetIsLanguageIndexObtained(species, langIndex);

    public static void SetLanguage(SAV9SV sv, ushort species, int langIndex, bool value)
    {
        if (GetBlock(sv, species) == Block.Paldea)
            sv.Zukan.DexPaldea.SetIsLanguageIndexObtained(species, langIndex, value);
    }
}
