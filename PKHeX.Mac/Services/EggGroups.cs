using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Names for the egg group indices held in <see cref="IPersonalEgg"/>.
/// </summary>
/// <remarks>
/// PKHeX ships no name table for these — the legality engine only needs the numbers —
/// so the mapping here was confirmed against known species: Bulbasaur 1/7, Pikachu
/// 5/6, Krabby 9, Magikarp 12/14, Ditto 13, Mewtwo 15.
/// </remarks>
public static class EggGroups
{
    /// <summary>Species in this group cannot breed at all.</summary>
    public const int Undiscovered = 15;

    /// <summary>Ditto's own group; it pairs with anything that can breed.</summary>
    public const int Ditto = 13;

    private static readonly string[] Names =
    [
        "—",            // 0, unused
        "Monster",      // 1
        "Water 1",      // 2
        "Bug",          // 3
        "Flying",       // 4
        "Field",        // 5
        "Fairy",        // 6
        "Grass",        // 7
        "Human-Like",   // 8
        "Water 3",      // 9
        "Mineral",      // 10
        "Amorphous",    // 11
        "Water 2",      // 12
        "Ditto",        // 13
        "Dragon",       // 14
        "Undiscovered", // 15
    ];

    public static string GetName(int group) =>
        (uint)group < Names.Length ? Names[group] : $"Group {group}";

    /// <summary>A readable description of a species' groups, collapsing duplicates.</summary>
    public static string Describe(IPersonalEgg pi) =>
        pi.EggGroup1 == pi.EggGroup2
            ? GetName(pi.EggGroup1)
            : $"{GetName(pi.EggGroup1)} · {GetName(pi.EggGroup2)}";

    public static bool CanBreed(IPersonalEgg pi) =>
        pi.EggGroup1 != Undiscovered && pi.EggGroup2 != Undiscovered;
}
