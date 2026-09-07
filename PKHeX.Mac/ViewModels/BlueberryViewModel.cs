using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Blueberry Academy: the club support board's purchases and the Blueberry Quest
/// (BBQ) tallies. Indigo Disk content, so Scarlet/Violet only.
/// </summary>
public partial class BlueberryViewModel : ObservableObject
{
    private readonly SAV9SV? _sav;
    private readonly BlueberrySupportBoard9? _board;
    private readonly Action _onChanged;
    private bool _loading;

    public BlueberryViewModel(SaveFile sav, Action onChanged)
    {
        _onChanged = onChanged;
        if (sav is not SAV9SV sv)
            return;

        _sav = sv;
        _board = sv.BlueberryClubRoom.SupportBoard;
        foreach (var prop in typeof(BlueberrySupportBoard9).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(bool) || !prop.CanRead || !prop.CanWrite)
                continue;
            Purchases.Add(new StyleToggleViewModel(_board, prop, onChanged));
        }

        var quests = sv.BlueberryQuestRecord;
        _loading = true;
        QuestsSolo = (int)quests.QuestsDoneSolo;
        QuestsGroup = (int)quests.QuestsDoneGroup;
        _loading = false;

        IsSupported = Purchases.Count > 0;
        RefreshSummary();
    }

    public bool IsSupported { get; }
    public ObservableCollection<StyleToggleViewModel> Purchases { get; } = [];

    [ObservableProperty] private int _questsSolo;
    [ObservableProperty] private int _questsGroup;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    partial void OnQuestsSoloChanged(int value)
    {
        if (_loading || _sav is null)
            return;
        _sav.BlueberryQuestRecord.QuestsDoneSolo = (uint)Math.Max(0, value);
        _onChanged();
    }

    partial void OnQuestsGroupChanged(int value)
    {
        if (_loading || _sav is null)
            return;
        _sav.BlueberryQuestRecord.QuestsDoneGroup = (uint)Math.Max(0, value);
        _onChanged();
    }

    private void RefreshSummary()
    {
        var bought = Purchases.Count(p => !p.IsUnreadFlag && p.Value);
        var total = Purchases.Count(p => !p.IsUnreadFlag);
        Summary = $"{bought} of {total} perks bought";
    }

    /// <summary>Marks every perk bought, clearing the "new" badges as the game would, as one change.</summary>
    [RelayCommand]
    public void BuyAllPerks()
    {
        if (_board is null)
            return;
        foreach (var row in Purchases)
            row.SetQuietly(!row.IsUnreadFlag);
        RefreshSummary();
        Status = "All club perks marked as purchased.";
        _onChanged();
    }

    /// <summary>Re-reads the board after something else wrote to it.</summary>
    public void Reload()
    {
        foreach (var row in Purchases)
            row.Reload();
        RefreshSummary();
    }
}
