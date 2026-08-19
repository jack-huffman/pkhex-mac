using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;
using CoreSpeciesName = PKHeX.Core.SpeciesName;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Full editor pane for the currently selected Pokémon.
/// Edits are applied to a working clone and written back to the save on Apply.
/// </summary>
public partial class PokemonDetailViewModel : ObservableObject
{
    private readonly GameStrings _strings;
    private FilteredGameDataSource? _sources;
    private SaveFile? _sav;
    private PKM? _pk; // working clone
    private bool _loading;

    public PokemonDetailViewModel(GameStrings strings)
    {
        _strings = strings;
        for (int i = 0; i < 6; i++)
            Stats.Add(new StatEditRowViewModel(this, i));
    }

    public void SetContext(SaveFile sav, FilteredGameDataSource sources)
    {
        _sav = sav;
        _sources = sources;
        SpeciesChoices = sources.Species;
        ItemChoices = sources.Items;
        BallChoices = sources.Balls;
        NatureChoices = sources.Natures;
        MoveChoices = sources.Moves;
        VersionChoices = sources.Games;
        LanguageChoices = sources.Languages;
        OnPropertyChanged(string.Empty); // refresh all bindings
    }

    // ---- Choice lists ----
    [ObservableProperty] private IReadOnlyList<ComboItem> _speciesChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _itemChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _ballChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _natureChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _moveChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _versionChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _languageChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _abilityChoices = [];
    [ObservableProperty] private IReadOnlyList<string> _formChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _metLocationChoices = [];
    public IReadOnlyList<string> GenderChoices { get; } = ["♂ Male", "♀ Female", "— Genderless"];

    // ---- Header / display ----
    [ObservableProperty] private bool _hasPokemon;
    [ObservableProperty] private Bitmap? _artwork;
    [ObservableProperty] private Bitmap? _ballSprite;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _typeText = string.Empty;
    [ObservableProperty] private string _pidText = string.Empty;
    [ObservableProperty] private string _ecText = string.Empty;
    [ObservableProperty] private bool _isLegal;
    [ObservableProperty] private string _legalityReport = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasForms;
    [ObservableProperty] private bool _hasRelearnMoves;
    [ObservableProperty] private bool _canEditGender;

    // ---- Main tab ----
    [ObservableProperty] private int _speciesValue;
    [ObservableProperty] private int _formIndex;
    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private int _level = 1;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private int _natureValue;
    [ObservableProperty] private int _abilityIndex;
    [ObservableProperty] private int _heldItemValue;
    [ObservableProperty] private int _ballValue;
    [ObservableProperty] private int _languageValue;
    [ObservableProperty] private int _genderIndex;
    [ObservableProperty] private int _friendship;

    // ---- Moves tab ----
    [ObservableProperty] private int _move1;
    [ObservableProperty] private int _move2;
    [ObservableProperty] private int _move3;
    [ObservableProperty] private int _move4;
    [ObservableProperty] private int _ppUps1;
    [ObservableProperty] private int _ppUps2;
    [ObservableProperty] private int _ppUps3;
    [ObservableProperty] private int _ppUps4;
    [ObservableProperty] private string _pp1 = string.Empty;
    [ObservableProperty] private string _pp2 = string.Empty;
    [ObservableProperty] private string _pp3 = string.Empty;
    [ObservableProperty] private string _pp4 = string.Empty;
    [ObservableProperty] private int _relearn1;
    [ObservableProperty] private int _relearn2;
    [ObservableProperty] private int _relearn3;
    [ObservableProperty] private int _relearn4;

    // ---- Met tab ----
    [ObservableProperty] private int _versionValue;
    [ObservableProperty] private int _metLocationValue;
    [ObservableProperty] private int _metLevel;
    [ObservableProperty] private DateTimeOffset? _metDate;
    [ObservableProperty] private bool _fatefulEncounter;
    [ObservableProperty] private string _otName = string.Empty;
    [ObservableProperty] private int _otGenderIndex;
    [ObservableProperty] private uint _displayTid;
    [ObservableProperty] private uint _displaySid;

    public ObservableCollection<StatEditRowViewModel> Stats { get; } = [];

    public PKM? Pokemon => _pk;
    public int MaxIV => _pk?.MaxIV ?? 31;
    public int MaxEV => _pk?.MaxEV ?? 252;

    // =====================================================================
    // Load
    // =====================================================================

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

            SpeciesValue = p.Species;
            RebuildFormList(p);
            FormIndex = p.Form < FormChoices.Count ? p.Form : 0;
            Nickname = p.Nickname;
            Level = p.CurrentLevel;
            IsShiny = p.IsShiny;
            NatureValue = (int)p.Nature;
            RebuildAbilityList(p);
            HeldItemValue = p.HeldItem;
            BallValue = p.Ball;
            LanguageValue = p.Language;
            GenderIndex = p.Gender;
            Friendship = p.CurrentFriendship;
            CanEditGender = !(p.PersonalInfo.Genderless || p.PersonalInfo.OnlyMale || p.PersonalInfo.OnlyFemale);

            Move1 = p.Move1; Move2 = p.Move2; Move3 = p.Move3; Move4 = p.Move4;
            PpUps1 = p.Move1_PPUps; PpUps2 = p.Move2_PPUps;
            PpUps3 = p.Move3_PPUps; PpUps4 = p.Move4_PPUps;
            RefreshPP(p);
            HasRelearnMoves = p.Format >= 6;
            Relearn1 = p.RelearnMove1; Relearn2 = p.RelearnMove2;
            Relearn3 = p.RelearnMove3; Relearn4 = p.RelearnMove4;

            VersionValue = (int)p.Version;
            RebuildMetLocationList(p);
            MetLocationValue = p.MetLocation;
            MetLevel = p.MetLevel;
            MetDate = p.MetDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
            FatefulEncounter = p.FatefulEncounter;
            OtName = p.OriginalTrainerName;
            OtGenderIndex = p.OriginalTrainerGender;
            DisplayTid = p.DisplayTID;
            DisplaySid = p.DisplaySID;

            RefreshDerived(p);
            IsDirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshDerived(PKM p)
    {
        Artwork = SpriteService.GetPokemonArtwork(p);
        BallSprite = SpriteService.GetBallSprite(p.Ball);
        SpeciesName = Name(_strings.specieslist, p.Species);
        var pi = p.PersonalInfo;
        var t1 = Name(_strings.types, pi.Type1);
        var t2 = Name(_strings.types, pi.Type2);
        TypeText = pi.Type1 == pi.Type2 ? t1 : $"{t1} / {t2}";
        PidText = $"{p.PID:X8}";
        EcText = $"{p.EncryptionConstant:X8}";
        RefreshStats();
        RunLegality(p);
        OnPropertyChanged(nameof(MaxIV));
        OnPropertyChanged(nameof(MaxEV));
    }

    private void RebuildFormList(PKM p)
    {
        var forms = FormConverter.GetFormList(p.Species, _strings.types, _strings.forms, GameInfo.GenderSymbolUnicode, p.Context);
        FormChoices = forms;
        HasForms = forms.Length > 1;
    }

    private void RebuildAbilityList(PKM p)
    {
        if (_sources is null)
            return;
        AbilityChoices = _sources.GetAbilityList(p.PersonalInfo);
        AbilityIndex = Math.Max(0, p.AbilityNumber switch { 1 => 0, 2 => 1, 4 => 2, _ => 0 });
    }

    private void RebuildMetLocationList(PKM p)
    {
        MetLocationChoices = GameInfo.GetLocationList(p.Version, p.Context, egg: false);
    }

    public void RefreshStats()
    {
        if (_pk is null)
            return;
        _pk.ResetPartyStats();
        foreach (var row in Stats)
            row.Refresh(_pk);
    }

    private void RunLegality(PKM p)
    {
        var la = new LegalityAnalysis(p);
        IsLegal = la.Valid;
        LegalityReport = la.Report();
    }

    private static string Name(IReadOnlyList<string> list, int index) =>
        (uint)index < list.Count ? list[index] : $"#{index}";

    internal void MarkDirty()
    {
        if (_pk is null || _loading)
            return;
        RunLegality(_pk);
        IsDirty = true;
    }

    // =====================================================================
    // Edit handlers
    // =====================================================================

    partial void OnSpeciesValueChanged(int value)
    {
        if (_loading || _pk is null || value <= 0)
            return;
        var p = _pk;
        var wasNicknamed = p.IsNicknamed;
        p.Species = (ushort)value;
        p.Form = 0;
        p.Gender = p.GetSaneGender();
        if (!wasNicknamed)
            p.Nickname = CoreSpeciesName.GetSpeciesNameGeneration(p.Species, p.Language, p.Format);
        _loading = true;
        RebuildFormList(p);
        FormIndex = 0;
        RebuildAbilityList(p);
        Nickname = p.Nickname;
        GenderIndex = p.Gender;
        CanEditGender = !(p.PersonalInfo.Genderless || p.PersonalInfo.OnlyMale || p.PersonalInfo.OnlyFemale);
        _loading = false;
        p.RefreshAbility(AbilityIndex);
        RefreshDerived(p);
        MarkDirty();
    }

    partial void OnFormIndexChanged(int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        _pk.Form = (byte)value;
        _loading = true;
        RebuildAbilityList(_pk);
        _loading = false;
        _pk.RefreshAbility(AbilityIndex);
        RefreshDerived(_pk);
        MarkDirty();
    }

    partial void OnNicknameChanged(string value)
    {
        if (_loading || _pk is null)
            return;
        _pk.Nickname = value;
        _pk.IsNicknamed = value != CoreSpeciesName.GetSpeciesNameGeneration(_pk.Species, _pk.Language, _pk.Format);
        MarkDirty();
    }

    partial void OnLevelChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.CurrentLevel = (byte)Math.Clamp(value, 1, 100);
        RefreshStats();
        MarkDirty();
    }

    partial void OnIsShinyChanged(bool value)
    {
        if (_loading || _pk is null || value == _pk.IsShiny)
            return;
        if (value)
            CommonEdits.SetShiny(_pk);
        else
            _pk.SetUnshiny();
        Artwork = SpriteService.GetPokemonArtwork(_pk);
        PidText = $"{_pk.PID:X8}";
        MarkDirty();
    }

    partial void OnNatureValueChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.Nature = (Nature)value;
        RefreshStats();
        MarkDirty();
    }

    partial void OnAbilityIndexChanged(int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        _pk.RefreshAbility(value);
        MarkDirty();
    }

    partial void OnHeldItemValueChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.HeldItem = value;
        MarkDirty();
    }

    partial void OnBallValueChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.Ball = (byte)value;
        BallSprite = SpriteService.GetBallSprite(_pk.Ball);
        MarkDirty();
    }

    partial void OnLanguageValueChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.Language = value;
        MarkDirty();
    }

    partial void OnGenderIndexChanged(int value)
    {
        if (_loading || _pk is null || value < 0 || value == _pk.Gender)
            return;
        var p = _pk;
        if (p.PersonalInfo.Genderless || p.PersonalInfo.OnlyMale || p.PersonalInfo.OnlyFemale)
            return;
        p.Gender = (byte)value;
        if (p.Format <= 5)
            p.SetPIDGender(p.Gender);
        RefreshDerived(p);
        MarkDirty();
    }

    partial void OnFriendshipChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.CurrentFriendship = (byte)Math.Clamp(value, 0, 255);
        MarkDirty();
    }

    private void RefreshPP(PKM p)
    {
        Pp1 = p.Move1 == 0 ? "—" : $"{p.Move1_PP} PP";
        Pp2 = p.Move2 == 0 ? "—" : $"{p.Move2_PP} PP";
        Pp3 = p.Move3 == 0 ? "—" : $"{p.Move3_PP} PP";
        Pp4 = p.Move4 == 0 ? "—" : $"{p.Move4_PP} PP";
    }

    private void SetMove(int index, int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        switch (index)
        {
            case 0: _pk.Move1 = (ushort)value; break;
            case 1: _pk.Move2 = (ushort)value; break;
            case 2: _pk.Move3 = (ushort)value; break;
            case 3: _pk.Move4 = (ushort)value; break;
        }
        _pk.HealPP();
        RefreshPP(_pk);
        MarkDirty();
    }

    private void SetPPUps(int index, int value)
    {
        if (_loading || _pk is null || value is < 0 or > 3)
            return;
        switch (index)
        {
            case 0: _pk.Move1_PPUps = value; break;
            case 1: _pk.Move2_PPUps = value; break;
            case 2: _pk.Move3_PPUps = value; break;
            case 3: _pk.Move4_PPUps = value; break;
        }
        _pk.HealPP();
        RefreshPP(_pk);
        MarkDirty();
    }

    partial void OnPpUps1Changed(int value) => SetPPUps(0, value);
    partial void OnPpUps2Changed(int value) => SetPPUps(1, value);
    partial void OnPpUps3Changed(int value) => SetPPUps(2, value);
    partial void OnPpUps4Changed(int value) => SetPPUps(3, value);

    partial void OnMove1Changed(int value) => SetMove(0, value);
    partial void OnMove2Changed(int value) => SetMove(1, value);
    partial void OnMove3Changed(int value) => SetMove(2, value);
    partial void OnMove4Changed(int value) => SetMove(3, value);

    private void SetRelearn(int index, int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        switch (index)
        {
            case 0: _pk.RelearnMove1 = (ushort)value; break;
            case 1: _pk.RelearnMove2 = (ushort)value; break;
            case 2: _pk.RelearnMove3 = (ushort)value; break;
            case 3: _pk.RelearnMove4 = (ushort)value; break;
        }
        MarkDirty();
    }

    partial void OnRelearn1Changed(int value) => SetRelearn(0, value);
    partial void OnRelearn2Changed(int value) => SetRelearn(1, value);
    partial void OnRelearn3Changed(int value) => SetRelearn(2, value);
    partial void OnRelearn4Changed(int value) => SetRelearn(3, value);

    partial void OnVersionValueChanged(int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        _pk.Version = (GameVersion)value;
        _loading = true;
        RebuildMetLocationList(_pk);
        _loading = false;
        MarkDirty();
    }

    partial void OnMetLocationValueChanged(int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        _pk.MetLocation = (ushort)value;
        MarkDirty();
    }

    partial void OnMetLevelChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.MetLevel = (byte)Math.Clamp(value, 0, 100);
        MarkDirty();
    }

    partial void OnMetDateChanged(DateTimeOffset? value)
    {
        if (_loading || _pk is null)
            return;
        _pk.MetDate = value is { } d ? DateOnly.FromDateTime(d.DateTime) : null;
        MarkDirty();
    }

    partial void OnFatefulEncounterChanged(bool value)
    {
        if (_loading || _pk is null)
            return;
        _pk.FatefulEncounter = value;
        MarkDirty();
    }

    partial void OnOtNameChanged(string value)
    {
        if (_loading || _pk is null)
            return;
        _pk.OriginalTrainerName = value;
        MarkDirty();
    }

    partial void OnOtGenderIndexChanged(int value)
    {
        if (_loading || _pk is null || value is < 0 or > 1)
            return;
        _pk.OriginalTrainerGender = (byte)value;
        MarkDirty();
    }

    partial void OnDisplayTidChanged(uint value)
    {
        if (_loading || _pk is null)
            return;
        _pk.DisplayTID = value;
        MarkDirty();
    }

    partial void OnDisplaySidChanged(uint value)
    {
        if (_loading || _pk is null)
            return;
        _pk.DisplaySID = value;
        MarkDirty();
    }

    // =====================================================================
    // Commands
    // =====================================================================

    [RelayCommand]
    public void MaxIVs()
    {
        if (_pk is null)
            return;
        _pk.IV_HP = _pk.IV_ATK = _pk.IV_DEF = _pk.IV_SPA = _pk.IV_SPD = _pk.IV_SPE = _pk.MaxIV;
        RefreshStats();
        MarkDirty();
    }

    [RelayCommand]
    public void ClearEVs()
    {
        if (_pk is null)
            return;
        _pk.EV_HP = _pk.EV_ATK = _pk.EV_DEF = _pk.EV_SPA = _pk.EV_SPD = _pk.EV_SPE = 0;
        RefreshStats();
        MarkDirty();
    }

    [RelayCommand]
    public void HealPokemon()
    {
        if (_pk is null)
            return;
        _pk.Heal();
        RefreshStats();
        MarkDirty();
    }

    /// <summary>Applies a Showdown set from pasted text to the current Pokémon.</summary>
    public bool ImportShowdownSet(string text, out string message)
    {
        message = string.Empty;
        if (_pk is null)
        {
            message = "Select a Pokémon first.";
            return false;
        }
        var set = new ShowdownSet(text);
        if (set.Species == 0)
        {
            message = "Clipboard does not contain a recognizable Showdown set.";
            return false;
        }
        _pk.ApplySetDetails(set);
        Load(_pk);
        IsDirty = true;
        message = $"Applied Showdown set for {Name(_strings.specieslist, _pk.Species)}.";
        return true;
    }

    public string? GetShowdownText() => _pk is null || _pk.Species == 0 ? null : new ShowdownSet(_pk).Text;
}

/// <summary>One editable stat row (IV/EV in, computed stat out).</summary>
public partial class StatEditRowViewModel : ObservableObject
{
    private static readonly string[] Labels = ["HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed"];
    private readonly PokemonDetailViewModel _parent;
    private readonly int _index;
    private bool _loading;

    public StatEditRowViewModel(PokemonDetailViewModel parent, int index)
    {
        _parent = parent;
        _index = index;
    }

    public string Label => Labels[_index];

    [ObservableProperty] private int _iv;
    [ObservableProperty] private int _ev;
    [ObservableProperty] private int _stat;

    public void Refresh(PKM p)
    {
        _loading = true;
        (Iv, Ev, Stat) = _index switch
        {
            0 => (p.IV_HP, p.EV_HP, (int)p.Stat_HPMax),
            1 => (p.IV_ATK, p.EV_ATK, (int)p.Stat_ATK),
            2 => (p.IV_DEF, p.EV_DEF, (int)p.Stat_DEF),
            3 => (p.IV_SPA, p.EV_SPA, (int)p.Stat_SPA),
            4 => (p.IV_SPD, p.EV_SPD, (int)p.Stat_SPD),
            _ => (p.IV_SPE, p.EV_SPE, (int)p.Stat_SPE),
        };
        _loading = false;
    }

    partial void OnIvChanged(int value)
    {
        if (_loading || _parent.Pokemon is not { } p)
            return;
        var v = Math.Clamp(value, 0, p.MaxIV);
        switch (_index)
        {
            case 0: p.IV_HP = v; break;
            case 1: p.IV_ATK = v; break;
            case 2: p.IV_DEF = v; break;
            case 3: p.IV_SPA = v; break;
            case 4: p.IV_SPD = v; break;
            case 5: p.IV_SPE = v; break;
        }
        _parent.RefreshStats();
        _parent.MarkDirty();
    }

    partial void OnEvChanged(int value)
    {
        if (_loading || _parent.Pokemon is not { } p)
            return;
        var v = Math.Clamp(value, 0, p.MaxEV);
        switch (_index)
        {
            case 0: p.EV_HP = v; break;
            case 1: p.EV_ATK = v; break;
            case 2: p.EV_DEF = v; break;
            case 3: p.EV_SPA = v; break;
            case 4: p.EV_SPD = v; break;
            case 5: p.EV_SPE = v; break;
        }
        _parent.RefreshStats();
        _parent.MarkDirty();
    }
}
