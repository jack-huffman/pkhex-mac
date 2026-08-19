using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Mystery Gift database: browse the bundled event gift archive as a sprite
/// tile grid, filter it, and convert a gift into a Pokémon.
/// </summary>
public partial class GiftsViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly List<MysteryGift> _all;
    private List<MysteryGift> _filtered = [];
    private GiftTileViewModel? _selectedTile;

    public GiftsViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        _all = EncounterEvent.GetAllEvents(sorted: false)
            .Where(g => g.Context == sav.Context && g.IsEntity)
            .ToList();
        ApplyFilter();
    }

    public ObservableCollection<GiftTileViewModel> Tiles { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _shinyOnly;
    [ObservableProperty] private bool _eggsOnly;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _resultSummary = string.Empty;

    public PKM? Result { get; private set; }

    /// <summary>Raised whenever the converted preview changes (or clears).</summary>
    public Action<PKM?>? PreviewReady { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShinyOnlyChanged(bool value) => ApplyFilter();
    partial void OnEggsOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        _selectedTile = null;
        Tiles.Clear();
        Result = null;
        PreviewReady?.Invoke(null);

        var query = SearchText.Trim();
        _filtered = _all.Where(g =>
                (!ShinyOnly || g.IsShiny)
                && (!EggsOnly || g.IsEgg)
                && (query.Length == 0 || Describe(g).Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var g in _filtered)
            Tiles.Add(new GiftTileViewModel(g, SpeciesName(g.Species), _strings));

        ResultSummary = _all.Count == 0
            ? "No event gifts are available for this save's game."
            : $"{_filtered.Count} of {_all.Count} gifts";
        StatusText = _filtered.Count == 0 && _all.Count > 0
            ? "No gifts match the current filters."
            : "Pick a gift to preview it.";
    }

    [RelayCommand]
    public void SelectTile(GiftTileViewModel? tile)
    {
        if (_selectedTile is not null)
            _selectedTile.IsSelected = false;
        _selectedTile = tile;
        if (tile is null)
        {
            PreviewReady?.Invoke(null);
            return;
        }
        tile.IsSelected = true;
        Convert(tile.Gift);
    }

    [RelayCommand]
    public void ClearFilters()
    {
        ShinyOnly = false;
        EggsOnly = false;
        SearchText = string.Empty;
    }

    private void Convert(MysteryGift gift)
    {
        Result = null;
        try
        {
            var pk = gift.ConvertToPKM(_sav);
            if (pk.GetType() != _sav.PKMType)
            {
                pk = EntityConverter.ConvertToType(pk, _sav.PKMType, out var res);
                if (pk is null)
                {
                    StatusText = $"Conversion failed: {res}";
                    PreviewReady?.Invoke(null);
                    return;
                }
            }
            pk.Heal();
            pk.RefreshChecksum();
            Result = pk;
            StatusText = Describe(gift);
            PreviewReady?.Invoke(pk);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not convert this gift: {ex.Message}";
            PreviewReady?.Invoke(null);
        }
    }

    private string SpeciesName(ushort species) =>
        (uint)species < _strings.specieslist.Length ? _strings.specieslist[species] : $"#{species}";

    private string Describe(MysteryGift g)
    {
        var title = g.CardTitle.Replace('　', ' ').Trim();
        var species = SpeciesName(g.Species);
        return string.IsNullOrWhiteSpace(title) ? species : $"{species} — {title}";
    }
}

/// <summary>One gift tile: sprite, species name, and event title.</summary>
public partial class GiftTileViewModel : ObservableObject
{
    public GiftTileViewModel(MysteryGift gift, string speciesName, GameStrings strings)
    {
        Gift = gift;
        SpeciesName = speciesName;
        CardTitle = gift.CardTitle.Replace('　', ' ').Trim();
        IsShiny = gift.IsShiny;
        IsEgg = gift.IsEgg;
        LevelText = gift.IsEgg ? "Egg" : $"Lv. {gift.Level}";
        Sprite = SpriteService.GetSprite(gift.Species, gift.Form, gift.Gender, 0, gift.IsShiny, gift.Context);
        ShinyOverlay = gift.IsShiny ? SpriteService.GetOverlay("rare_icon") : null;
        ToolTipText = string.IsNullOrWhiteSpace(CardTitle)
            ? $"{speciesName} · {LevelText}"
            : $"{speciesName} · {LevelText}\n{CardTitle}";
    }

    public MysteryGift Gift { get; }
    public string SpeciesName { get; }
    public string CardTitle { get; }
    public string LevelText { get; }
    public string ToolTipText { get; }
    public bool IsShiny { get; }
    public bool IsEgg { get; }
    public Bitmap? Sprite { get; }
    public Bitmap? ShinyOverlay { get; }

    [ObservableProperty] private bool _isSelected;
}
