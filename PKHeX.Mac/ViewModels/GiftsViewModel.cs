using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Mystery Gift database: browse the bundled event gift archive for gifts
/// compatible with this save, and convert one into a Pokémon.
/// </summary>
public partial class GiftsViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private List<MysteryGift> _gifts = [];
    private List<MysteryGift> _filtered = [];

    public GiftsViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        _gifts = EncounterEvent.GetAllEvents(sorted: false)
            .Where(g => g.Context == sav.Context && g.IsEntity)
            .ToList();
        ApplyFilter();
        StatusText = _filtered.Count == 0
            ? "No event gifts are available for this save's game."
            : $"{_filtered.Count} event gift(s) available for this game.";
    }

    public ObservableCollection<string> GiftTitles { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedGiftIndex = -1;
    [ObservableProperty] private string _statusText = string.Empty;

    public PKM? Result { get; private set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        GiftTitles.Clear();
        var query = SearchText.Trim();
        _filtered = query.Length == 0
            ? _gifts
            : _gifts.Where(g => Describe(g).Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var g in _filtered)
            GiftTitles.Add(Describe(g));
    }

    private string Describe(MysteryGift g)
    {
        var species = (uint)g.Species < _strings.specieslist.Length ? _strings.specieslist[g.Species] : $"#{g.Species}";
        var title = g.CardTitle.Replace('　', ' ').Trim();
        return string.IsNullOrWhiteSpace(title) ? species : $"{species} — {title}";
    }

    [RelayCommand]
    public void ConvertSelected()
    {
        Result = null;
        if ((uint)SelectedGiftIndex >= _filtered.Count)
        {
            StatusText = "Select a gift first.";
            return;
        }
        var gift = _filtered[SelectedGiftIndex];
        try
        {
            var pk = gift.ConvertToPKM(_sav);
            if (pk.GetType() != _sav.PKMType)
            {
                pk = EntityConverter.ConvertToType(pk, _sav.PKMType, out var res);
                if (pk is null)
                {
                    StatusText = $"Conversion failed: {res}";
                    return;
                }
            }
            pk.Heal();
            pk.RefreshChecksum();
            Result = pk;
            var la = new LegalityAnalysis(pk);
            StatusText = $"Converted {Describe(gift)} — legal: {(la.Valid ? "yes" : "no")}. Click Add to Box.";
        }
        catch (System.Exception ex)
        {
            StatusText = $"Could not convert this gift: {ex.Message}";
        }
    }
}
