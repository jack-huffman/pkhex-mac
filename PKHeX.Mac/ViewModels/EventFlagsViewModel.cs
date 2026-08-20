using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Event flag / work value editor for the save formats that expose them.
/// </summary>
/// <remarks>
/// Only some generations keep a flat event-flag array (<see cref="IEventFlagArray"/>)
/// or work-value array (<see cref="IEventWorkArray{T}"/>). Gen 8/9 replaced both with
/// the SCBlock system, so Sword/Shield, BDSP-era and Scarlet/Violet saves report
/// unsupported here rather than showing an editor that cannot write anything.
/// </remarks>
public partial class EventFlagsViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly Action _onChanged;
    private readonly List<EventFlagRowViewModel> _allFlags = [];

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

        SupportsWork = sav is IEventWorkArray<int> or IEventWorkArray<byte>;
        if (sav is IEventWorkArray<int> wi)
            WorkCount = wi.EventWorkCount;
        else if (sav is IEventWorkArray<byte> wb)
            WorkCount = wb.EventWorkCount;

        UnsupportedNote = SupportsFlags || SupportsWork
            ? string.Empty
            : $"{GameInfo.GetVersionName(sav.Version)} stores progress in SCBlocks rather than a flat event-flag " +
              "array, so there is nothing here to edit. Generations 2 through 5 (and BDSP work values) are supported.";

        ApplyFilter();
    }

    public bool SupportsFlags { get; }
    public bool SupportsWork { get; }
    public int FlagCount { get; }
    public int WorkCount { get; }
    public string UnsupportedNote { get; }
    public bool IsSupported => SupportsFlags || SupportsWork;

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
            if (Flags.Count >= 500)
                break; // the array can be thousands long; keep the list responsive
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
        RefreshSummary();
        _onChanged();
    }

    [RelayCommand]
    public void ClearVisible()
    {
        foreach (var row in Flags.ToList())
            row.Value = false;
        RefreshSummary();
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
