using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
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
        History = new PokemonHistoryViewModel(strings, MarkDirty);
        Ribbons = new RibbonsViewModel(strings, MarkDirty);
    }

    /// <summary>Trainer &amp; History group (handler, memories, contest, markings, HOME).</summary>
    public PokemonHistoryViewModel History { get; }

    /// <summary>Ribbons &amp; Marks group.</summary>
    public RibbonsViewModel Ribbons { get; }

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
        History.SetLanguageChoices(sources.Languages);
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
    [ObservableProperty] private string _type1Name = string.Empty;
    [ObservableProperty] private string _type2Name = string.Empty;
    [ObservableProperty] private bool _hasType2;
    [ObservableProperty] private IBrush? _type1Brush;
    [ObservableProperty] private IBrush? _type2Brush;
    [ObservableProperty] private string _levelBadge = string.Empty;

    public Bitmap? ShinyIcon => SpriteService.GetOverlay("rare_icon");
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
    [ObservableProperty] private uint _experience;
    [ObservableProperty] private int _statNatureValue;
    [ObservableProperty] private bool _hasStatNature;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _supportsFavorite;
    [ObservableProperty] private int _pokerusStrain;
    [ObservableProperty] private int _pokerusDays;
    [ObservableProperty] private bool _formArgumentVisible;
    [ObservableProperty] private uint _formArgument;

    // ---- Tera type (PK9) ----
    [ObservableProperty] private bool _supportsTeraType;
    [ObservableProperty] private IReadOnlyList<ComboItem> _teraTypeChoices = [];
    [ObservableProperty] private int _teraTypeOriginalValue;
    [ObservableProperty] private int _teraTypeOverrideValue;

    // ---- Hyper training ----
    [ObservableProperty] private bool _supportsHyperTraining;
    [ObservableProperty] private bool _htHp;
    [ObservableProperty] private bool _htAtk;
    [ObservableProperty] private bool _htDef;
    [ObservableProperty] private bool _htSpa;
    [ObservableProperty] private bool _htSpd;
    [ObservableProperty] private bool _htSpe;

    // ---- Size ----
    [ObservableProperty] private bool _supportsScalars;
    [ObservableProperty] private bool _supportsScale;
    [ObservableProperty] private int _heightScalar;
    [ObservableProperty] private int _weightScalar;
    [ObservableProperty] private int _scale;
    [ObservableProperty] private string _sizeSummary = string.Empty;

    // ---- Obedience / battle version ----
    [ObservableProperty] private bool _supportsObedience;
    [ObservableProperty] private int _obedienceLevel;
    [ObservableProperty] private bool _supportsBattleVersion;
    [ObservableProperty] private int _battleVersionValue;

    // ---- Egg met data ----
    [ObservableProperty] private bool _isEgg;
    [ObservableProperty] private DateTimeOffset? _eggMetDate;
    [ObservableProperty] private int _eggLocationValue;

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

    // ---- EV budget / nature summary (Stats tab) ----
    [ObservableProperty] private int _evTotal;
    [ObservableProperty] private int _evRemaining;
    [ObservableProperty] private string _evBudgetText = string.Empty;
    [ObservableProperty] private double _evBudgetPercent;
    [ObservableProperty] private bool _evOverLimit;
    [ObservableProperty] private string _natureEffectText = string.Empty;

    /// <summary>Game-legal ceiling on the sum of all EVs (510 from Gen 3 on).</summary>
    public int EvTotalLimit => EffortValues.Max510;

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
                History.Load(null);
                Ribbons.Load(null);
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

            LoadExtendedFields(p);
            History.Load(p);
            Ribbons.Load(p);
            RefreshDerived(p);
            IsDirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Loads the format-dependent field groups (Tera type, hyper training, size,
    /// obedience, Pokérus, egg data…). Each group advertises support so the UI can
    /// hide what this entity format does not carry.
    /// </summary>
    private void LoadExtendedFields(PKM p)
    {
        Experience = p.EXP;
        HasStatNature = p.Format >= 8;
        StatNatureValue = (int)p.StatAlignment;
        PokerusStrain = p.PokerusStrain;
        PokerusDays = p.PokerusDays;
        IsEgg = p.IsEgg;

        SupportsFavorite = p is IFavorite;
        IsFavorite = p is IFavorite { IsFavorite: true };

        FormArgumentVisible = p is IFormArgument && p.Format >= 6;
        FormArgument = p is IFormArgument fa ? fa.FormArgument : 0;

        // Tera type (Scarlet/Violet only).
        SupportsTeraType = p is ITeraType;
        if (p is ITeraType tera)
        {
            if (TeraTypeChoices.Count == 0)
                TeraTypeChoices = BuildTeraTypeChoices();
            TeraTypeOriginalValue = (int)tera.TeraTypeOriginal;
            TeraTypeOverrideValue = (int)tera.TeraTypeOverride;
        }

        // Hyper training (bottle caps).
        SupportsHyperTraining = p is IHyperTrain;
        if (p is IHyperTrain ht)
        {
            HtHp = ht.HT_HP; HtAtk = ht.HT_ATK; HtDef = ht.HT_DEF;
            HtSpa = ht.HT_SPA; HtSpd = ht.HT_SPD; HtSpe = ht.HT_SPE;
        }

        // Size.
        SupportsScalars = p is IScaledSize;
        SupportsScale = p is IScaledSize3;
        if (p is IScaledSize ss)
        {
            HeightScalar = ss.HeightScalar;
            WeightScalar = ss.WeightScalar;
        }
        Scale = p is IScaledSize3 s3 ? s3.Scale : 0;
        RefreshSizeSummary(p);

        SupportsObedience = p is IObedienceLevel;
        ObedienceLevel = p is IObedienceLevel ob ? ob.ObedienceLevel : 0;

        SupportsBattleVersion = p is IBattleVersion;
        BattleVersionValue = p is IBattleVersion bv ? (int)bv.BattleVersion : 0;

        EggMetDate = p.EggMetDate is { } ed
            ? new DateTimeOffset(ed.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        EggLocationValue = p.EggLocation;
    }

    private IReadOnlyList<ComboItem> BuildTeraTypeChoices()
    {
        // The 18 regular types plus Stellar, which uses a sentinel value.
        var list = new List<ComboItem>(20);
        for (int i = 0; i <= TeraTypeUtil.MaxType; i++)
            list.Add(new ComboItem(Name(_strings.types, i), i));
        list.Add(new ComboItem(Name(_strings.types, TeraTypeUtil.StellarTypeDisplayStringIndex), TeraTypeUtil.Stellar));
        return list;
    }

    /// <summary>Choices for the override slot, including "no override".</summary>
    public IReadOnlyList<ComboItem> TeraTypeOverrideChoices =>
        [new ComboItem("(No override)", TeraTypeUtil.OverrideNone), .. TeraTypeChoices];

    private void RefreshSizeSummary(PKM p)
    {
        if (p is not IScaledSize sz)
        {
            SizeSummary = string.Empty;
            return;
        }
        // PK9 rates size from Scale; earlier Gen 8 formats use height/weight scalars.
        if (p is IScaledSize3 sc)
            SizeSummary = $"Scale class: {PokeSizeDetailedUtil.GetSizeRating(sc.Scale)}";
        else
            SizeSummary = $"Height class: {PokeSizeUtil.GetSizeRating(sz.HeightScalar)}";
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
        Type1Name = t1;
        Type2Name = t2;
        HasType2 = pi.Type1 != pi.Type2;
        Type1Brush = TypePalette.GetBrush(pi.Type1);
        Type2Brush = TypePalette.GetBrush(pi.Type2);
        LevelBadge = $"Lv. {p.CurrentLevel}";
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
        RefreshEvBudget();
        RefreshNatureEffect();
    }

    /// <summary>Recomputes the shared EV pool and pushes each row's remaining headroom.</summary>
    private void RefreshEvBudget()
    {
        if (_pk is null)
            return;
        var limit = EvTotalLimit;
        EvTotal = _pk.EVTotal;
        EvRemaining = Math.Max(0, limit - EvTotal);
        EvOverLimit = EvTotal > limit;
        EvBudgetPercent = limit == 0 ? 0 : Math.Min(100.0, EvTotal * 100.0 / limit);
        EvBudgetText = EvOverLimit
            ? $"{EvTotal} / {limit} EVs — over the limit by {EvTotal - limit}"
            : $"{EvTotal} / {limit} EVs · {EvRemaining} left to spend";
        foreach (var row in Stats)
            row.UpdateBudget(EvRemaining);
    }

    /// <summary>Tags the rows the current nature raises and lowers.</summary>
    private void RefreshNatureEffect()
    {
        if (_pk is null)
            return;
        var nature = _pk.StatAlignment;
        var (up, dn) = nature.GetNatureModification();
        // Nature indexes are in the games' internal order (Atk, Def, Spe, SpA, SpD);
        // our rows are HP, Atk, Def, SpA, SpD, Spe.
        int[] internalToRow = [1, 2, 5, 3, 4];
        var upRow = (uint)up < internalToRow.Length ? internalToRow[up] : -1;
        var dnRow = (uint)dn < internalToRow.Length ? internalToRow[dn] : -1;
        var neutral = up == dn;

        for (int i = 0; i < Stats.Count; i++)
            Stats[i].SetNatureEffect(neutral ? 0 : i == upRow ? 1 : i == dnRow ? -1 : 0);

        var natureName = Name(_strings.natures, (int)nature);
        NatureEffectText = neutral
            ? $"{natureName} — no stat changes"
            : $"{natureName} — raises {StatEditRowViewModel.LabelFor(upRow)}, lowers {StatEditRowViewModel.LabelFor(dnRow)}";
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

    // ---- Extended field handlers (Pass 1) ----

    partial void OnExperienceChanged(uint value)
    {
        if (_loading || _pk is null)
            return;
        _pk.EXP = value;
        _loading = true;
        Level = _pk.CurrentLevel; // EXP and level move together
        _loading = false;
        RefreshStats();
        LevelBadge = $"Lv. {_pk.CurrentLevel}";
        MarkDirty();
    }

    partial void OnStatNatureValueChanged(int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        _pk.StatAlignment = (Nature)value;
        RefreshStats();
        MarkDirty();
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        if (_loading || _pk is not IFavorite fav)
            return;
        fav.IsFavorite = value;
        MarkDirty();
    }

    partial void OnPokerusStrainChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.PokerusStrain = Math.Clamp(value, 0, 15);
        MarkDirty();
    }

    partial void OnPokerusDaysChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.PokerusDays = Math.Clamp(value, 0, 4);
        MarkDirty();
    }

    partial void OnFormArgumentChanged(uint value)
    {
        if (_loading || _pk is not IFormArgument fa)
            return;
        fa.FormArgument = value;
        MarkDirty();
    }

    partial void OnTeraTypeOriginalValueChanged(int value)
    {
        if (_loading || _pk is not ITeraType tera || value < 0)
            return;
        tera.TeraTypeOriginal = (MoveType)value;
        MarkDirty();
    }

    partial void OnTeraTypeOverrideValueChanged(int value)
    {
        if (_loading || _pk is not ITeraType tera || value < 0)
            return;
        tera.TeraTypeOverride = (MoveType)value;
        MarkDirty();
    }

    private void SetHyperTrain(int index, bool value)
    {
        if (_loading || _pk is not IHyperTrain ht)
            return;
        switch (index)
        {
            case 0: ht.HT_HP = value; break;
            case 1: ht.HT_ATK = value; break;
            case 2: ht.HT_DEF = value; break;
            case 3: ht.HT_SPA = value; break;
            case 4: ht.HT_SPD = value; break;
            case 5: ht.HT_SPE = value; break;
        }
        RefreshStats();
        MarkDirty();
    }

    partial void OnHtHpChanged(bool value) => SetHyperTrain(0, value);
    partial void OnHtAtkChanged(bool value) => SetHyperTrain(1, value);
    partial void OnHtDefChanged(bool value) => SetHyperTrain(2, value);
    partial void OnHtSpaChanged(bool value) => SetHyperTrain(3, value);
    partial void OnHtSpdChanged(bool value) => SetHyperTrain(4, value);
    partial void OnHtSpeChanged(bool value) => SetHyperTrain(5, value);

    partial void OnHeightScalarChanged(int value)
    {
        if (_loading || _pk is not IScaledSize ss)
            return;
        ss.HeightScalar = (byte)Math.Clamp(value, 0, 255);
        RefreshSizeSummary(_pk!);
        MarkDirty();
    }

    partial void OnWeightScalarChanged(int value)
    {
        if (_loading || _pk is not IScaledSize ss)
            return;
        ss.WeightScalar = (byte)Math.Clamp(value, 0, 255);
        RefreshSizeSummary(_pk!);
        MarkDirty();
    }

    partial void OnScaleChanged(int value)
    {
        if (_loading || _pk is not IScaledSize3 s3)
            return;
        s3.Scale = (byte)Math.Clamp(value, 0, 255);
        RefreshSizeSummary(_pk!);
        MarkDirty();
    }

    partial void OnObedienceLevelChanged(int value)
    {
        if (_loading || _pk is not IObedienceLevel ob)
            return;
        ob.ObedienceLevel = (byte)Math.Clamp(value, 0, 100);
        MarkDirty();
    }

    partial void OnBattleVersionValueChanged(int value)
    {
        if (_loading || _pk is not IBattleVersion bv || value < 0)
            return;
        bv.BattleVersion = (GameVersion)value;
        MarkDirty();
    }

    partial void OnIsEggChanged(bool value)
    {
        if (_loading || _pk is null)
            return;
        _pk.IsEgg = value;
        RefreshDerived(_pk);
        MarkDirty();
    }

    partial void OnEggMetDateChanged(DateTimeOffset? value)
    {
        if (_loading || _pk is null)
            return;
        _pk.EggMetDate = value is { } d ? DateOnly.FromDateTime(d.DateTime) : null;
        MarkDirty();
    }

    partial void OnEggLocationValueChanged(int value)
    {
        if (_loading || _pk is null || value < 0)
            return;
        _pk.EggLocation = (ushort)value;
        MarkDirty();
    }

    // ---- Commands for the new groups ----

    [RelayCommand]
    public void RerollPid()
    {
        if (_pk is null)
            return;
        _pk.SetPIDGender(_pk.Gender);
        _loading = true;
        IsShiny = _pk.IsShiny;
        _loading = false;
        RefreshDerived(_pk);
        MarkDirty();
    }

    [RelayCommand]
    public void RerollEncryptionConstant()
    {
        if (_pk is null)
            return;
        _pk.EncryptionConstant = Util.Rand32();
        EcText = $"{_pk.EncryptionConstant:X8}";
        MarkDirty();
    }

    [RelayCommand]
    public void MaxHyperTraining()
    {
        if (_pk is not IHyperTrain ht)
            return;
        _loading = true;
        HtHp = HtAtk = HtDef = HtSpa = HtSpd = HtSpe = true;
        _loading = false;
        ht.HT_HP = ht.HT_ATK = ht.HT_DEF = ht.HT_SPA = ht.HT_SPD = ht.HT_SPE = true;
        RefreshStats();
        MarkDirty();
    }

    [RelayCommand]
    public void ClearHyperTraining()
    {
        if (_pk is not IHyperTrain ht)
            return;
        _loading = true;
        HtHp = HtAtk = HtDef = HtSpa = HtSpd = HtSpe = false;
        _loading = false;
        ht.HyperTrainFlags = 0;
        RefreshStats();
        MarkDirty();
    }

    [RelayCommand]
    public void RandomizeScale()
    {
        if (_pk is not IScaledSize3 s3)
            return;
        Scale = (byte)Util.Rand.Next(0, 256);
        _ = s3;
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

/// <summary>
/// One editable stat row: IV and EV sliders, the resulting stat, and how the
/// current nature affects it. EV headroom is shared across all six rows, so the
/// row clamps itself to whatever the 510 pool has left.
/// </summary>
public partial class StatEditRowViewModel : ObservableObject
{
    private static readonly string[] Labels = ["HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed"];

    internal static string LabelFor(int index) => (uint)index < Labels.Length ? Labels[index] : "?";

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
    [ObservableProperty] private int _maxIv = 31;
    [ObservableProperty] private int _maxEv = 252;

    /// <summary>Highest EV this row may take right now, given the shared 510 pool.</summary>
    [ObservableProperty] private int _evCeiling = 252;

    // ---- Nature effect: +1 raised, -1 lowered, 0 unaffected ----
    [ObservableProperty] private string _natureBadge = string.Empty;
    [ObservableProperty] private bool _hasNatureBadge;
    [ObservableProperty] private IBrush? _natureBadgeBrush;
    [ObservableProperty] private IBrush? _labelBrush;

    public void Refresh(PKM p)
    {
        _loading = true;
        MaxIv = p.MaxIV;
        MaxEv = p.MaxEV;
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

    /// <summary>Pushes the shared pool's remaining headroom into this row's ceiling.</summary>
    internal void UpdateBudget(int remaining) => EvCeiling = Math.Min(MaxEv, Ev + remaining);

    internal void SetNatureEffect(int direction)
    {
        HasNatureBadge = direction != 0;
        NatureBadge = direction switch { 1 => "▲", -1 => "▼", _ => string.Empty };
        NatureBadgeBrush = direction switch
        {
            1 => new SolidColorBrush(Color.Parse("#FF6961")),   // raised
            -1 => new SolidColorBrush(Color.Parse("#6AA9FF")),  // lowered
            _ => null,
        };
        LabelBrush = NatureBadgeBrush;
    }

    partial void OnIvChanged(int value)
    {
        if (_loading || _parent.Pokemon is not { } p)
            return;
        var v = Math.Clamp(value, 0, p.MaxIV);
        if (v != value)
        {
            Iv = v; // re-enters with the clamped value
            return;
        }
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
        // Never let the six rows sum past the game's 510 ceiling.
        var v = Math.Clamp(value, 0, Math.Min(p.MaxEV, EvCeiling));
        if (v != value)
        {
            Ev = v;
            return;
        }
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
