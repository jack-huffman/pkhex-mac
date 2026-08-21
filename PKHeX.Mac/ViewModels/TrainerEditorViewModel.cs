using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>Editor for the save file's trainer data. Writes to the save on Apply.</summary>
public partial class TrainerEditorViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly Action? _onChanged;

    /// <param name="onChanged">
    /// Called when a toggle writes directly into the save. Badges and ride abilities do
    /// not wait for Apply, so without this the save would be modified while the
    /// unsaved-changes indicator still read clean.
    /// </param>
    public TrainerEditorViewModel(SaveFile sav, Action? onChanged = null)
    {
        _sav = sav;
        _onChanged = onChanged;
        OtName = sav.OT;
        GenderIndex = sav.Gender;
        Tid = sav.DisplayTID;
        Sid = sav.DisplaySID;
        Money = sav.Money;
        Hours = sav.PlayedHours;
        Minutes = sav.PlayedMinutes;
        Seconds = sav.PlayedSeconds;
        MaxTid = sav.Generation >= 7 ? 999_999u : 65_535u;
        MaxSid = sav.Generation >= 7 ? 9_999u : 65_535u;
        MaxOtLength = sav.MaxStringLengthTrainer;
        MaxMoney = (uint)sav.MaxMoney;

        // Scarlet/Violet carry extra progression currencies and unlock helpers.
        if (sav is SAV9SV sv)
        {
            IsScarletViolet = true;
            LeaguePoints = sv.LeaguePoints;
            BlueberryPoints = sv.BlueberryPoints;
            BuildBadges(sav);
            BuildRideUpgrades(sav);
        }
    }

    /// <summary>Gym, Titan and Team Star clears, grouped for display.</summary>
    public List<BadgeRowViewModel> Badges { get; } = [];

    /// <summary>The ride legendary's unlocked abilities.</summary>
    public List<ProgressFlagViewModel> RideUpgrades { get; } = [];

    public bool HasRideUpgrades => RideUpgrades.Count > 0;

    /// <summary>Scarlet rides Koraidon, Violet rides Miraidon.</summary>
    public string RideName { get; private set; } = string.Empty;

    public string RideSummary => RideUpgrades.Count == 0
        ? string.Empty
        : $"{RideUpgrades.Count(u => u.Value)} of {RideUpgrades.Count} unlocked";
    public bool HasBadges => Badges.Count > 0;

    /// <summary>"12 of 18 cleared" — a quick read on story progress.</summary>
    public string BadgeSummary
    {
        get
        {
            var cleared = Badges.Count(b => b.Cleared);
            return $"{cleared} of {Badges.Count} cleared";
        }
    }

    private void BuildRideUpgrades(SaveFile sav)
    {
        if (sav is not SAV9SV sv)
            return;
        RideName = sv.Version switch
        {
            GameVersion.SL => "Koraidon",
            GameVersion.VL => "Miraidon",
            _ => "Ride legendary",
        };
        var progress = new Sv9Progress(sav);
        foreach (var (label, block) in Sv9Progress.RideUpgrades)
        {
            if (progress.Exists(block))
                RideUpgrades.Add(new ProgressFlagViewModel(progress, label, block, _onChanged));
        }
    }

    private void BuildBadges(SaveFile sav)
    {
        var progress = new Sv9Progress(sav);
        foreach (var (group, label, block) in Sv9Progress.Badges)
        {
            if (!progress.Exists(block))
                continue;
            Badges.Add(new BadgeRowViewModel(progress, group, label, block));
        }
        foreach (var badge in Badges)
            badge.PropertyChanged += (_, _) => OnPropertyChanged(nameof(BadgeSummary));
    }

    public bool IsScarletViolet { get; }
    public uint MaxMoney { get; }

    [ObservableProperty] private uint _leaguePoints;
    [ObservableProperty] private uint _blueberryPoints;
    [ObservableProperty] private string _unlockResult = string.Empty;

    partial void OnLeaguePointsChanged(uint value)
    {
        if (_sav is SAV9SV sv)
            sv.LeaguePoints = value;
    }

    partial void OnBlueberryPointsChanged(uint value)
    {
        if (_sav is SAV9SV sv)
            sv.BlueberryPoints = value;
    }

    /// <summary>Progression shortcuts PKHeX exposes for Scarlet/Violet.</summary>
    [RelayCommand]
    public void UnlockAllTmRecipes()
    {
        if (_sav is not SAV9SV sv)
            return;
        sv.UnlockAllTMRecipes();
        UnlockResult = "Unlocked every TM recipe.";
    }

    [RelayCommand]
    public void UnlockAllThrowStyles()
    {
        if (_sav is not SAV9SV sv)
            return;
        sv.UnlockAllThrowStyles();
        UnlockResult = "Unlocked every throw style.";
    }

    [RelayCommand]
    public void UnlockAllCoaches()
    {
        if (_sav is not SAV9SV sv)
            return;
        sv.UnlockAllCoaches();
        UnlockResult = "Unlocked the Blueberry Academy coaches.";
    }

    [RelayCommand]
    public void CollectAllStakes()
    {
        if (_sav is not SAV9SV sv)
            return;
        sv.CollectAllStakes();
        UnlockResult = "Marked all Ogre Clan stakes collected.";
    }

    [RelayCommand]
    public void ActivateSnacksworthLegendaries()
    {
        if (_sav is not SAV9SV sv)
            return;
        sv.ActivateSnacksworthLegendaries();
        UnlockResult = "Activated the Snacksworth legendary encounters.";
    }

    [ObservableProperty] private string _otName = string.Empty;
    [ObservableProperty] private int _genderIndex;
    [ObservableProperty] private uint _tid;
    [ObservableProperty] private uint _sid;
    [ObservableProperty] private uint _money;
    [ObservableProperty] private int _hours;
    [ObservableProperty] private int _minutes;
    [ObservableProperty] private int _seconds;

    public uint MaxTid { get; }
    public uint MaxSid { get; }
    public int MaxOtLength { get; }

    public void Apply()
    {
        _sav.OT = OtName;
        _sav.Gender = (byte)GenderIndex;
        _sav.DisplayTID = Tid;
        _sav.DisplaySID = Sid;
        _sav.Money = Money;
        _sav.PlayedHours = Hours;
        _sav.PlayedMinutes = Minutes;
        _sav.PlayedSeconds = Seconds;
    }
}

/// <summary>
/// One gym / titan / Team Star clear. The save stores the order it was cleared in,
/// so ticking one assigns the next free position and clearing it writes zero.
/// </summary>
public partial class BadgeRowViewModel : ObservableObject
{
    private readonly Sv9Progress _progress;
    private readonly string _block;
    private readonly Action? _onChanged;
    private bool _loading;

    public BadgeRowViewModel(Sv9Progress progress, string group, string label, string block,
                            Action? onChanged = null)
    {
        _progress = progress;
        _block = block;
        _onChanged = onChanged;
        Group = group;
        Label = label;
        _loading = true;
        Order = progress.GetInt(block);
        Cleared = Order != 0;
        _loading = false;
    }

    public string Group { get; }
    public string Label { get; }

    [ObservableProperty] private bool _cleared;
    [ObservableProperty] private int _order;

    public string OrderText => Order == 0 ? "—" : $"#{Order}";

    partial void OnClearedChanged(bool value)
    {
        if (_loading)
            return;
        if (value)
        {
            // Take the next position after whatever is already cleared.
            var next = 1;
            foreach (var (_, _, block) in Sv9Progress.Badges)
                next = System.Math.Max(next, _progress.GetInt(block) + 1);
            Order = next;
        }
        else
        {
            Order = 0;
        }
        _progress.SetInt(_block, Order);
        OnPropertyChanged(nameof(OrderText));
        _onChanged?.Invoke();
    }
}

/// <summary>
/// A boolean progress flag written straight into the save, used for the ride
/// legendary's abilities.
/// </summary>
public partial class ProgressFlagViewModel : ObservableObject
{
    private readonly Sv9Progress _progress;
    private readonly string _block;
    private readonly Action? _onChanged;
    private bool _loading;

    public ProgressFlagViewModel(Sv9Progress progress, string label, string block, Action? onChanged)
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
        _onChanged?.Invoke();
    }
}
