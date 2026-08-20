using System.Collections.Generic;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>A Poké Ball entry for the editor's ball picker: its sprite and name.</summary>
public sealed class BallChoice
{
    private BallChoice(int value, string name)
    {
        Value = value;
        Name = name;
        Sprite = value > 0 ? SpriteService.GetBallSprite((byte)value) : null;
    }

    public int Value { get; }
    public string Name { get; }
    public Bitmap? Sprite { get; }
    public bool HasSprite => Sprite is not null;

    public static List<BallChoice> Build(IReadOnlyList<ComboItem> source)
    {
        var list = new List<BallChoice>(source.Count);
        foreach (var item in source)
            list.Add(new BallChoice(item.Value, item.Text));
        return list;
    }
}
