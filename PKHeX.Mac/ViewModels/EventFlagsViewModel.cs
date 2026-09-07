using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Event flag editor for the save formats that keep a flat flag array.
/// </summary>
/// <remarks>
/// Generations 2 through 5 (and a few later formats) expose <see cref="IEventFlagArray"/>.
/// Gen 8/9 replaced it with the SCBlock system, so Sword/Shield and Scarlet/Violet report
/// unsupported here rather than showing an editor that cannot write anything. Work values
/// are counted in the summary but are not edited yet.
/// </remarks>
public partial class EventFlagsViewModel : ObservableObject
{
    /// <summary>The array can be thousands long; the list stays responsive by stopping here.</summary>
    private const int MaxFlagsShown = 500;

    private readonly SaveFile _sav;
    private readonly Action _onChanged;
    private readonly List<EventFlagRowViewModel> _allFlags = [];
    private bool _bulk;

    public EventFlagsViewModel(SaveFile sav, Action onChanged)
    {
        _sav = sav;
        _onChanged = onChanged;

        if (sav is IEventFlagArray flags)
        {
            FlagCount = flags.EventFlagCount;
            SupportsFlags = FlagCount > 0;
            for (int i = 0; i < FlagCount; i++)
                _allFlags.Add(new EventFlagRowViewModel(this, i, flags.GetEventFlag(i)));
        }

        if (sav is IEventWorkArray<int> wi)
            WorkCount = wi.EventWorkCount;
        else if (sav is IEventWorkArray<byte> wb)
            WorkCount = wb.EventWorkCount;

        UnsupportedNote = SupportsFlags
            ? string.Empty
            : $"{GameInfo.GetVersionName(sav.Version)} stores progress in SCBlocks rather than a flat event-flag " +
              "array, so there is nothing here to edit. Generations 2 through 5 are supported.";

        ApplyFilter();
    }

    public bool SupportsFlags { get; }
    public int FlagCount { get; }
    public int WorkCount { get; }
    public string UnsupportedNote { get; }
    public bool IsSupported => SupportsFlags;

    public ObservableCollection<EventFlagRowViewModel> Flags { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _setOnly;
    [ObservableProperty] private string _summary = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSetOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Flags.Clear();
        var query = SearchText.Trim();
        foreach (var row in _allFlags)
        {
            if (SetOnly && !row.Value)
                continue;
            if (query.Length != 0 && !row.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Flags.Add(row);
            if (Flags.Count >= MaxFlagsShown)
                break;
        }
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var set = _allFlags.Count(f => f.Value);
        Summary = SupportsFlags
            ? $"{set} of {FlagCount} flags set · showing {Flags.Count}"
              + (WorkCount > 0 ? $" · {WorkCount} work values" : string.Empty)
            : string.Empty;
    }

    internal void Write(int index, bool value)
    {
        if (_sav is not IEventFlagArray flags)
            return;
        flags.SetEventFlag(index, value);
        if (_bulk)
            return;
        RefreshSummary();
        _onChanged();
    }

    /// <summary>Clears every flag currently listed, reported as one change.</summary>
    [RelayCommand]
    public void ClearVisible()
    {
        _bulk = true;
        foreach (var row in Flags.ToList())
            row.Value = false;
        _bulk = false;
        RefreshSummary();
        _onChanged();
    }
}

/// <summary>One event flag.</summary>
public partial class EventFlagRowViewModel : ObservableObject
{
    private readonly EventFlagsViewModel _parent;
    private readonly int _index;
    private bool _loading;

    public EventFlagRowViewModel(EventFlagsViewModel parent, int index, bool value)
    {
        _parent = parent;
        _index = index;
        Label = $"Flag {index:0000}";
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
        _parent.Write(_index, value);
    }
}
