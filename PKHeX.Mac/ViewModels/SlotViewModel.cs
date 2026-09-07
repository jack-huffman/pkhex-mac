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
    /// <summary>The value of <see cref="Box"/> for party slots.</summary>
    public const int PartyBox = -1;

    /// <summary>
    /// Which box this slot belongs to; <see cref="PartyBox"/> for the party. Settable
    /// because the grid's slot objects are reused as the current box changes.
    /// </summary>
    public int Box { get; internal set; }
    public int Slot { get; }
    public bool IsParty => Box == PartyBox;

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

        var species = strings.SpeciesName(pk);
        var name = pk.Nickname == species ? species : $"{pk.Nickname} ({species})";
        ToolTipText = $"{name}\nLv. {pk.CurrentLevel}{(pk.IsShiny ? " ★" : string.Empty)}";
    }
}
