using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// A single storage slot (box or party) shown in the grid.
/// </summary>
public partial class SlotViewModel : ObservableObject
{
    public int Box { get; }      // -1 for party
    public int Slot { get; }
    public bool IsParty => Box < 0;

    [ObservableProperty] private Bitmap? _sprite;
    [ObservableProperty] private Bitmap? _ballSprite;
    [ObservableProperty] private Bitmap? _shinyOverlay;
    [ObservableProperty] private string _toolTipText = string.Empty;
    [ObservableProperty] private string _levelText = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isDragOver;

    public PKM? Pokemon { get; private set; }

    public SlotViewModel(int box, int slot)
    {
        Box = box;
        Slot = slot;
    }

    public void Update(PKM? pk, GameStrings strings)
    {
        Pokemon = pk;
        if (pk is null || pk.Species == 0)
        {
            Sprite = null;
            BallSprite = null;
            ShinyOverlay = null;
            ToolTipText = string.Empty;
            LevelText = string.Empty;
            IsEmpty = true;
            IsShiny = false;
            return;
        }

        IsEmpty = false;
        IsShiny = pk.IsShiny;
        LevelText = $"{pk.CurrentLevel}";
        Sprite = SpriteService.GetPokemonSprite(pk);
        BallSprite = SpriteService.GetBallSprite(pk.Ball);
        ShinyOverlay = pk.IsShiny ? SpriteService.GetOverlay("rare_icon") : null;

        var species = (uint)pk.Species < strings.specieslist.Length ? strings.specieslist[pk.Species] : $"#{pk.Species}";
        var name = pk.Nickname == species ? species : $"{pk.Nickname} ({species})";
        ToolTipText = $"{name}\nLv. {pk.CurrentLevel}{(pk.IsShiny ? " ★" : string.Empty)}";
    }
}

/// <summary>A Pokémon found by the cross-box search, with where it lives.</summary>
public sealed class SearchHitViewModel
{
    public SearchHitViewModel(int box, int slot, string species, string boxName, PKM pk, GameStrings strings)
    {
        Box = box;
        Slot = slot;
        Species = species;
        Location = $"{boxName} · slot {slot + 1}";
        Detail = $"Lv. {pk.CurrentLevel}{(pk.IsShiny ? " · ★" : string.Empty)}";
        Sprite = SpriteService.GetPokemonSprite(pk);
    }

    public int Box { get; }
    public int Slot { get; }
    public string Species { get; }
    public string Location { get; }
    public string Detail { get; }
    public Avalonia.Media.Imaging.Bitmap? Sprite { get; }
}
