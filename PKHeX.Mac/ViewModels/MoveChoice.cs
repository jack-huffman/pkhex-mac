using System.Collections.Generic;
using Avalonia.Media;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// A move as shown in the editor's pickers: name, type, damage category, power,
/// accuracy and PP.
/// </summary>
/// <remarks>
/// Type and PP come from PKHeX.Core (which is generation-aware); power, accuracy,
/// category and the description come from the bundled supplementary table.
/// </remarks>
public sealed class MoveChoice
{
    private MoveChoice(int value, string name, EntityContext context, GameStrings strings)
    {
        Value = value;
        Name = name;
        if (value <= 0)
        {
            TypeName = string.Empty;
            IsRealMove = false;
            return;
        }

        IsRealMove = true;
        var move = (ushort)value;
        var typeId = MoveInfo.GetType(move, context);
        TypeName = (uint)typeId < strings.types.Length ? strings.types[typeId] : string.Empty;
        TypeBrush = TypePalette.GetBrush(typeId);
        TypeIcon = TypeIconService.Get(typeId);
        HasTypeIcon = TypeIcon is not null;
        Pp = MoveInfo.GetPP(context, move);

        var facts = MoveDataService.Get(move);
        CategoryKind = facts.Category;
        PowerText = facts.PowerText;
        AccuracyText = facts.AccuracyText;
        CategoryLabel = facts.CategoryLabel;
        CategoryBrush = MoveDataService.BrushFor(facts.Category);
        Description = facts.Description;
        HasCategory = CategoryLabel.Length > 0;
    }

    public int Value { get; }
    public string Name { get; }
    public string TypeName { get; }
    public IBrush? TypeBrush { get; }
    public Avalonia.Media.IImage? TypeIcon { get; }
    public bool HasTypeIcon { get; }
    public string CategoryLabel { get; } = string.Empty;
    public MoveDataService.Category CategoryKind { get; } = MoveDataService.Category.Unknown;
    public IBrush? CategoryBrush { get; }
    public string PowerText { get; } = "—";
    public string AccuracyText { get; } = "—";
    public string Description { get; } = string.Empty;
    public byte Pp { get; }
    public bool IsRealMove { get; }
    public bool HasCategory { get; }

    /// <summary>"55 power · 100% acc · 15 PP" — the summary shown beside an assigned move.</summary>
    public string StatLine => !IsRealMove
        ? string.Empty
        : $"{PowerText} pow · {AccuracyText}% acc · {Pp} PP";

    /// <summary>Builds picker entries for every move the save allows.</summary>
    public static List<MoveChoice> Build(IReadOnlyList<ComboItem> source, EntityContext context, GameStrings strings)
    {
        var list = new List<MoveChoice>(source.Count);
        foreach (var item in source)
            list.Add(new MoveChoice(item.Value, item.Text, context, strings));
        return list;
    }
}
