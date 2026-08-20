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

        // Tier unlocks and win tallies explain an otherwise-empty record list.
        var progress = new Sv9Progress(sav);
        foreach (var (label, block) in Sv9Progress.RaidUnlocks)
        {
            if (progress.Exists(block))
                Unlocks.Add(new RaidFlagViewModel(progress, label, block, onChanged));
        }
        foreach (var (label, block) in Sv9Progress.RaidCounters)
        {
            if (progress.Exists(block))
                Counters.Add(new RaidCounterViewModel(progress, label, block, onChanged));
        }
        SevenStarUnlocked = progress.GetFlag("KUnlockedRaidDifficulty6");
        ApplyFilter();
    }

    public bool IsSupported { get; }
    public ObservableCollection<RaidRowViewModel> Rows { get; } = [];
    public ObservableCollection<RaidFlagViewModel> Unlocks { get; } = [];
    public ObservableCollection<RaidCounterViewModel> Counters { get; } = [];

    /// <summary>Seven-star records only accumulate once that tier is unlocked.</summary>
    [ObservableProperty] private bool _sevenStarUnlocked;

    [ObservableProperty] private bool _usedOnly = true;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _emptyNote = string.Empty;

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
        EmptyNote = captured == 0 && defeated == 0
            ? (SevenStarUnlocked
                ? "No seven-star raids recorded yet — records appear once you beat event raids."
                : "Seven-star raids are not unlocked on this save, so there are no records. Tick \"7-star raids\" above to unlock the tier.")
            : string.Empty;
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

/// <summary>A raid tier unlock flag.</summary>
public partial class RaidFlagViewModel : ObservableObject
{
    private readonly Sv9Progress _progress;
    private readonly string _block;
    private readonly Action _onChanged;
    private bool _loading;

    public RaidFlagViewModel(Sv9Progress progress, string label, string block, Action onChanged)
    {
        _progress = progress;
        _block = block;
        _onChanged = onChanged;
        Label = label;
        _loading = true;
        Value = progress.GetFlag(block);
        _loading = false;
    }

    public string Label { get; }

    [ObservableProperty] private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_loading)
            return;
        _progress.SetFlag(_block, value);
        _onChanged();
    }
}

/// <summary>A raids-won tally.</summary>
public partial class RaidCounterViewModel : ObservableObject
{
    private readonly Sv9Progress _progress;
    private readonly string _block;
    private readonly Action _onChanged;
    private bool _loading;

    public RaidCounterViewModel(Sv9Progress progress, string label, string block, Action onChanged)
    {
        _progress = progress;
        _block = block;
        _onChanged = onChanged;
        Label = label;
        _loading = true;
        Value = progress.GetInt(block);
        _loading = false;
    }

    public string Label { get; }

    [ObservableProperty] private int _value;

    partial void OnValueChanged(int value)
    {
        if (_loading)
            return;
        _progress.SetInt(_block, value);
        _onChanged();
    }
}
