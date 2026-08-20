using System.Collections.Generic;
using Avalonia.Media;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// A nature as shown in the editor: its name plus the stat it raises and lowers,
/// e.g. "Lonely  ↑ATK  ↓DEF".
/// </summary>
public sealed class NatureChoice
{
    /// <summary>Stat short names in the order players read them (no HP — natures never touch it).</summary>
    private static readonly string[] Short = ["ATK", "DEF", "SATK", "SDEF", "SPE"];

    /// <summary>
    /// The games store natures as <c>raised * 5 + lowered</c> over the internal stat
    /// order (Atk, Def, Spe, SpA, SpD). This maps that internal index onto the
    /// display order above.
    /// </summary>
    private static readonly int[] InternalToDisplay = [0, 1, 4, 2, 3];

    public static readonly IBrush UpBrush = new SolidColorBrush(Color.Parse("#5FD07A"));
    public static readonly IBrush DownBrush = new SolidColorBrush(Color.Parse("#FF9F43"));

    private NatureChoice(int value, string name, int upDisplay, int downDisplay, bool neutral)
    {
        Value = value;
        Name = name;
        IsNeutral = neutral;
        UpLabel = neutral ? string.Empty : $"↑ {Short[upDisplay]}";
        DownLabel = neutral ? string.Empty : $"↓ {Short[downDisplay]}";
        NeutralLabel = neutral ? "no change" : string.Empty;
        UpDisplay = upDisplay;
        DownDisplay = downDisplay;
    }

    public int Value { get; }
    public string Name { get; }
    public string UpLabel { get; }
    public string DownLabel { get; }
    public string NeutralLabel { get; }
    public bool IsNeutral { get; }
    public bool HasModifiers => !IsNeutral;
    public int UpDisplay { get; }
    public int DownDisplay { get; }

    /// <summary>
    /// Builds the nature list grouped by the stat raised, then by the stat lowered —
    /// so all the +ATK natures sit together (−DEF, −SATK, −SDEF, −SPE), then +DEF,
    /// and so on. Neutral natures land at the end.
    /// </summary>
    public static List<NatureChoice> Build(IReadOnlyList<string> natureNames)
    {
        var all = new List<NatureChoice>(25);
        for (int value = 0; value < 25; value++)
        {
            if ((uint)value >= natureNames.Count)
                break;
            var (up, dn) = ((Nature)value).GetNatureModification();
            var neutral = up == dn;
            all.Add(new NatureChoice(
                value,
                natureNames[value],
                InternalToDisplay[up],
                InternalToDisplay[dn],
                neutral));
        }

        all.Sort((a, b) =>
        {
            // Neutral natures after everything else.
            if (a.IsNeutral != b.IsNeutral)
                return a.IsNeutral ? 1 : -1;
            if (a.IsNeutral)
                return a.Value.CompareTo(b.Value);
            var byUp = a.UpDisplay.CompareTo(b.UpDisplay);
            return byUp != 0 ? byUp : a.DownDisplay.CompareTo(b.DownDisplay);
        });
        return all;
    }
}
