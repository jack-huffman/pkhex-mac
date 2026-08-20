using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>Editor for the save file's trainer data. Writes to the save on Apply.</summary>
public partial class TrainerEditorViewModel : ObservableObject
{
    private readonly SaveFile _sav;

    public TrainerEditorViewModel(SaveFile sav)
    {
        _sav = sav;
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
        }
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
