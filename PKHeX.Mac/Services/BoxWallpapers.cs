using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// The wallpaper choices a save offers for its boxes.
/// </summary>
/// <remarks>
/// Each generation exposes a different number of wallpapers, and only some ship
/// names for them. Sword/Shield and Scarlet/Violet have none in PKHeX's string
/// tables, so they get numbered entries — the same fallback PKHeX itself uses.
/// </remarks>
public static class BoxWallpapers
{
    public static bool IsSupported(SaveFile sav) => sav is IBoxDetailWallpaper && GetCount(sav) > 0;

    /// <summary>How many wallpapers this save's game knows about.</summary>
    public static int GetCount(SaveFile sav) => sav switch
    {
        SAV3 or SAV3RSBox => 16,
        SAV8BS => 32,
        SAV8SWSH => 19,
        SAV9SV or SAV9ZA => 20,
        { Generation: 4 or 5 or 6 } => 24,
        { Generation: 7 } => 16,
        { Generation: 8 } => 19,
        { Generation: 9 } => 20,
        _ => 0,
    };

    /// <summary>Named where the game strings supply names, numbered otherwise.</summary>
    public static IReadOnlyList<string> GetChoices(SaveFile sav, GameStrings strings)
    {
        var count = GetCount(sav);
        if (count == 0)
            return [];

        var named = sav is SAV3 or SAV3RSBox or SAV8BS || sav.Generation is 4 or 5 or 6 or 7;
        var names = strings.wallpapernames;
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(named && i < names.Length && !string.IsNullOrWhiteSpace(names[i])
                ? names[i]
                : $"Wallpaper {i + 1}");
        }
        return result;
    }

    public static int Get(SaveFile sav, int box) =>
        sav is IBoxDetailWallpaper wp ? Math.Clamp(wp.GetBoxWallpaper(box), 0, Math.Max(0, GetCount(sav) - 1)) : 0;

    public static void Set(SaveFile sav, int box, int value)
    {
        if (sav is IBoxDetailWallpaper wp)
            wp.SetBoxWallpaper(box, value);
    }
}
