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
/// Pokédex editor: registers or clears seen/caught state per species, with bulk
/// actions for completing or wiping the dex. Writes straight to the save.
/// </summary>
public partial class PokedexViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly List<DexRowViewModel> _all = [];
    private readonly Action _onChanged;

    public PokedexViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _sav = sav;
        _strings = strings;
        _onChanged = onChanged;
        IsSupported = DexAccessor.IsSupported(sav);
        if (!IsSupported)
            return;

        for (ushort species = 1; species <= sav.MaxSpeciesID; species++)
        {
            if (!sav.Personal.IsSpeciesInGame(species))
                continue; // not in this game's dex at all
            var name = (uint)species < strings.specieslist.Length ? strings.specieslist[species] : $"#{species}";
            var row = new DexRowViewModel(this, species, name);
            row.Reload(sav);
            _all.Add(row);
        }
        ApplyFilter();
    }

    public bool IsSupported { get; }
    public ObservableCollection<DexRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _missingOnly;
    [ObservableProperty] private bool _shinyToo;
    [ObservableProperty] private string _summary = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnMissingOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var query = SearchText.Trim();
        foreach (var row in _all)
        {
            if (MissingOnly && row.Caught)
                continue;
            if (query.Length != 0
                && !row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !row.Number.ToString().Contains(query))
                continue;
            Rows.Add(row);
        }
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var caught = _all.Count(r => r.Caught);
        var seen = _all.Count(r => r.Seen);
        Summary = $"{caught} caught · {seen} seen · {_all.Count} in this game's dex";
    }

    internal void Write(ushort species, bool value)
    {
        DexAccessor.SetEntry(_sav, species, value, ShinyToo);
        _onChanged();
    }

    /// <summary>Re-reads every row from the save after a bulk change.</summary>
    private void ReloadAll()
    {
        foreach (var row in _all)
            row.Reload(_sav);
        RefreshSummary();
    }

    [RelayCommand]
    public void CompleteDex()
    {
        DexAccessor.SetAll(_sav, true, ShinyToo);
        ReloadAll();
        _onChanged();
    }

    [RelayCommand]
    public void ClearDex()
    {
        DexAccessor.SetAll(_sav, false);
        ReloadAll();
        _onChanged();
    }

    /// <summary>Registers everything currently sitting in the boxes and party.</summary>
    [RelayCommand]
    public void RegisterOwned()
    {
        var seen = new HashSet<ushort>();
        if (_sav.HasBox)
        {
            for (int box = 0; box < _sav.BoxCount; box++)
            {
                for (int slot = 0; slot < _sav.BoxSlotCount; slot++)
                {
                    var pk = _sav.GetBoxSlotAtIndex(box, slot);
                    if (pk.Species != 0)
                        seen.Add(pk.Species);
                }
            }
        }
        if (_sav.HasParty)
        {
            for (int i = 0; i < _sav.PartyCount; i++)
            {
                var pk = _sav.GetPartySlotAtIndex(i);
                if (pk.Species != 0)
                    seen.Add(pk.Species);
            }
        }
        foreach (var species in seen)
            DexAccessor.SetEntry(_sav, species, true, ShinyToo);
        ReloadAll();
        _onChanged();
        Summary = $"Registered {seen.Count} species found in your boxes and party. " + Summary;
    }
}

/// <summary>One dex entry: species, sprite, and its seen/caught flags.</summary>
public partial class DexRowViewModel : ObservableObject
{
    private readonly PokedexViewModel _parent;
    private bool _loading;

    public DexRowViewModel(PokedexViewModel parent, ushort species, string name)
    {
        _parent = parent;
        Number = species;
        Name = name;
        Sprite = SpriteService.GetSprite(species, 0, 0, 0, shiny: false, EntityContext.None);
        _loading = true;
        _loading = false;
    }

    public ushort Number { get; }
    public string Name { get; }
    public Bitmap? Sprite { get; }
    public string NumberText => $"#{Number:0000}";

    [ObservableProperty] private bool _seen;
    [ObservableProperty] private bool _caught;

    /// <summary>Reads this entry's state back from the save.</summary>
    internal void Reload(SaveFile sav)
    {
        _loading = true;
        Seen = DexAccessor.GetSeen(sav, Number);
        Caught = DexAccessor.GetCaught(sav, Number);
        _loading = false;
    }

    partial void OnCaughtChanged(bool value)
    {
        if (_loading)
            return;
        // Caught implies seen; clearing caught leaves the entry unregistered.
        _parent.Write(Number, value);
        _loading = true;
        Seen = value || Seen;
        _loading = false;
        _parent.RefreshSummary();
    }
}
