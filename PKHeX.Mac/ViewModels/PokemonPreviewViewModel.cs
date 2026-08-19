using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Read-only preview of a Pokémon shown in the inspector while browsing the
/// encounter / mystery gift databases, before it is added to the save.
/// </summary>
public partial class PokemonPreviewViewModel : ObservableObject
{
    private readonly GameStrings _strings;

    public PokemonPreviewViewModel(GameStrings strings) => _strings = strings;

    /// <summary>The previewed entity; cloned when added to the save.</summary>
    public PKM? Current { get; private set; }

    [ObservableProperty] private bool _hasPokemon;
    [ObservableProperty] private Bitmap? _artwork;
    [ObservableProperty] private Bitmap? _ballSprite;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _levelBadge = string.Empty;
    [ObservableProperty] private string _type1Name = string.Empty;
    [ObservableProperty] private string _type2Name = string.Empty;
    [ObservableProperty] private bool _hasType2;
    [ObservableProperty] private IBrush? _type1Brush;
    [ObservableProperty] private IBrush? _type2Brush;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _isLegal;
    [ObservableProperty] private string _legalityReport = string.Empty;

    public Bitmap? ShinyIcon => SpriteService.GetOverlay("rare_icon");

    public void Load(PKM? pk)
    {
        Current = pk;
        if (pk is null || pk.Species == 0)
        {
            HasPokemon = false;
            return;
        }

        HasPokemon = true;
        Artwork = SpriteService.GetPokemonArtwork(pk);
        BallSprite = SpriteService.GetBallSprite(pk.Ball);
        SpeciesName = (uint)pk.Species < _strings.specieslist.Length ? _strings.specieslist[pk.Species] : $"#{pk.Species}";
        LevelBadge = $"Lv. {pk.CurrentLevel}";
        IsShiny = pk.IsShiny;

        var pi = pk.PersonalInfo;
        Type1Name = Name(pi.Type1);
        Type2Name = Name(pi.Type2);
        HasType2 = pi.Type1 != pi.Type2;
        Type1Brush = TypePalette.GetBrush(pi.Type1);
        Type2Brush = TypePalette.GetBrush(pi.Type2);

        var la = new LegalityAnalysis(pk);
        IsLegal = la.Valid;
        LegalityReport = la.Report();
    }

    private string Name(int type) =>
        (uint)type < _strings.types.Length ? _strings.types[type] : $"#{type}";
}
