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
/// Mystery Gift database: browses the entire bundled event archive (every
/// generation) as a filterable sprite tile grid, and converts a gift into a
/// Pokémon for the loaded save.
/// </summary>
public partial class GiftsViewModel : ObservableObject
{
    /// <summary>Tiles added per page. The grid is not virtualized, so it fills on demand.</summary>
    private const int PageSize = 100;

    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly List<GiftTileViewModel> _allTiles;
    private List<GiftTileViewModel> _matches = [];
    private GiftTileViewModel? _selectedTile;
    private bool _suppressFilter;

    public GiftsViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;

        // The whole archive, every generation — not just this save's own gifts.
        _allTiles = EncounterEvent.GetAllEvents(sorted: false)
            .Where(g => g.IsEntity)
            .Select(g =>
            {
                // Actually attempt the transfer once up front (~100ms for the whole
                // archive) so "can this save accept it" is exact rather than guessed,
                // and the reason is ready before the user clicks.
                TryConvert(g, sav, out _, out var reason);
                return new GiftTileViewModel(g, SpeciesNameOf(g.Species), reason);
            })
            .ToList();

        GenerationChoices = ["All generations", .. _allTiles.Select(t => t.Generation).Distinct().OrderBy(g => g).Select(g => $"Generation {g}")];
        RebuildGameChoices();

        // Default to this save's own generation: the most relevant slice, and it
        // keeps the first paint small.
        _suppressFilter = true;
        SelectedGenerationIndex = GenerationChoices.IndexOf($"Generation {sav.Generation}") is var i and >= 0 ? i : 0;
        _suppressFilter = false;
        ApplyFilter();
    }

    public ObservableCollection<GiftTileViewModel> Tiles { get; } = [];
    public List<string> GenerationChoices { get; }
    public ObservableCollection<string> GameChoices { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedGenerationIndex;
    [ObservableProperty] private int _selectedGameIndex;
    [ObservableProperty] private bool _shinyOnly;
    [ObservableProperty] private bool _eggsOnly;
    [ObservableProperty] private bool _addableOnly = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _resultSummary = string.Empty;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private string _loadMoreLabel = string.Empty;

    public PKM? Result { get; private set; }

    /// <summary>Raised whenever the converted preview changes (or clears).</summary>
    public Action<PKM?>? PreviewReady { get; set; }

    /// <summary>Raised when a gift cannot be converted, with a human-readable reason.</summary>
    public Action<string>? Blocked { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShinyOnlyChanged(bool value) => ApplyFilter();
    partial void OnEggsOnlyChanged(bool value) => ApplyFilter();
    partial void OnAddableOnlyChanged(bool value) => ApplyFilter();
    partial void OnSelectedGameIndexChanged(int value) => ApplyFilter();

    partial void OnSelectedGenerationIndexChanged(int value)
    {
        // The game list depends on the chosen generation.
        RebuildGameChoices();
        ApplyFilter();
    }

    private int? SelectedGeneration =>
        SelectedGenerationIndex <= 0 || SelectedGenerationIndex >= GenerationChoices.Count
            ? null
            : int.Parse(GenerationChoices[SelectedGenerationIndex].AsSpan("Generation ".Length));

    private void RebuildGameChoices()
    {
        var gen = SelectedGeneration;
        var games = _allTiles
            .Where(t => gen is null || t.Generation == gen)
            .Select(t => t.GameName)
            .Distinct()
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var previous = SelectedGameIndex > 0 && SelectedGameIndex < GameChoices.Count ? GameChoices[SelectedGameIndex] : null;
        _suppressFilter = true;
        GameChoices.Clear();
        GameChoices.Add("All games");
        foreach (var g in games)
            GameChoices.Add(g);
        // Keep the game selection if it still exists under the new generation.
        SelectedGameIndex = previous is not null && GameChoices.IndexOf(previous) is var idx and > 0 ? idx : 0;
        _suppressFilter = false;
    }

    private void ApplyFilter()
    {
        if (_suppressFilter)
            return;

        _selectedTile = null;
        Tiles.Clear();
        Result = null;
        PreviewReady?.Invoke(null);

        var query = SearchText.Trim();
        var gen = SelectedGeneration;
        var game = SelectedGameIndex > 0 && SelectedGameIndex < GameChoices.Count ? GameChoices[SelectedGameIndex] : null;

        _matches = _allTiles.Where(t =>
            (gen is null || t.Generation == gen)
            && (game is null || t.GameName == game)
            && (!ShinyOnly || t.IsShiny)
            && (!EggsOnly || t.IsEgg)
            && (!AddableOnly || t.IsAddable)
            && (query.Length == 0 || t.Matches(query))).ToList();

        ResultSummary = $"{_matches.Count} of {_allTiles.Count} gifts";
        StatusText = _matches.Count == 0
            ? "No gifts match the current filters."
            : "Pick a gift to preview it.";
        LoadNextPage();
    }

    /// <summary>Appends the next page of matching tiles to the grid.</summary>
    [RelayCommand]
    public void LoadNextPage()
    {
        foreach (var tile in _matches.Skip(Tiles.Count).Take(PageSize))
        {
            tile.IsSelected = false;
            Tiles.Add(tile);
        }
        var remaining = _matches.Count - Tiles.Count;
        HasMore = remaining > 0;
        LoadMoreLabel = remaining > 0
            ? $"Load {Math.Min(PageSize, remaining)} more  ({remaining:N0} left)"
            : string.Empty;
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
        Convert(tile);
    }

    [RelayCommand]
    public void ClearFilters()
    {
        _suppressFilter = true;
        ShinyOnly = false;
        EggsOnly = false;
        AddableOnly = true;
        SearchText = string.Empty;
        SelectedGenerationIndex = 0;
        RebuildGameChoices();
        _suppressFilter = false;
        ApplyFilter();
    }

    private void Convert(GiftTileViewModel tile)
    {
        Result = null;
        if (!tile.IsAddable)
        {
            StatusText = tile.BlockedReason;
            Blocked?.Invoke(tile.BlockedReason);
            return;
        }
        if (!TryConvert(tile.Gift, _sav, out var pk, out var reason) || pk is null)
        {
            var message = reason ?? "This gift could not be converted.";
            StatusText = message;
            Blocked?.Invoke(message);
            return;
        }
        Result = pk;
        StatusText = tile.Description;
        PreviewReady?.Invoke(pk);
    }

    /// <summary>
    /// Attempts to bring a gift into the save. Returns true with the entity on
    /// success; false with a human-readable reason otherwise.
    /// </summary>
    private static bool TryConvert(MysteryGift gift, SaveFile sav, out PKM? result, out string? reason)
    {
        result = null;
        reason = null;
        try
        {
            var pk = gift.ConvertToPKM(sav);
            if (pk.GetType() != sav.PKMType)
            {
                pk = EntityConverter.ConvertToType(pk, sav.PKMType, out var res);
                if (pk is null)
                {
                    var game = GameInfo.GetVersionName(sav.Version);
                    var name = (uint)gift.Species < GameInfo.GetStrings("en").specieslist.Length
                        ? GameInfo.GetStrings("en").specieslist[gift.Species]
                        : $"#{gift.Species}";
                    reason = res == EntityConverterResult.IncompatibleSpecies
                        ? $"This {name} comes from {gift.Version} and cannot be transferred into {game}."
                        : $"{name} cannot be brought into {game} ({res}).";
                    return false;
                }
            }
            pk.Heal();
            pk.RefreshChecksum();
            result = pk;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Could not convert this gift: {ex.Message}";
            return false;
        }
    }

    private string SpeciesNameOf(ushort species) =>
        (uint)species < _strings.specieslist.Length ? _strings.specieslist[species] : $"#{species}";
}

/// <summary>One gift tile: sprite, species, event title, and origin.</summary>
public partial class GiftTileViewModel : ObservableObject
{
    public GiftTileViewModel(MysteryGift gift, string speciesName, string? blockedReason)
    {
        var isAddable = blockedReason is null;
        BlockedReason = blockedReason ?? string.Empty;
        Gift = gift;
        SpeciesName = speciesName;
        CardTitle = gift.CardTitle.Replace('　', ' ').Trim();
        Generation = gift.Generation;
        GameName = gift.Version.ToString();
        IsShiny = gift.IsShiny;
        IsEgg = gift.IsEgg;
        IsAddable = isAddable;
        LevelText = gift.IsEgg ? "Egg" : $"Lv. {gift.Level}";
        OriginText = $"Gen {Generation} · {GameName}";
        Sprite = SpriteService.GetSprite(gift.Species, gift.Form, gift.Gender, 0, gift.IsShiny, gift.Context);
        ShinyOverlay = gift.IsShiny ? SpriteService.GetOverlay("rare_icon") : null;
        Description = string.IsNullOrWhiteSpace(CardTitle) ? speciesName : $"{speciesName} — {CardTitle}";
        ToolTipText = $"{Description}\n{LevelText} · {OriginText}"
                      + (isAddable ? string.Empty : "\nCannot be transferred into this save");
    }

    public MysteryGift Gift { get; }
    public string SpeciesName { get; }
    public string CardTitle { get; }
    public string Description { get; }
    public string LevelText { get; }
    public string OriginText { get; }
    public string GameName { get; }
    public string ToolTipText { get; }
    public byte Generation { get; }
    public bool IsShiny { get; }
    public bool IsEgg { get; }
    public bool IsAddable { get; }
    public string BlockedReason { get; }
    public Bitmap? Sprite { get; }
    public Bitmap? ShinyOverlay { get; }

    /// <summary>Gifts this save cannot accept are dimmed rather than hidden.</summary>
    public double TileOpacity => IsAddable ? 1.0 : 0.45;

    [ObservableProperty] private bool _isSelected;

    public bool Matches(string query) =>
        SpeciesName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || CardTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
        || GameName.Contains(query, StringComparison.OrdinalIgnoreCase);
}
