using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Detail/editor pane for the currently selected Pokémon.
/// Edits are applied to a clone and written back to the save on Apply.
/// </summary>
public partial class PokemonDetailViewModel : ObservableObject
{
    private readonly GameStrings _strings;
    private PKM? _pk; // working clone
    private bool _loading;

    public PokemonDetailViewModel(GameStrings strings)
    {
        _strings = strings;
    }

    [ObservableProperty] private bool _hasPokemon;
    [ObservableProperty] private Bitmap? _artwork;
    [ObservableProperty] private Bitmap? _ballSprite;
    [ObservableProperty] private Bitmap? _heldItemSprite;

    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private int _level = 1;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private string _genderSymbol = string.Empty;
    [ObservableProperty] private string _typeText = string.Empty;
    [ObservableProperty] private string _natureName = string.Empty;
    [ObservableProperty] private string _abilityName = string.Empty;
    [ObservableProperty] private string _heldItemName = string.Empty;
    [ObservableProperty] private string _otName = string.Empty;
    [ObservableProperty] private string _tidText = string.Empty;
    [ObservableProperty] private string _metText = string.Empty;
    [ObservableProperty] private string _pidText = string.Empty;
    [ObservableProperty] private string _ecText = string.Empty;

    public List<string> MoveNames { get; } = [];
    public List<StatRowViewModel> Stats { get; } = [];

    [ObservableProperty] private bool _isLegal;
    [ObservableProperty] private string _legalityReport = string.Empty;

    [ObservableProperty] private bool _isDirty;

    public PKM? Pokemon => _pk;

    public void Load(PKM? pk)
    {
        _loading = true;
        try
        {
            _pk = pk?.Clone();
            if (_pk is null || _pk.Species == 0)
            {
                HasPokemon = false;
                IsDirty = false;
                return;
            }

            var p = _pk;
            HasPokemon = true;
            Artwork = SpriteService.GetPokemonArtwork(p);
            BallSprite = SpriteService.GetBallSprite(p.Ball);
            HeldItemSprite = SpriteService.GetItemSprite(p.HeldItem);

            SpeciesName = Name(_strings.specieslist, p.Species);
            Nickname = p.Nickname;
            Level = p.CurrentLevel;
            IsShiny = p.IsShiny;
            GenderSymbol = p.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };

            var pi = p.PersonalInfo;
            var t1 = Name(_strings.types, pi.Type1);
            var t2 = Name(_strings.types, pi.Type2);
            TypeText = pi.Type1 == pi.Type2 ? t1 : $"{t1} / {t2}";

            NatureName = Name(_strings.natures, (int)p.Nature);
            AbilityName = Name(_strings.abilitylist, p.Ability);
            HeldItemName = p.HeldItem > 0 ? Name(_strings.itemlist, p.HeldItem) : "None";
            OtName = p.OriginalTrainerName;
            TidText = $"{p.DisplayTID:D6} / {p.DisplaySID:D4}";
            MetText = $"Met at Lv. {p.MetLevel}";
            PidText = $"{p.PID:X8}";
            EcText = $"{p.EncryptionConstant:X8}";

            MoveNames.Clear();
            Span<ushort> moves = [p.Move1, p.Move2, p.Move3, p.Move4];
            foreach (var m in moves)
            {
                if (m != 0)
                    MoveNames.Add(Name(_strings.movelist, m));
            }
            OnPropertyChanged(nameof(MoveNames));

            LoadStats(p);
            RunLegality(p);
            IsDirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadStats(PKM p)
    {
        Stats.Clear();
        p.ResetPartyStats();
        string[] labels = ["HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed"];
        int[] ivs = [p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE];
        int[] evs = [p.EV_HP, p.EV_ATK, p.EV_DEF, p.EV_SPA, p.EV_SPD, p.EV_SPE];
        int[] stats = [p.Stat_HPMax, p.Stat_ATK, p.Stat_DEF, p.Stat_SPA, p.Stat_SPD, p.Stat_SPE];
        for (int i = 0; i < 6; i++)
            Stats.Add(new StatRowViewModel(labels[i], ivs[i], evs[i], stats[i]));
        OnPropertyChanged(nameof(Stats));
    }

    private void RunLegality(PKM p)
    {
        var la = new LegalityAnalysis(p);
        IsLegal = la.Valid;
        LegalityReport = la.Report();
    }

    private static string Name(IReadOnlyList<string> list, int index) =>
        (uint)index < list.Count ? list[index] : $"#{index}";

    partial void OnNicknameChanged(string value)
    {
        if (_loading || _pk is null)
            return;
        _pk.Nickname = value;
        _pk.IsNicknamed = value != SpeciesName;
        MarkDirty();
    }

    partial void OnLevelChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        var lvl = Math.Clamp(value, 1, 100);
        _pk.CurrentLevel = (byte)lvl;
        LoadStats(_pk);
        MarkDirty();
    }

    partial void OnIsShinyChanged(bool value)
    {
        if (_loading || _pk is null)
            return;
        if (value == _pk.IsShiny)
            return;
        if (value)
            CommonEdits.SetShiny(_pk);
        else
            _pk.SetUnshiny();
        Artwork = SpriteService.GetPokemonArtwork(_pk);
        PidText = $"{_pk.PID:X8}";
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (_pk is null)
            return;
        RunLegality(_pk);
        IsDirty = true;
    }
}

public class StatRowViewModel(string label, int iv, int ev, int stat)
{
    public string Label => label;
    public int IV => iv;
    public int EV => ev;
    public int Stat => stat;
}
