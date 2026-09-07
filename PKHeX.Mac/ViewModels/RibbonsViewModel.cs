using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Ribbons and Marks editor for the selected Pokémon. Ribbons are discovered
/// from the entity type itself (every <c>Ribbon*</c> property), so the list adapts
/// to the format without a hand-maintained table.
/// </summary>
public partial class RibbonsViewModel : ObservableObject
{
    private readonly GameStrings _strings;
    private readonly Action _markDirty;
    private PKM? _pk;
    private readonly List<RibbonRowViewModel> _all = [];
    private bool _loading;

    public RibbonsViewModel(GameStrings strings, Action markDirty)
    {
        _strings = strings;
        _markDirty = markDirty;
    }

    public ObservableCollection<RibbonRowViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _hasPokemon;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _ownedOnly;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _supportsAffixed;
    [ObservableProperty] private IReadOnlyList<ComboItem> _affixedChoices = [];
    [ObservableProperty] private int _affixedValue = -1;

    public void Load(PKM? pk)
    {
        _loading = true;
        try
        {
            _pk = pk;
            _all.Clear();
            Rows.Clear();
            // Ribbons are discovered by reflection, so any format that has Ribbon* properties
            // is editable — not only the Gen 8+ formats that index them.
            var ribbons = pk is { Species: > 0 } ? RibbonInfo.GetRibbonInfo(pk) : [];
            HasPokemon = ribbons.Count > 0;
            SupportsAffixed = pk is IRibbonSetAffixed;
            if (!HasPokemon || pk is null)
                return;

            foreach (var info in ribbons)
            {
                var display = _strings.Ribbons.GetNameSafe(info.Name, out var name) ? name : Prettify(info.Name);
                _all.Add(new RibbonRowViewModel(this, info, display));
            }
            _all.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

            if (pk is IRibbonSetAffixed af)
            {
                // -1 means "no ribbon affixed".
                var choices = new List<ComboItem>(_all.Count + 1) { new("(None affixed)", -1) };
                foreach (var row in _all.Where(r => !r.IsCounter))
                {
                    if (RibbonIndexOf(row.PropertyName) is { } idx)
                        choices.Add(new ComboItem(row.DisplayName, idx));
                }
                AffixedChoices = choices;
                AffixedValue = af.AffixedRibbon;
            }
        }
        finally
        {
            _loading = false;
        }
        ApplyFilter();
    }

    /// <summary>Maps a <c>Ribbon*</c> property name back to its <see cref="RibbonIndex"/> value.</summary>
    private static int? RibbonIndexOf(string propertyName)
    {
        var name = propertyName.StartsWith("Ribbon", StringComparison.Ordinal)
            ? propertyName["Ribbon".Length..]
            : propertyName;
        return Enum.TryParse<RibbonIndex>(name, out var idx) ? (int)idx : null;
    }

    /// <summary>"RibbonChampionKalos" → "Champion Kalos"; proper nouns keep their capitals.</summary>
    private static string Prettify(string propertyName)
    {
        var name = propertyName.StartsWith("Ribbon", StringComparison.Ordinal)
            ? propertyName["Ribbon".Length..]
            : propertyName;
        return DisplayNames.FromPascalCase(name, keepCase: true);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnOwnedOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var query = SearchText.Trim();
        foreach (var row in _all)
        {
            if (OwnedOnly && !row.HasRibbon && row.Count == 0)
                continue;
            if (query.Length != 0 && !row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Rows.Add(row);
        }
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var owned = _all.Count(r => r.HasRibbon || r.Count > 0);
        Summary = _all.Count == 0
            ? string.Empty
            : $"{owned} of {_all.Count} ribbons & marks set";
    }

    internal void Write(RibbonRowViewModel row)
    {
        if (_loading || _pk is null)
            return;
        // Ribbon properties are reflective; write through the same path PKHeX uses.
        if (row.IsCounter)
            ReflectUtil.SetValue(_pk, row.PropertyName, (byte)row.Count);
        else
            ReflectUtil.SetValue(_pk, row.PropertyName, row.HasRibbon);
        RefreshSummary();
        _markDirty();
    }

    partial void OnAffixedValueChanged(int value)
    {
        if (_loading || _pk is not IRibbonSetAffixed af)
            return;
        af.AffixedRibbon = (sbyte)Math.Clamp(value, -1, sbyte.MaxValue);
        _markDirty();
    }

    [RelayCommand]
    public void GiveAllLegal()
    {
        if (_pk is null)
            return;
        // Ask the legality engine which ribbons this Pokémon is entitled to.
        var la = new LegalityAnalysis(_pk);
        RibbonApplicator.SetAllValidRibbons(la);
        Load(_pk);
        _markDirty();
    }

    /// <summary>
    /// Clears every ribbon and mark, not only the ones the legality engine would remove:
    /// the point of the button is a clean slate.
    /// </summary>
    [RelayCommand]
    public void RemoveAll()
    {
        if (_pk is null)
            return;
        foreach (var row in _all)
            ReflectUtil.SetValue(_pk, row.PropertyName, row.IsCounter ? (byte)0 : false);
        Load(_pk);
        _markDirty();
    }
}

/// <summary>One ribbon (a flag) or ribbon counter (a byte, e.g. contest memory count).</summary>
public partial class RibbonRowViewModel : ObservableObject
{
    private readonly RibbonsViewModel _parent;
    private bool _loading;

    public RibbonRowViewModel(RibbonsViewModel parent, RibbonInfo info, string displayName)
    {
        _parent = parent;
        PropertyName = info.Name;
        DisplayName = displayName;
        IsCounter = info.Type == RibbonValueType.Byte;
        MaxCount = IsCounter ? info.MaxCount : 0;
        _loading = true;
        HasRibbon = info.HasRibbon;
        Count = info.RibbonCount;
        _loading = false;
    }

    public string PropertyName { get; }
    public string DisplayName { get; }
    public bool IsCounter { get; }
    public bool IsFlag => !IsCounter;
    public int MaxCount { get; }

    [ObservableProperty] private bool _hasRibbon;
    [ObservableProperty] private int _count;

    partial void OnHasRibbonChanged(bool value)
    {
        if (_loading)
            return;
        _parent.Write(this);
    }

    partial void OnCountChanged(int value)
    {
        if (_loading)
            return;
        _parent.Write(this);
    }
}
