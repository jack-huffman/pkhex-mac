using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Breeding planner: a species' egg groups and egg moves for the open game, and which
/// partners can actually pass a chosen egg move.
/// </summary>
/// <remarks>
/// Works for every generation because the move data comes from
/// <see cref="GameData.GetLearnSource"/> keyed on the save's own version — egg moves
/// differ sharply between games (Eevee has 2 in Gen 2, 14 in Gen 6, 8 in Gen 9), so a
/// single hard-coded list would be wrong nearly everywhere. Three games have no
/// breeding at all — Gen 1, Let's Go and Legends Arceus — and are reported as such
/// rather than shown as an empty list.
///
/// Inheritance rules also changed: before Gen 6 only the father could pass egg moves,
/// so the partner list is described differently depending on the era.
/// </remarks>
public partial class BreedingViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly ILearnSource _source;
    private readonly IReadOnlyList<ushort> _speciesPool;

    public BreedingViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        _source = GameData.GetLearnSource(sav.Version);
        Generation = sav.Generation;

        // Every species this game knows about, for the partner search.
        var max = sav.MaxSpeciesID;
        var pool = new List<ushort>(max);
        for (ushort species = 1; species <= max; species++)
            pool.Add(species);
        _speciesPool = pool;

        SpeciesOptions = pool
            .Where(sp => sp < strings.specieslist.Length)
            .Select(sp => new SpeciesChoice(sp, strings.specieslist[sp]))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FathersOnly = Generation < 6;
        InheritanceNote = FathersOnly
            ? "In this generation only the father passes egg moves, so the partner below must be male."
            : "From Gen 6 onward either parent can pass egg moves.";

        SupportsBreeding = DetectBreeding();
    }

    private bool DetectBreeding()
    {
        // Rather than hard-code which games dropped breeding, ask the data: a game with
        // breeding has egg moves for at least one common species.
        foreach (ushort probe in new ushort[] { 133, 1, 4, 7, 25, 129 })
        {
            if (probe <= _sav.MaxSpeciesID && _source.GetEggMoves(probe, 0).Length > 0)
                return true;
        }
        return false;
    }

    public int Generation { get; }
    public bool SupportsBreeding { get; }
    public bool FathersOnly { get; }
    public string InheritanceNote { get; }

    public IReadOnlyList<SpeciesChoice> SpeciesOptions { get; }

    [ObservableProperty] private SpeciesChoice? _selectedSpecies;
    [ObservableProperty] private EggMoveViewModel? _selectedEggMove;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _eggGroupText = string.Empty;
    [ObservableProperty] private string _genderText = string.Empty;
    [ObservableProperty] private string _hatchText = string.Empty;
    [ObservableProperty] private Bitmap? _sprite;
    [ObservableProperty] private bool _hasSpecies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlocked))]
    private string _blockedReason = string.Empty;

    [ObservableProperty] private string _partnerSummary = string.Empty;

    public ObservableCollection<EggMoveViewModel> EggMoves { get; } = [];
    public ObservableCollection<PartnerViewModel> Partners { get; } = [];

    public bool HasEggMoves => EggMoves.Count > 0;
    public bool IsBlocked => BlockedReason.Length > 0;

    partial void OnSelectedSpeciesChanged(SpeciesChoice? value)
    {
        if (value is null)
            return;
        Load((ushort)value.Value);
    }

    partial void OnSelectedEggMoveChanged(EggMoveViewModel? value) => BuildPartners(value);

    private void Load(ushort species)
    {
        EggMoves.Clear();
        Partners.Clear();
        BlockedReason = string.Empty;
        PartnerSummary = string.Empty;
        HasSpecies = true;
        Sprite = SpriteService.GetSprite(species, 0, 0, 0, shiny: false, EntityContext.None);

        var pi = _sav.Personal.GetFormEntry(species, 0);
        EggGroupText = EggGroups.Describe(pi);
        GenderText = DescribeGender(pi);
        HatchText = pi is PersonalInfo { HatchCycles: > 0 } concrete
            ? $"{concrete.HatchCycles} hatch cycles"
            : string.Empty;

        if (!SupportsBreeding)
        {
            BlockedReason = $"{GameName} has no breeding, so there are no egg moves to plan.";
        }
        else if (!EggGroups.CanBreed(pi))
        {
            BlockedReason = $"{_strings.SpeciesName(species)} is in the Undiscovered group and cannot breed.";
        }

        var moves = _source.GetEggMoves(species, 0);
        foreach (var move in moves)
            EggMoves.Add(new EggMoveViewModel(move, _strings, _sav.Context));
        OnPropertyChanged(nameof(HasEggMoves));

        Summary = BlockedReason.Length > 0
            ? EggGroupText
            : $"{EggGroupText} · {moves.Length} egg move{(moves.Length == 1 ? string.Empty : "s")} in {GameName}";

        SelectedEggMove = EggMoves.FirstOrDefault();
        if (SelectedEggMove is null)
            BuildPartners(null);
    }

    private string GameName => _sav.Version.ToString();

    private static string DescribeGender(IPersonalInfo pi)
    {
        if (pi.Genderless)
            return "Genderless — can only breed with Ditto";
        if (pi.OnlyFemale)
            return "Female only";
        if (pi.OnlyMale)
            return "Male only";
        return $"{pi.Gender} / 255 female ratio";
    }

    /// <summary>
    /// Which species could hand this egg move over: a shared egg group, plus the move
    /// in its own level-up list or as one of its own egg moves (a breeding chain).
    /// </summary>
    private void BuildPartners(EggMoveViewModel? move)
    {
        Partners.Clear();
        if (move is null || SelectedSpecies is null || IsBlocked)
        {
            PartnerSummary = string.Empty;
            return;
        }

        var target = (ushort)SelectedSpecies.Value;
        var targetInfo = _sav.Personal.GetFormEntry(target, 0);
        var direct = 0;
        var chain = 0;

        foreach (var candidate in _speciesPool)
        {
            if (candidate >= _strings.specieslist.Length)
                continue;
            var pi = _sav.Personal.GetFormEntry(candidate, 0);
            if (!EggGroups.CanBreed(pi) || !pi.SharesCommonEggGroup(targetInfo))
                continue;
            // A father cannot be female-only, and Ditto passes nothing down.
            if (FathersOnly && pi.OnlyFemale)
                continue;
            if (candidate == (ushort)Species.Ditto)
                continue;
            // "Breed it from one that already has it" is circular advice.
            if (candidate == target)
                continue;

            var learnsByLevel = _source.GetLearnset(candidate, 0).GetIsLearn(move.Move);
            var ownEggMove = _source.GetIsEggMove(candidate, 0, move.Move);
            if (!learnsByLevel && !ownEggMove)
                continue;

            var how = learnsByLevel
                && _source.GetLearnset(candidate, 0).TryGetLevelLearnMove(move.Move, out var level)
                ? $"learns it at Lv. {level}"
                : ownEggMove ? "has it as an egg move too" : "can learn it";
            if (learnsByLevel)
                direct++;
            else
                chain++;

            Partners.Add(new PartnerViewModel(candidate, _strings, how, learnsByLevel));
        }

        var ordered = Partners.OrderByDescending(p => p.IsDirect).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Partners.Clear();
        foreach (var p in ordered)
            Partners.Add(p);

        PartnerSummary = Partners.Count == 0
            ? $"No compatible partner in {GameName} can pass {move.Name}. It may need a move tutor, a TM, or transferring in from another game."
            : $"{Partners.Count} partner{(Partners.Count == 1 ? string.Empty : "s")} can pass {move.Name}"
              + $" — {direct} know it by level-up, {chain} would need it bred onto them first.";
    }

    [RelayCommand]
    public void Clear()
    {
        SelectedSpecies = null;
        HasSpecies = false;
        EggMoves.Clear();
        Partners.Clear();
        Summary = string.Empty;
        BlockedReason = string.Empty;
        OnPropertyChanged(nameof(HasEggMoves));
    }
}

/// <summary>One egg move the target species can be bred with.</summary>
public sealed class EggMoveViewModel
{
    public EggMoveViewModel(ushort move, GameStrings strings, EntityContext context)
    {
        Move = move;
        Name = strings.MoveName(move);
        var type = MoveInfo.GetType(move, context);
        TypeName = strings.TypeName(type);
        TypeIcon = TypeIconService.Get(type);
        TypeBrush = TypePalette.GetBrush(type);

        var facts = MoveDataService.Get(move);
        CategoryKind = facts.Category;
        Detail = facts.Category == MoveDataService.Category.Status
            ? "status"
            : $"{facts.PowerText} pow · {facts.AccuracyText}% acc";
    }

    public ushort Move { get; }
    public string Name { get; }
    public string TypeName { get; }
    public IImage? TypeIcon { get; }
    public IBrush? TypeBrush { get; }
    public bool HasTypeIcon => TypeIcon is not null;
    public MoveDataService.Category CategoryKind { get; }
    public string Detail { get; }

    public override string ToString() => Name;
}

/// <summary>A species that can pass the chosen egg move.</summary>
public sealed class PartnerViewModel
{
    public PartnerViewModel(ushort species, GameStrings strings, string how, bool isDirect)
    {
        Species = species;
        Name = strings.SpeciesName(species);
        How = how;
        IsDirect = isDirect;
        Sprite = SpriteService.GetSprite(species, 0, 0, 0, shiny: false, EntityContext.None);
    }

    public ushort Species { get; }
    public string Name { get; }
    public string How { get; }

    /// <summary>True when it learns the move itself, rather than needing it bred on first.</summary>
    public bool IsDirect { get; }

    public Bitmap? Sprite { get; }
}
