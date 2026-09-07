using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Editor for the save file's trainer data. Identity fields wait for Apply; the toggles
/// and shortcuts write straight into the save.
/// </summary>
public partial class TrainerEditorViewModel : ObservableObject
{
    /// <summary>
    /// Gen 7 onward shows a six-digit TID and a four-digit SID carved out of one 32-bit
    /// value, so the SID cannot exceed <c>uint.MaxValue / 1,000,000</c>: 4294. A larger SID
    /// wraps the combined value and silently rewrites both numbers.
    /// </summary>
    private const uint MaxSevenDigitSid = uint.MaxValue / 1_000_000;
    private const uint MaxSixDigitTid = 999_999;

    private readonly SaveFile _sav;
    private readonly Action _onChanged;
    private bool _loading;

    /// <param name="sav">The save being edited.</param>
    /// <param name="onChanged">
    /// Called whenever something writes directly into the save. Badges, ride abilities,
    /// currencies and the unlock shortcuts do not wait for Apply, so without this the
    /// save would be modified while the unsaved-changes indicator still read clean.
    /// </param>
    public TrainerEditorViewModel(SaveFile sav, Action onChanged)
    {
        _sav = sav;
        _onChanged = onChanged;
        _loading = true;
        OtName = sav.OT;
        GenderIndex = sav.Gender;
        Tid = sav.DisplayTID;
        Sid = sav.DisplaySID;
        Money = sav.Money;
        Hours = sav.PlayedHours;
        Minutes = sav.PlayedMinutes;
        Seconds = sav.PlayedSeconds;
        var sevenDigit = sav.Generation >= 7;
        MaxTid = sevenDigit ? MaxSixDigitTid : ushort.MaxValue;
        MaxSid = sevenDigit ? MaxSevenDigitSid : ushort.MaxValue;
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
        _loading = false;
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
        foreach (var upgrade in RideUpgrades)
            upgrade.PropertyChanged += (_, _) => OnPropertyChanged(nameof(RideSummary));
    }

    private void BuildBadges(SaveFile sav)
    {
        var progress = new Sv9Progress(sav);
        foreach (var (group, label, block) in Sv9Progress.Badges)
        {
            if (!progress.Exists(block))
                continue;
            Badges.Add(new BadgeRowViewModel(progress, group, label, block, _onChanged));
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
        if (_loading || _sav is not SAV9SV sv)
            return;
        sv.LeaguePoints = value;
        _onChanged();
    }

    partial void OnBlueberryPointsChanged(uint value)
    {
        if (_loading || _sav is not SAV9SV sv)
            return;
        sv.BlueberryPoints = value;
        _onChanged();
    }

    /// <summary>Progression shortcuts PKHeX exposes for Scarlet/Violet.</summary>
    [RelayCommand]
    private void UnlockAllTmRecipes() => Shortcut(sv => sv.UnlockAllTMRecipes(), "Unlocked every TM recipe.");

    [RelayCommand]
    private void UnlockAllThrowStyles() => Shortcut(sv => sv.UnlockAllThrowStyles(), "Unlocked every throw style.");

    [RelayCommand]
    private void UnlockAllCoaches() => Shortcut(sv => sv.UnlockAllCoaches(), "Unlocked the Blueberry Academy coaches.");

    [RelayCommand]
    private void CollectAllStakes() => Shortcut(sv => sv.CollectAllStakes(), "Marked all Ogre Clan stakes collected.");

    [RelayCommand]
    private void ActivateSnacksworthLegendaries() =>
        Shortcut(sv => sv.ActivateSnacksworthLegendaries(), "Activated the Snacksworth legendary encounters.");

    /// <summary>Runs one of PKHeX's bulk unlocks and records that the save changed.</summary>
    private void Shortcut(Action<SAV9SV> apply, string result)
    {
        if (_sav is not SAV9SV sv)
            return;
        apply(sv);
        UnlockResult = result;
        _onChanged();
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

    /// <summary>
    /// Writes the identity fields. Refuses a TID/SID pair the game cannot store, because
    /// the engine would wrap it into two different numbers rather than reject it.
    /// </summary>
    public bool Apply(out string problem)
    {
        problem = string.Empty;
        if (_sav.Generation >= 7 && !_sav.IsValidTrainerID7(Sid, Tid))
        {
            problem = $"A secret ID above {MaxSevenDigitSid} cannot be stored with that trainer ID.";
            return false;
        }
        _sav.OT = OtName;
        _sav.Gender = (byte)GenderIndex;
        _sav.DisplayTID = Tid;
        _sav.DisplaySID = Sid;
        _sav.Money = Money;
        _sav.PlayedHours = Hours;
        _sav.PlayedMinutes = Minutes;
        _sav.PlayedSeconds = Seconds;
        return true;
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
    private readonly Action _onChanged;
    private bool _loading;

    public BadgeRowViewModel(Sv9Progress progress, string group, string label, string block, Action onChanged)
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
                next = Math.Max(next, _progress.GetInt(block) + 1);
            Order = next;
        }
        else
        {
            Order = 0;
        }
        _progress.SetInt(_block, Order);
        OnPropertyChanged(nameof(OrderText));
        _onChanged();
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
    private readonly Action _onChanged;
    private bool _loading;

    public ProgressFlagViewModel(Sv9Progress progress, string label, string block, Action onChanged)
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
