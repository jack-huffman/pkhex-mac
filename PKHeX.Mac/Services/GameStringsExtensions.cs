using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Lookups into PKHeX's string tables that degrade to an id instead of throwing.
/// </summary>
/// <remarks>
/// A save can hold a species or item id the bundled strings do not know — a newer game's
/// entity sitting in an older save, or plain corruption — and the UI has to show
/// <em>something</em> rather than crash. Every display path goes through here so that
/// fallback is spelled once.
/// </remarks>
public static class GameStringsExtensions
{
    /// <summary>The species name, or <c>#id</c> when the id is out of range.</summary>
    public static string SpeciesName(this GameStrings strings, int species) =>
        strings.specieslist.NameOrId(species);

    /// <summary>The species name of an entity, or <c>#id</c> when the id is out of range.</summary>
    public static string SpeciesName(this GameStrings strings, PKM pk) =>
        strings.specieslist.NameOrId(pk.Species);

    public static string TypeName(this GameStrings strings, int type) => strings.types.NameOrId(type);

    public static string NatureName(this GameStrings strings, Nature nature) => strings.natures.NameOrId((int)nature);

    public static string MoveName(this GameStrings strings, int move) => strings.movelist.NameOrId(move);

    public static string ItemName(this GameStrings strings, int item) => strings.itemlist.NameOrId(item);

    public static string AbilityName(this GameStrings strings, int ability) => strings.abilitylist.NameOrId(ability);

    /// <summary>An entry from any name table, or <c>#index</c> when the index is out of range.</summary>
    public static string NameOrId(this IReadOnlyList<string> table, int index) =>
        (uint)index < table.Count ? table[index] : $"#{index}";
}
