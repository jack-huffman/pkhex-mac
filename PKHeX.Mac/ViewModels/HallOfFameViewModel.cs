using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The Hall of Fame record: the teams this save beat the game with.
/// </summary>
/// <remarks>
/// Three generations keep a readable record and each stores it differently — Gen 1
/// and Gen 6 hand out <c>ref struct</c> views over the save's bytes, which cannot be
/// held onto, so every member is copied into a plain row as it is read. Gen 2, 4, 5
/// and later either drop the record or keep only an opaque blob and a clear time; the
/// clear time itself shows up in the Extras tab as <c>SecondsToFame</c>.
///
/// The record is history rather than live state, so this shows it and can clear an
/// entry, but does not offer to rewrite individual members.
/// </remarks>
public partial class HallOfFameViewModel : ObservableObject
{
    private readonly HallOfFame6? _fame6;
    private readonly Action _onChanged;

    public HallOfFameViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _onChanged = onChanged;

        switch (sav)
        {
            case SAV1 sav1:
                LoadGen1(sav1, strings);
                break;
            case SAV3 sav3:
                LoadGen3(sav3, strings);
                break;
            case SAV6XY xy:
                _fame6 = xy.HallOfFame;
                LoadGen6(strings, sav.Language);
                CanClear = true;
                break;
            case SAV6AO ao:
                _fame6 = ao.HallOfFame;
                LoadGen6(strings, sav.Language);
                CanClear = true;
                break;
        }

        IsSupported = Teams.Count > 0;
        if (IsSupported)
            SelectedTeam = Teams.FirstOrDefault(t => !t.IsEmpty) ?? Teams[0];
        RefreshSummary();
    }

    public bool IsSupported { get; }

    /// <summary>Only Gen 6 exposes a per-entry clear.</summary>
    public bool CanClear { get; }

    public ObservableCollection<FameTeamViewModel> Teams { get; } = [];

    [ObservableProperty] private FameTeamViewModel? _selectedTeam;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _usedOnly = true;

    public IEnumerable<FameTeamViewModel> Visible => UsedOnly ? Teams.Where(t => !t.IsEmpty) : Teams;

    partial void OnUsedOnlyChanged(bool value) => OnPropertyChanged(nameof(Visible));

    private void RefreshSummary()
    {
        var used = Teams.Count(t => !t.IsEmpty);
        Summary = IsSupported
            ? $"{used} of {Teams.Count} entries recorded"
            : string.Empty;
    }

    private void LoadGen1(SAV1 sav, GameStrings strings)
    {
        var reader = sav.HallOfFame;
        for (int team = 0; team < HallOfFameReader1.TeamCount; team++)
        {
            var members = new List<FameMemberViewModel>();
            for (int slot = 0; slot < HallOfFameReader1.SlotsPerTeam; slot++)
            {
                // ref struct: read everything out before moving on.
                var entity = reader.GetEntity(team, slot);
                // IsEmpty tests the 0xFF sentinel, so a zeroed slot slips past it.
                if (entity.IsEmpty || entity.Species == 0)
                    continue;
                members.Add(FameMemberViewModel.Create(entity.Species, 0, entity.Level, entity.Nickname,
                                                       shiny: false, strings));
            }
            Teams.Add(new FameTeamViewModel($"Entry {team + 1}", members));
        }
    }

    private void LoadGen3(SAV3 sav, GameStrings strings)
    {
        HallFame3Entry[] entries;
        try
        {
            entries = HallFame3Entry.GetEntries(sav);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or NotSupportedException)
        {
            return; // some Gen 3 variants keep no readable record
        }
        for (int i = 0; i < entries.Length; i++)
        {
            var members = new List<FameMemberViewModel>();
            foreach (var member in entries[i].Team)
            {
                if (member.Species == 0)
                    continue;
                members.Add(FameMemberViewModel.Create(member.Species, member.Form, (byte)member.Level,
                                                       member.Nickname, shiny: false, strings));
            }
            Teams.Add(new FameTeamViewModel($"Entry {i + 1}", members));
        }
    }

    private void LoadGen6(GameStrings strings, int language)
    {
        if (_fame6 is null)
            return;
        for (int team = 0; team < HallOfFame6.Entries; team++)
        {
            var members = new List<FameMemberViewModel>();
            for (int slot = 0; slot < HallOfFame6.PokeCount; slot++)
            {
                // The language only matters for the setters, which are never used here.
                var entity = new HallFame6Entity(_fame6.GetEntity(team, slot), language);
                if (entity.Species == 0)
                    continue;
                members.Add(FameMemberViewModel.Create(entity.Species, entity.Form, (byte)entity.Level,
                                                       entity.Nickname, entity.IsShiny, strings));
            }
            // Entry 1 is the first clear; the rest are the most recent fifteen.
            var label = team == 0 ? "First clear" : $"Recent {team}";
            Teams.Add(new FameTeamViewModel(label, members));
        }
    }

    /// <summary>Wipes the selected entry, where the format allows it.</summary>
    [RelayCommand]
    public void ClearSelected()
    {
        if (_fame6 is null || SelectedTeam is not { } team)
        {
            Status = "Pick an entry first.";
            return;
        }
        var index = Teams.IndexOf(team);
        if (index < 0)
            return;
        _fame6.ClearEntry(index);
        Teams[index] = new FameTeamViewModel(team.Label, []);
        SelectedTeam = Teams[index];
        RefreshSummary();
        OnPropertyChanged(nameof(Visible));
        Status = $"Cleared {team.Label}.";
        _onChanged();
    }
}

/// <summary>One recorded team.</summary>
public sealed class FameTeamViewModel
{
    public FameTeamViewModel(string label, List<FameMemberViewModel> members)
    {
        Label = label;
        Members = members;
        IsEmpty = members.Count == 0;
        Detail = IsEmpty
            ? "no record"
            : string.Join(", ", members.Select(m => m.SpeciesName));
    }

    public string Label { get; }
    public IReadOnlyList<FameMemberViewModel> Members { get; }
    public bool IsEmpty { get; }
    public string Detail { get; }

    public override string ToString() => $"{Label} — {Detail}";
}

/// <summary>One member of a recorded team, copied out of the save's bytes.</summary>
public sealed class FameMemberViewModel
{
    private FameMemberViewModel(string speciesName, string nickname, string levelText,
                               Bitmap? sprite, bool isShiny)
    {
        SpeciesName = speciesName;
        Nickname = nickname;
        LevelText = levelText;
        Sprite = sprite;
        IsShiny = isShiny;
    }

    public static FameMemberViewModel Create(ushort species, byte form, byte level, string nickname,
                                            bool shiny, GameStrings strings)
    {
        var name = strings.SpeciesName(species);
        var nick = string.IsNullOrWhiteSpace(nickname) || nickname == name ? string.Empty : nickname;
        var sprite = SpriteService.GetSprite(species, form, 0, 0, shiny, EntityContext.None);
        return new FameMemberViewModel(name, nick, $"Lv. {level}", sprite, shiny);
    }

    public string SpeciesName { get; }
    public string Nickname { get; }
    public string LevelText { get; }
    public Bitmap? Sprite { get; }
    public bool IsShiny { get; }
    public bool HasNickname => Nickname.Length > 0;
}
