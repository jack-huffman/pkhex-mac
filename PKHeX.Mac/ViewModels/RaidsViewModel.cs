using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Tera Raid editor for Scarlet/Violet: the live raid crystals in each region, the
/// seven-star event capture records, and the progression that gates both.
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

        // The live raid crystals: one list per region, each with its own daily seeds.
        AddRegion("Paldea", sv.RaidPaldea, RaidSpawnList9.RaidCountLegal_T0);
        AddRegion("Kitakami", sv.RaidKitakami, RaidSpawnList9.RaidCountLegal_T1);
        AddRegion("Blueberry Academy", sv.RaidBlueberry, RaidSpawnList9.RaidCountLegal_T2);
        SelectedRegion = Regions.FirstOrDefault();

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

    private void AddRegion(string name, RaidSpawnList9 list, int legalCount)
    {
        // A save from before a DLC has an empty block for that region.
        if (list.CountAll == 0)
            return;
        Regions.Add(new RaidRegionViewModel(name, list, legalCount, _onChanged));
    }

    public bool IsSupported { get; }
    public ObservableCollection<RaidRowViewModel> Rows { get; } = [];
    public ObservableCollection<RaidFlagViewModel> Unlocks { get; } = [];
    public ObservableCollection<RaidCounterViewModel> Counters { get; } = [];

    /// <summary>The live raid crystal lists, one per region the save knows about.</summary>
    public ObservableCollection<RaidRegionViewModel> Regions { get; } = [];

    [ObservableProperty] private RaidRegionViewModel? _selectedRegion;

    public bool HasRegions => Regions.Count > 0;

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
                : "Seven-star raids are not unlocked on this save, so there are no records. Tick \"7-star raids\" under Progression to unlock the tier.")
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

/// <summary>
/// One region's raid crystals. The two seeds decide today's and tomorrow's raid
/// line-up for the whole region; each den then has its own seed and content tier.
/// </summary>
public partial class RaidRegionViewModel : ObservableObject
{
    private readonly RaidSpawnList9 _list;
    private readonly Action _onChanged;
    private readonly List<DenRowViewModel> _all = [];
    private bool _loading;

    public RaidRegionViewModel(string name, RaidSpawnList9 list, int legalCount, Action onChanged)
    {
        _list = list;
        _onChanged = onChanged;
        Name = name;
        LegalCount = legalCount;
        HasSeeds = list.HasSeeds;

        var dens = list.GetAllRaids();
        for (int i = 0; i < dens.Length; i++)
            _all.Add(new DenRowViewModel(this, i, dens[i]));

        _loading = true;
        CurrentSeedText = $"{list.CurrentSeed:X16}";
        TomorrowSeedText = $"{list.TomorrowSeed:X16}";
        _loading = false;

        ApplyFilter();
    }

    public string Name { get; }
    public int LegalCount { get; }
    public bool HasSeeds { get; }
    public int TotalDens => _all.Count;

    public ObservableCollection<DenRowViewModel> Dens { get; } = [];

    /// <summary>Content tiers, shared by every den picker.</summary>
    public static IReadOnlyList<string> ContentChoices { get; } =
        ["1–5★ standard", "6★ black crystal", "Event distribution", "7★ Mightiest Mark"];

    [ObservableProperty] private bool _activeOnly = true;
    [ObservableProperty] private string _currentSeedText = string.Empty;
    [ObservableProperty] private string _tomorrowSeedText = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _seedError = string.Empty;
    [ObservableProperty] private DenRowViewModel? _selectedDen;

    public override string ToString() => Name;

    partial void OnActiveOnlyChanged(bool value) => ApplyFilter();

    partial void OnCurrentSeedTextChanged(string value) => WriteSeed(value, today: true);
    partial void OnTomorrowSeedTextChanged(string value) => WriteSeed(value, today: false);

    private void WriteSeed(string text, bool today)
    {
        if (_loading || !HasSeeds)
            return;
        if (!ulong.TryParse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seed))
        {
            SeedError = "Seeds are 16 hex digits.";
            return;
        }
        SeedError = string.Empty;
        if (today)
            _list.CurrentSeed = seed;
        else
            _list.TomorrowSeed = seed;
        _onChanged();
    }

    private void ApplyFilter()
    {
        Dens.Clear();
        foreach (var den in _all)
        {
            // Padding entries have no area assigned and can't hold a real raid.
            if (ActiveOnly && den.AreaId == 0)
                continue;
            Dens.Add(den);
        }
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var placed = _all.Count(d => d.AreaId != 0);
        var active = _all.Count(d => d.IsEnabled);
        var special = _all.Count(d => d.IsEnabled && d.ContentIndex > 0);
        Summary = $"{active} active of {placed} placed dens · {special} above standard tier · {LegalCount} normally in play";
    }

    internal void NotifyChanged()
    {
        RefreshSummary();
        _onChanged();
    }

    /// <summary>
    /// Copies the selected den's tier and seed onto every placed den — PKHeX's own
    /// bulk edit, and the practical way to farm one raid type.
    /// </summary>
    [RelayCommand]
    public void PropagateSelected()
    {
        if (SelectedDen is not { } den)
            return;
        _list.Propagate(den.Index, seedToo: true);
        Reload();
        _onChanged();
    }

    [RelayCommand]
    public void UnclaimAllPoints()
    {
        foreach (var den in _all.Where(d => d.ClaimedLeaguePoints))
            den.ClaimedLeaguePoints = false;
        NotifyChanged();
    }

    [RelayCommand]
    public void EnableAllPlaced()
    {
        foreach (var den in _all.Where(d => d.AreaId != 0 && !d.IsEnabled))
            den.IsEnabled = true;
        NotifyChanged();
    }

    /// <summary>Re-reads every row after a bulk write went straight to the save data.</summary>
    private void Reload()
    {
        foreach (var den in _all)
            den.Reload();
        RefreshSummary();
    }
}

/// <summary>One raid crystal: where it is, whether it is up, and what it will spawn.</summary>
public partial class DenRowViewModel : ObservableObject
{
    private readonly RaidRegionViewModel _parent;
    private readonly TeraRaidDetail _detail;
    private bool _loading;

    public DenRowViewModel(RaidRegionViewModel parent, int index, TeraRaidDetail detail)
    {
        _parent = parent;
        _detail = detail;
        Index = index;
        Reload();
    }

    public int Index { get; }
    public uint AreaId => _detail.AreaID;

    /// <summary>Exposed per row so the tier picker can bind inside the item template.</summary>
    public IReadOnlyList<string> ContentChoices => RaidRegionViewModel.ContentChoices;

    /// <summary>Area, lottery group and spawn point — the crystal's place in the world.</summary>
    public string PlaceText => AreaId == 0
        ? "unplaced"
        : $"Area {_detail.AreaID} · group {_detail.LotteryGroup} · point {_detail.SpawnPointID}";

    public string Label => $"Den {Index + 1}";

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _contentIndex;
    [ObservableProperty] private string _seedText = string.Empty;
    [ObservableProperty] private bool _claimedLeaguePoints;
    [ObservableProperty] private string _error = string.Empty;

    /// <summary>Refreshes the displayed values from the underlying save data.</summary>
    internal void Reload()
    {
        _loading = true;
        IsEnabled = _detail.IsEnabled;
        ContentIndex = (int)_detail.Content;
        SeedText = $"{_detail.Seed:X8}";
        ClaimedLeaguePoints = _detail.IsClaimedLeaguePoints;
        _loading = false;
        OnPropertyChanged(nameof(PlaceText));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading)
            return;
        _detail.IsEnabled = value;
        _parent.NotifyChanged();
    }

    partial void OnContentIndexChanged(int value)
    {
        if (_loading || (uint)value > 3)
            return;
        _detail.Content = (TeraRaidContentType)value;
        _parent.NotifyChanged();
    }

    partial void OnClaimedLeaguePointsChanged(bool value)
    {
        if (_loading)
            return;
        _detail.IsClaimedLeaguePoints = value;
        _parent.NotifyChanged();
    }

    partial void OnSeedTextChanged(string value)
    {
        if (_loading)
            return;
        if (!uint.TryParse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seed))
        {
            Error = "8 hex digits";
            return;
        }
        Error = string.Empty;
        _detail.Seed = seed;
        _parent.NotifyChanged();
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
