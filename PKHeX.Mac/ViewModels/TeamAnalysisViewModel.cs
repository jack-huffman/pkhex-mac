using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Battle-readiness view of a team: what it is collectively weak to, and what its
/// moves cannot hit hard. Something PKHeX itself does not offer.
/// </summary>
/// <remarks>
/// Uses the type chart in <see cref="TypeChart"/>, picked to match the save's
/// generation, and the bundled move table for each move's type and category. Immunity
/// and damage-reducing abilities are applied where they are unambiguous; held items,
/// terrain, and move-specific exceptions such as Freeze-Dry are not modelled.
/// </remarks>
public partial class TeamAnalysisViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly ChartEra _era;

    public TeamAnalysisViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        _era = TypeChart.GetEra(sav.Generation);
        EraNote = _era switch
        {
            ChartEra.Gen1 => "Using the Gen 1 chart, including the Ghost-versus-Psychic bug. Gen 1 had further quirks that are not modelled, so treat this as approximate.",
            ChartEra.Gen2To5 => "Using the Gen 2–5 chart: no Fairy type, and Steel still resists Dark and Ghost.",
            _ => "Using the modern type chart.",
        };
        Analyze();
    }

    public IReadOnlyList<string> ScopeChoices { get; } = ["Party", "Current box"];

    [ObservableProperty] private int _scopeIndex;
    [ObservableProperty] private int _boxIndex;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _eraNote = string.Empty;

    public ObservableCollection<TeamMemberViewModel> Members { get; } = [];

    /// <summary>
    /// The attacking types, in canonical order. One collection feeds the grid header
    /// and both totals rows, so they cannot drift out of alignment.
    /// </summary>
    public ObservableCollection<MatrixColumnViewModel> Columns { get; } = [];

    public ObservableCollection<CoverageRowViewModel> Coverage { get; } = [];
    public ObservableCollection<string> Findings { get; } = [];

    public bool HasMembers => Members.Count > 0;
    public bool HasFindings => Findings.Count > 0;

    /// <summary>
    /// Offensive coverage needs the bundled move table for each move's category. If it
    /// failed to load, say so — silently reporting a team that hits nothing would be
    /// worse than reporting nothing at all.
    /// </summary>
    public bool CoverageAvailable => MoveDataService.HasData;

    partial void OnScopeIndexChanged(int value) => Analyze();

    /// <summary>Called when the window's current box changes.</summary>
    public void SetBox(int box)
    {
        BoxIndex = box;
        if (ScopeIndex == 1)
            Analyze();
    }

    [RelayCommand]
    public void Refresh() => Analyze();

    private void Analyze()
    {
        Members.Clear();
        Columns.Clear();
        Coverage.Clear();
        Findings.Clear();

        foreach (var pk in GetTeam())
            Members.Add(new TeamMemberViewModel(pk, _strings, _era));

        OnPropertyChanged(nameof(HasMembers));
        if (Members.Count == 0)
        {
            Summary = ScopeIndex == 0
                ? "The party is empty."
                : "This box has no Pokémon in it.";
            OnPropertyChanged(nameof(HasFindings));
            return;
        }

        BuildMatrix();
        if (CoverageAvailable)
            BuildCoverage();

        var weakSpots = Columns.Count(d => d.IsSharedWeakness);
        Summary = $"{Members.Count} Pokémon · {weakSpots} shared weakness{(weakSpots == 1 ? string.Empty : "es")}";
        if (CoverageAvailable)
        {
            var blind = Coverage.Count(c => !c.IsCovered);
            Summary += $" · {blind} type{(blind == 1 ? string.Empty : "s")} nothing hits hard";
        }
        else
        {
            Findings.Add("Move data is unavailable, so offensive coverage could not be checked.");
        }
        OnPropertyChanged(nameof(HasFindings));
    }

    private IEnumerable<PKM> GetTeam()
    {
        if (ScopeIndex == 0)
        {
            if (!_sav.HasParty)
                yield break;
            for (int i = 0; i < _sav.PartyCount; i++)
            {
                var pk = _sav.GetPartySlotAtIndex(i);
                if (pk.Species != 0)
                    yield return pk;
            }
            yield break;
        }

        if (!_sav.HasBox || (uint)BoxIndex >= _sav.BoxCount)
            yield break;
        for (int i = 0; i < _sav.BoxSlotCount; i++)
        {
            var pk = _sav.GetBoxSlotAtIndex(BoxIndex, i);
            if (pk.Species != 0)
                yield return pk;
        }
    }

    /// <summary>
    /// Builds the team-by-type grid: a column per attacking type in canonical order,
    /// a cell per member, and the weak/resist tallies underneath.
    /// </summary>
    private void BuildMatrix()
    {
        var types = new List<int>();
        for (int attacker = 0; attacker < TypeChart.TypeCount; attacker++)
        {
            if (_era != ChartEra.Modern && attacker == 17) // no Fairy before Gen 6
                continue;
            if (_era == ChartEra.Gen1 && attacker is 8 or 16) // no Steel or Dark in Gen 1
                continue;
            types.Add(attacker);
        }

        foreach (var member in Members)
            member.BuildCells(types);

        foreach (var attacker in types)
        {
            int weak = 0, resist = 0, immune = 0, quadWeak = 0;
            string worst = string.Empty;
            double worstMultiplier = 0;
            foreach (var member in Members)
            {
                var multiplier = member.MultiplierAgainst(attacker);
                if (multiplier == 0)
                    immune++;
                else if (multiplier > 1)
                {
                    weak++;
                    if (multiplier >= 4)
                        quadWeak++;
                    if (multiplier > worstMultiplier)
                    {
                        worstMultiplier = multiplier;
                        worst = member.SpeciesName;
                    }
                }
                else if (multiplier < 1)
                    resist++;
            }

            var column = new MatrixColumnViewModel(attacker, _strings, weak, resist, immune, quadWeak);
            Columns.Add(column);
            if (column.IsSharedWeakness)
            {
                var detail = worstMultiplier >= 4 ? $" · {worst} takes {MatrixCellViewModel.Format(worstMultiplier)}" : string.Empty;
                Findings.Add($"{column.TypeName}: {weak} of {Members.Count} are weak and nothing resists it.{detail}");
            }
        }
    }

    /// <summary>For every defending type, the best the team's moves can do to it.</summary>
    private void BuildCoverage()
    {
        for (int defender = 0; defender < TypeChart.TypeCount; defender++)
        {
            if (_era != ChartEra.Modern && defender == 17)
                continue;
            if (_era == ChartEra.Gen1 && defender is 8 or 16)
                continue;

            double best = 0;
            string bestSource = string.Empty;
            foreach (var member in Members)
            {
                foreach (var (moveType, moveName) in member.AttackingMoves)
                {
                    var multiplier = TypeChart.Get(moveType, defender, _era);
                    if (multiplier <= best)
                        continue;
                    best = multiplier;
                    bestSource = $"{moveName} ({member.SpeciesName})";
                }
            }
            var row = new CoverageRowViewModel(defender, _strings, best, bestSource);
            Coverage.Add(row);
            if (!row.IsCovered)
                Findings.Add($"Nothing hits {row.TypeName} for extra damage.");
        }

        var ordered = Coverage.OrderBy(c => c.Best).ThenBy(c => c.TypeId).ToList();
        Coverage.Clear();
        foreach (var row in ordered)
            Coverage.Add(row);
    }
}

/// <summary>One team member, with the data the analysis needs.</summary>
public sealed class TeamMemberViewModel
{
    private readonly int _type1;
    private readonly int _type2;
    private readonly int _ability;
    private readonly ChartEra _era;
    private readonly GameStrings _typeStrings;

    public TeamMemberViewModel(PKM pk, GameStrings strings, ChartEra era)
    {
        _era = era;
        _typeStrings = strings;
        var pi = pk.PersonalInfo;
        _type1 = pi.Type1;
        _type2 = pi.Type2;
        _ability = pk.Ability;

        SpeciesName = (uint)pk.Species < strings.specieslist.Length
            ? strings.specieslist[pk.Species]
            : $"#{pk.Species}";
        Nickname = pk.Nickname == SpeciesName ? string.Empty : pk.Nickname;
        LevelText = $"Lv. {pk.CurrentLevel}";
        Sprite = SpriteService.GetPokemonSprite(pk);

        Type1Name = TypeName(_type1, strings);
        Type2Name = TypeName(_type2, strings);
        HasType2 = _type1 != _type2;
        Type1Icon = TypeIconService.Get(_type1);
        Type2Icon = TypeIconService.Get(_type2);
        Type1Brush = TypePalette.GetBrush(_type1);
        Type2Brush = TypePalette.GetBrush(_type2);

        AbilityName = (uint)_ability < strings.abilitylist.Length ? strings.abilitylist[_ability] : string.Empty;
        AbilityMatters = TypeChart.IsRelevantAbility(_ability);

        // Only damaging moves contribute offensive coverage.
        var attacking = new List<(int Type, string Name)>();
        foreach (var move in new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 })
        {
            if (move == 0)
                continue;
            var facts = MoveDataService.Get(move);
            if (facts.Category is MoveDataService.Category.Status)
                continue;
            // Unknown category with no power is almost certainly a status move.
            if (facts.Category is MoveDataService.Category.Unknown && facts.Power is null or 0)
                continue;
            var type = MoveInfo.GetType(move, pk.Context);
            var name = move < strings.movelist.Length ? strings.movelist[move] : $"#{move}";
            attacking.Add((type, name));
        }
        AttackingMoves = attacking;

        var worst = new List<string>();
        for (int t = 0; t < TypeChart.TypeCount; t++)
        {
            if (MultiplierAgainst(t) >= 2)
                worst.Add(TypeName(t, strings));
        }
        WeakTo = worst.Count == 0 ? "nothing" : string.Join(", ", worst);
    }

    public string SpeciesName { get; }
    public string Nickname { get; }
    public string LevelText { get; }
    public Bitmap? Sprite { get; }
    public string Type1Name { get; }
    public string Type2Name { get; }
    public bool HasType2 { get; }
    public IImage? Type1Icon { get; }
    public IImage? Type2Icon { get; }
    public IBrush? Type1Brush { get; }
    public IBrush? Type2Brush { get; }
    public bool HasType1Icon => Type1Icon is not null;
    public bool HasType2Icon => Type2Icon is not null;
    public string AbilityName { get; }

    /// <summary>True when the ability changes incoming damage, so the UI can say so.</summary>
    public bool AbilityMatters { get; }

    public string WeakTo { get; }
    public IReadOnlyList<(int Type, string Name)> AttackingMoves { get; }

    /// <summary>This member's row in the grid, one cell per attacking type.</summary>
    public ObservableCollection<MatrixCellViewModel> Cells { get; } = [];

    internal void BuildCells(IReadOnlyList<int> attackingTypes)
    {
        Cells.Clear();
        foreach (var attacker in attackingTypes)
            Cells.Add(new MatrixCellViewModel(MultiplierAgainst(attacker), attacker, SpeciesName, _typeStrings));
    }

    /// <summary>Incoming damage multiplier for an attacking type, abilities included.</summary>
    public double MultiplierAgainst(int attacker)
    {
        var raw = TypeChart.GetAgainst(attacker, _type1, _type2, _era);
        return TypeChart.ApplyAbility(raw, attacker, _ability);
    }

    private static string TypeName(int type, GameStrings strings) =>
        (uint)type < strings.types.Length ? strings.types[type] : $"#{type}";
}

/// <summary>What the team's moves can do to one defending type.</summary>
public sealed class CoverageRowViewModel
{
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E5776D"));
    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#6FAFB8"));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#8FA6B8"));

    public CoverageRowViewModel(int typeId, GameStrings strings, double best, string source)
    {
        TypeId = typeId;
        TypeName = (uint)typeId < strings.types.Length ? strings.types[typeId] : $"#{typeId}";
        TypeIcon = TypeIconService.Get(typeId);
        TypeBrush = TypePalette.GetBrush(typeId);
        Best = best;
        IsCovered = best > 1;

        BestText = best == 0 ? "no damage" : $"{best.ToString("0.##", CultureInfo.InvariantCulture)}×";
        Source = IsCovered ? source : string.Empty;
        Verdict = best > 1 ? "covered" : best == 0 ? "immune" : "neutral at best";
        VerdictBrush = best > 1 ? Ok : best == 0 ? Bad : Neutral;
    }

    public int TypeId { get; }
    public string TypeName { get; }
    public IImage? TypeIcon { get; }
    public IBrush? TypeBrush { get; }
    public bool HasIcon => TypeIcon is not null;
    public double Best { get; }
    public string BestText { get; }
    public string Source { get; }
    public bool IsCovered { get; }
    public string Verdict { get; }
    public IBrush VerdictBrush { get; }
}

/// <summary>
/// One attacking type: the grid's column header and the tallies underneath it.
/// </summary>
public sealed class MatrixColumnViewModel
{
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E5776D"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8FA6B8"));
    private static readonly IBrush Cool = new SolidColorBrush(Color.Parse("#6FAFB8"));

    public MatrixColumnViewModel(int typeId, GameStrings strings, int weak, int resist, int immune, int quadWeak)
    {
        TypeId = typeId;
        TypeName = (uint)typeId < strings.types.Length ? strings.types[typeId] : $"#{typeId}";
        TypeIcon = TypeIconService.Get(typeId);
        TypeBrush = TypePalette.GetBrush(typeId);
        Weak = weak;
        Resist = resist + immune;
        IsSharedWeakness = weak >= 2 && resist + immune == 0;

        // Zeroes are noise in a grid this dense; only counts that matter are printed.
        WeakText = weak == 0 ? string.Empty : weak.ToString(CultureInfo.InvariantCulture);
        ResistText = Resist == 0 ? string.Empty : Resist.ToString(CultureInfo.InvariantCulture);
        WeakBrush = IsSharedWeakness ? Bad : weak > 0 ? Muted : Muted;
        ResistBrush = Resist > 0 ? Cool : Muted;

        HeaderBrush = IsSharedWeakness ? Bad : Muted;
        Tooltip = IsSharedWeakness
            ? $"{TypeName}: {weak} weak, nothing resists it"
            : $"{TypeName}: {weak} weak · {Resist} resist or immune"
              + (quadWeak > 0 ? $" · {quadWeak} at 4×" : string.Empty);
    }

    public int TypeId { get; }
    public string TypeName { get; }
    public IImage? TypeIcon { get; }
    public IBrush? TypeBrush { get; }
    public bool HasIcon => TypeIcon is not null;
    public int Weak { get; }
    public int Resist { get; }

    /// <summary>Two or more members weak to it and nobody resisting: the real problem.</summary>
    public bool IsSharedWeakness { get; }

    public string WeakText { get; }
    public string ResistText { get; }
    public IBrush WeakBrush { get; }
    public IBrush ResistBrush { get; }
    public IBrush HeaderBrush { get; }
    public string Tooltip { get; }
}

/// <summary>
/// One cell: how hard a given attacking type hits one member. Neutral cells are left
/// blank so only the exceptions draw the eye.
/// </summary>
public sealed class MatrixCellViewModel
{
    private static readonly IBrush Quad = new SolidColorBrush(Color.Parse("#FF6B5B"));
    private static readonly IBrush Double = new SolidColorBrush(Color.Parse("#E5776D"));
    private static readonly IBrush Half = new SolidColorBrush(Color.Parse("#5E8FD0"));
    private static readonly IBrush Quarter = new SolidColorBrush(Color.Parse("#4A78BC"));
    private static readonly IBrush Zero = new SolidColorBrush(Color.Parse("#6FAFB8"));

    private static readonly IBrush QuadFill = new SolidColorBrush(Color.Parse("#3A1E1B"));
    private static readonly IBrush DoubleFill = new SolidColorBrush(Color.Parse("#2E1B19"));
    private static readonly IBrush ResistFill = new SolidColorBrush(Color.Parse("#182430"));
    private static readonly IBrush ZeroFill = new SolidColorBrush(Color.Parse("#16292B"));

    public MatrixCellViewModel(double multiplier, int attacker, string member, GameStrings strings)
    {
        Multiplier = multiplier;
        Text = Format(multiplier);
        IsNeutral = multiplier == 1;

        Foreground = multiplier switch
        {
            >= 4 => Quad,
            > 1 => Double,
            0 => Zero,
            <= 0.25 => Quarter,
            < 1 => Half,
            _ => Half,
        };
        Background = multiplier switch
        {
            >= 4 => QuadFill,
            > 1 => DoubleFill,
            0 => ZeroFill,
            < 1 => ResistFill,
            _ => Brushes.Transparent,
        };

        var type = (uint)attacker < strings.types.Length ? strings.types[attacker] : $"#{attacker}";
        Tooltip = multiplier == 1
            ? $"{type} → {member}: normal damage"
            : $"{type} → {member}: {Text}";
    }

    /// <summary>Compact multiplier text; blank at neutral.</summary>
    public static string Format(double multiplier) => multiplier switch
    {
        0 => "0",
        0.25 => "¼",
        0.5 => "½",
        1 => "",
        2 => "2×",
        4 => "4×",
        _ => multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "×",
    };

    public double Multiplier { get; }
    public string Text { get; }
    public bool IsNeutral { get; }
    public IBrush Foreground { get; }
    public IBrush Background { get; }
    public string Tooltip { get; }
}
