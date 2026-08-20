using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Seven-star Tera Raid records for Scarlet/Violet: which event raids this save has
/// captured and defeated. Clearing a capture flag is what lets an event raid be
/// caught again.
/// </summary>
public partial class RaidsViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly Action _onChanged;
    private readonly List<RaidRowViewModel> _all = [];

    public RaidsViewModel(SaveFile sav, Action onChanged)
    {
        _sav = sav;
        _onChanged = onChanged;
        IsSupported = sav is SAV9SV;
        if (sav is not SAV9SV sv)
            return;

        var raids = sv.RaidSevenStar.GetAllRaids();
        for (int i = 0; i < raids.Length; i++)
            _all.Add(new RaidRowViewModel(this, i, raids[i]));
        ApplyFilter();
    }

    public bool IsSupported { get; }
    public ObservableCollection<RaidRowViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _usedOnly = true;
    [ObservableProperty] private string _summary = string.Empty;

    partial void OnUsedOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in _all)
        {
            // Most entries are unused padding; hide them unless asked for.
            if (UsedOnly && row.Identifier == 0 && !row.Captured && !row.Defeated)
                continue;
            Rows.Add(row);
        }
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var captured = _all.Count(r => r.Captured);
        var defeated = _all.Count(r => r.Defeated);
        Summary = $"{captured} captured · {defeated} defeated · {_all.Count} record slots";
    }

    internal void NotifyChanged()
    {
        RefreshSummary();
        _onChanged();
    }

    /// <summary>Clears every capture flag, making the event raids catchable again.</summary>
    [RelayCommand]
    public void AllowRecatchAll()
    {
        foreach (var row in _all.Where(r => r.Captured))
            row.Captured = false;
        RefreshSummary();
        _onChanged();
    }

    [RelayCommand]
    public void MarkAllDefeated()
    {
        foreach (var row in _all.Where(r => r.Identifier != 0 && !r.Defeated))
            row.Defeated = true;
        RefreshSummary();
        _onChanged();
    }
}

/// <summary>One seven-star raid record.</summary>
public partial class RaidRowViewModel : ObservableObject
{
    private readonly RaidsViewModel _parent;
    private readonly SevenStarRaidDetail _detail;
    private bool _loading;

    public RaidRowViewModel(RaidsViewModel parent, int index, SevenStarRaidDetail detail)
    {
        _parent = parent;
        _detail = detail;
        Index = index;
        Identifier = detail.Identifier;
        Label = Identifier == 0 ? $"Slot {index + 1} (empty)" : $"Slot {index + 1} · ID {Identifier:X8}";
        _loading = true;
        Captured = detail.Captured;
        Defeated = detail.Defeated;
        _loading = false;
    }

    public int Index { get; }
    public uint Identifier { get; }
    public string Label { get; }

    [ObservableProperty] private bool _captured;
    [ObservableProperty] private bool _defeated;

    partial void OnCapturedChanged(bool value)
    {
        if (_loading)
            return;
        _detail.Captured = value;
        _parent.NotifyChanged();
    }

    partial void OnDefeatedChanged(bool value)
    {
        if (_loading)
            return;
        _detail.Defeated = value;
        _parent.NotifyChanged();
    }
}
