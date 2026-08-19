using System.Collections.Generic;
using Avalonia.Media;

namespace PKHeX.Mac.Services;

/// <summary>
/// Brand colors for Pokémon types, indexed by the games' internal type ids
/// (the same order as <c>GameStrings.types</c>).
/// </summary>
public static class TypePalette
{
    private static readonly Color[] Colors =
    [
        Color.Parse("#A8A77A"), // 0  Normal
        Color.Parse("#C22E28"), // 1  Fighting
        Color.Parse("#A98FF3"), // 2  Flying
        Color.Parse("#A33EA1"), // 3  Poison
        Color.Parse("#E2BF65"), // 4  Ground
        Color.Parse("#B6A136"), // 5  Rock
        Color.Parse("#A6B91A"), // 6  Bug
        Color.Parse("#735797"), // 7  Ghost
        Color.Parse("#B7B7CE"), // 8  Steel
        Color.Parse("#EE8130"), // 9  Fire
        Color.Parse("#6390F0"), // 10 Water
        Color.Parse("#7AC74C"), // 11 Grass
        Color.Parse("#F7D02C"), // 12 Electric
        Color.Parse("#F95587"), // 13 Psychic
        Color.Parse("#96D9D6"), // 14 Ice
        Color.Parse("#6F35FC"), // 15 Dragon
        Color.Parse("#705746"), // 16 Dark
        Color.Parse("#D685AD"), // 17 Fairy
        Color.Parse("#40B5A5"), // 18 Stellar
    ];

    private static readonly Dictionary<int, IBrush> Cache = new();

    public static IBrush GetBrush(int typeId)
    {
        if (Cache.TryGetValue(typeId, out var brush))
            return brush;
        var color = (uint)typeId < Colors.Length ? Colors[typeId] : Color.Parse("#8E8E93");
        brush = new SolidColorBrush(color);
        Cache[typeId] = brush;
        return brush;
    }
}
