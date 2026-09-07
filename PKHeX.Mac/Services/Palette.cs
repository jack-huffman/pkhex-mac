using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PKHeX.Mac.Services;

/// <summary>
/// The semantic colours the analysis and editor views share, defined once.
/// </summary>
/// <remarks>
/// These are deliberately not theme resources: they colour <em>data</em> (a weakness, a
/// raised stat, a severity) rather than chrome, and the same hue has to mean the same
/// thing in every view that shows it. Chrome colours live in <c>App.axaml</c>.
/// </remarks>
public static class Palette
{
    /// <summary>Neutral labels and de-emphasised text.</summary>
    public static readonly IBrush Muted = Brush("#8FA6B8");

    /// <summary>Something is fine: a covered type, a resisted hit, a neutral stat bar.</summary>
    public static readonly IBrush Good = Brush("#6FAFB8");

    /// <summary>Something needs attention: a weakness, a lowered verdict, a raised-stat bar.</summary>
    public static readonly IBrush Bad = Brush("#E5776D");

    /// <summary>The worst tier: a 4× weakness, a conclusive audit finding.</summary>
    public static readonly IBrush Severe = Brush("#FF6B5B");

    /// <summary>A strong but not conclusive audit finding.</summary>
    public static readonly IBrush Warning = Brush("#E0A33D");

    /// <summary>An audit finding worth a look.</summary>
    public static readonly IBrush Notable = Brush("#D8C05A");

    // ---- Stat editor and preview ----
    public static readonly IBrush RaisedStat = Brush("#FF8A80");
    public static readonly IBrush LoweredStat = Brush("#82B1FF");
    public static readonly IBrush LoweredStatBar = Brush("#5E8FD0");
    public static readonly IBrush NatureUp = Brush("#5FD07A");
    public static readonly IBrush NatureDown = Brush("#FF9F43");

    // ---- Type matrix ----
    public static readonly IBrush Quarter = Brush("#4A78BC");
    public static readonly IBrush Resist = Brush("#6FCF97");
    public static readonly IBrush Immune = Brush("#7FD4C1");
    public static readonly IBrush SevereFill = Brush("#3A1E1B");
    public static readonly IBrush BadFill = Brush("#2E1B19");
    public static readonly IBrush ResistFill = Brush("#182430");
    public static readonly IBrush ImmuneFill = Brush("#16292B");

    // ---- Move damage categories ----
    public static readonly IBrush Physical = Brush("#E0733D");
    public static readonly IBrush Special = Brush("#5C8FD6");
    public static readonly IBrush Status = Brush("#8E8E93");

    private static ImmutableSolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
