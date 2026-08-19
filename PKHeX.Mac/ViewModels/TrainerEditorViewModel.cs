using CommunityToolkit.Mvvm.ComponentModel;
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
