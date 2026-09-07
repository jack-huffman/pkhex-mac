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
            var row = new DexRowViewModel(this, species, strings.SpeciesName(species));
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

    /// <summary>
    /// The row whose details fill the side pane. Detail lives outside the list so
    /// row heights stay uniform — a virtualized list re-estimates its scroll extent
    /// when an item changes height, which is what made the viewport jump.
    /// </summary>
    [ObservableProperty] private DexRowViewModel? _selectedRow;

    partial void OnSelectedRowChanged(DexRowViewModel? value) => value?.BuildDetail();

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
                && !row.NumberText.Contains(query, StringComparison.Ordinal))
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

    internal SaveFile Save => _sav;
    internal GameStrings Strings => _strings;

    /// <summary>Registers or clears a whole entry — every form and gender — and records the change.</summary>
    internal void Write(ushort species, bool value)
    {
        DexAccessor.SetEntry(_sav, species, value, ShinyToo);
        _onChanged();
    }

    /// <summary>
    /// Records a change a row wrote itself. Rows edit single flags and must not go through
    /// <see cref="Write"/>, which would re-register every form and undo the edit.
    /// </summary>
    internal void NotifyChanged() => _onChanged();

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

/// <summary>
/// One dex entry. Collapsed it shows sprite/name/caught; expanded it exposes every
/// field the game stores for that species — per-form seen/obtained, genders seen,
/// shiny, and (base-game entries only) the languages it was obtained in.
/// </summary>
public partial class DexRowViewModel : ObservableObject
{
    /// <summary>The dex stores form flags in a 32-bit field, so later forms have no bit.</summary>
    private const int MaxFormsTracked = 32;

    private static readonly string[] GenderNames = ["Male", "Female", "Genderless"];
    private static readonly string[] LanguageNames =
        ["Japanese", "English", "French", "Italian", "German", "Spanish", "Korean", "Chinese S", "Chinese T"];

    private readonly PokedexViewModel _parent;
    private bool _loading;
    private bool _detailBuilt;

    public DexRowViewModel(PokedexViewModel parent, ushort species, string name)
    {
        _parent = parent;
        Number = species;
        Name = name;
        Sprite = SpriteService.GetSprite(species, 0, 0, 0, shiny: false, EntityContext.None);
    }

    public ushort Number { get; }
    public string Name { get; }
    public Bitmap? Sprite { get; }
    public string NumberText => $"#{Number:0000}";

    [ObservableProperty] private bool _seen;
    [ObservableProperty] private bool _caught;
    [ObservableProperty] private bool _shinySeen;
    [ObservableProperty] private bool _supportsDetail;
    [ObservableProperty] private bool _hasLanguages;
    [ObservableProperty] private bool _hasPerFormObtained;
    [ObservableProperty] private string _detailNote = string.Empty;

    public ObservableCollection<DexFormRowViewModel> Forms { get; } = [];
    public ObservableCollection<DexFlagViewModel> Genders { get; } = [];
    public ObservableCollection<DexFlagViewModel> Languages { get; } = [];

    /// <summary>Reads this entry's headline state from the save.</summary>
    internal void Reload(SaveFile sav)
    {
        _loading = true;
        Seen = DexAccessor.GetSeen(sav, Number);
        Caught = DexAccessor.GetCaught(sav, Number);
        SupportsDetail = Dex9Detail.IsSupported(sav, Number);
        if (sav is SAV9SV sv && SupportsDetail)
            ShinySeen = Dex9Detail.GetShinySeen(sv, Number);
        _loading = false;
        if (_detailBuilt)
            BuildDetail(); // keep an open panel in sync after bulk edits
    }

    /// <summary>Populates the detail pane from the save, on selection and after bulk changes.</summary>
    internal void BuildDetail()
    {
        Forms.Clear();
        Genders.Clear();
        Languages.Clear();
        _detailBuilt = true;

        if (_parent.Save is not SAV9SV sv || !SupportsDetail)
        {
            DetailNote = "This save format does not expose per-entry Pokédex details.";
            return;
        }

        HasLanguages = Dex9Detail.HasLanguageFlags(sv, Number);
        HasPerFormObtained = Dex9Detail.HasPerFormObtained(sv, Number);
        DetailNote = HasPerFormObtained
            ? "DLC entry: forms track seen and obtained separately."
            : "Base-game entry: obtained is stored once for the whole species, not per form.";

        var strings = _parent.Strings;
        var formNames = FormConverter.GetFormList(Number, strings.types, strings.forms,
            GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
        for (byte f = 0; f < formNames.Length && f < MaxFormsTracked; f++)
        {
            var label = string.IsNullOrWhiteSpace(formNames[f]) ? $"Form {f}" : formNames[f];
            Forms.Add(new DexFormRowViewModel(sv, Number, f, label, _parent.NotifyChanged));
        }

        for (byte g = 0; g < GenderNames.Length; g++)
        {
            var gender = g;
            Genders.Add(new DexFlagViewModel(
                GenderNames[g],
                Dex9Detail.GetGenderSeen(sv, Number, gender),
                v => Dex9Detail.SetGenderSeen(sv, Number, gender, v),
                _parent.NotifyChanged));
        }

        if (HasLanguages)
        {
            for (int i = 0; i < LanguageNames.Length; i++)
            {
                var index = i;
                Languages.Add(new DexFlagViewModel(
                    LanguageNames[i],
                    Dex9Detail.GetLanguage(sv, Number, index),
                    v => Dex9Detail.SetLanguage(sv, Number, index, v),
                    _parent.NotifyChanged));
            }
        }
    }

    partial void OnCaughtChanged(bool value)
    {
        if (_loading)
            return;
        _parent.Write(Number, value);
        // Registering the whole entry also decides the seen and shiny flags.
        _loading = true;
        Seen = value || Seen;
        if (_parent.Save is SAV9SV sv && SupportsDetail)
            ShinySeen = Dex9Detail.GetShinySeen(sv, Number);
        _loading = false;
        _parent.RefreshSummary();
        if (_detailBuilt)
            BuildDetail();
    }

    partial void OnShinySeenChanged(bool value)
    {
        if (_loading || _parent.Save is not SAV9SV sv || !SupportsDetail)
            return;
        Dex9Detail.SetShinySeen(sv, Number, value);
        _parent.NotifyChanged();
    }
}

/// <summary>A per-form row inside an expanded dex entry.</summary>
public partial class DexFormRowViewModel : ObservableObject
{
    private readonly SAV9SV _sav;
    private readonly ushort _species;
    private readonly byte _form;
    private readonly Action _onChanged;
    private bool _loading;

    public DexFormRowViewModel(SAV9SV sav, ushort species, byte form, string label, Action onChanged)
    {
        _sav = sav;
        _species = species;
        _form = form;
        _onChanged = onChanged;
        Label = label;
        _loading = true;
        Seen = Dex9Detail.GetFormSeen(sav, species, form);
        Obtained = Dex9Detail.GetFormObtained(sav, species, form);
        _loading = false;
    }

    public string Label { get; }

    [ObservableProperty] private bool _seen;
    [ObservableProperty] private bool _obtained;

    partial void OnSeenChanged(bool value)
    {
        if (_loading)
            return;
        Dex9Detail.SetFormSeen(_sav, _species, _form, value);
        _onChanged();
    }

    partial void OnObtainedChanged(bool value)
    {
        if (_loading)
            return;
        Dex9Detail.SetFormObtained(_sav, _species, _form, value);
        _onChanged();
    }
}

/// <summary>A single labelled dex flag (gender seen, language obtained).</summary>
public partial class DexFlagViewModel : ObservableObject
{
    private readonly Action<bool> _write;
    private readonly Action _onChanged;
    private bool _loading;

    public DexFlagViewModel(string label, bool value, Action<bool> write, Action onChanged)
    {
        Label = label;
        _write = write;
        _onChanged = onChanged;
        _loading = true;
        Value = value;
        _loading = false;
    }

    public string Label { get; }

    [ObservableProperty] private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_loading)
            return;
        _write(value);
        _onChanged();
    }
}
