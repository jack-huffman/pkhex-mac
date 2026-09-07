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
/// Battle-readiness view of the party: what it is collectively weak to, and what its
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
            Summary = "The party is empty.";
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
        if (!_sav.HasParty)
            yield break;
        for (int i = 0; i < _sav.PartyCount; i++)
        {
            var pk = _sav.GetPartySlotAtIndex(i);
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
        var types = TypeChart.GetTypes(_era);

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

    /// <summary>For every defending type this era has, the best the team's moves can do to it.</summary>
    private void BuildCoverage()
    {
        foreach (var defender in TypeChart.GetTypes(_era))
        {

            // The best multiplier answers "do I have a super-effective hit"; among the
            // moves that reach it, the strongest one answers "which should I use".
            double best = 0;
            foreach (var member in Members)
            {
                foreach (var option in member.AttackingMoves)
                    best = Math.Max(best, TypeChart.Get(option.Type, defender, _era));
            }

            var candidates = new List<CoverageCandidate>();
            foreach (var member in Members)
            {
                foreach (var option in member.AttackingMoves)
                {
                    if (Math.Abs(TypeChart.Get(option.Type, defender, _era) - best) > 1e-9)
                        continue;
                    candidates.Add(new CoverageCandidate(option.Name, option.Owner,
                                                         option.Score * best, option.HasStab, option.HitsText));
                }
            }
            candidates.Sort((a, b) => b.Damage.CompareTo(a.Damage));
            var row = new CoverageRowViewModel(defender, _strings, best, candidates);
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
    private readonly GameStrings _strings;

    public TeamMemberViewModel(PKM pk, GameStrings strings, ChartEra era)
    {
        _era = era;
        _strings = strings;
        var pi = pk.PersonalInfo;
        _type1 = pi.Type1;
        _type2 = pi.Type2;
        _ability = pk.Ability;

        SpeciesName = strings.SpeciesName(pk);
        Nickname = pk.Nickname == SpeciesName ? string.Empty : pk.Nickname;
        LevelText = $"Lv. {pk.CurrentLevel}";
        // Pixel sprite for the dense grid rows, hi-res render for the team cards.
        Sprite = SpriteService.GetPokemonSprite(pk);
        Artwork = SpriteService.GetPokemonArtwork(pk);

        Type1Name = strings.TypeName(_type1);
        Type2Name = strings.TypeName(_type2);
        HasType2 = _type1 != _type2;
        Type1Icon = TypeIconService.Get(_type1);
        Type2Icon = TypeIconService.Get(_type2);
        Type1Brush = TypePalette.GetBrush(_type1);
        Type2Brush = TypePalette.GetBrush(_type2);

        AbilityName = strings.AbilityName(_ability);
        AbilityMatters = TypeChart.IsRelevantAbility(_ability);

        // One pass builds both the display list and the damaging subset that feeds
        // offensive coverage. MoveChoice is the same shape the move pickers use, so
        // the type and category icons match the rest of the app.
        // Make sure the live stats are populated before reading the attack stats.
        // This is a copy of the slot, so the save is untouched.
        pk.ResetPartyStats();

        var display = new List<MoveChoice>(4);
        var attacking = new List<AttackOption>();
        foreach (var move in new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 })
        {
            if (move == 0)
                continue;
            display.Add(MoveChoice.For(move, pk.Context, strings));

            var facts = MoveDataService.Get(move);
            if (facts.Category is MoveDataService.Category.Status)
                continue;
            // Unknown category with no power is almost certainly a status move.
            if (facts.Category is MoveDataService.Category.Unknown && facts.Power is null or 0)
                continue;

            var type = MoveInfo.GetType(move, pk.Context);
            var name = strings.MoveName(move);

            // Rough output per use, before the type matchup: effective power (hits and
            // crits folded in), same-type bonus, and the stat the move actually uses.
            var stab = type == _type1 || type == _type2 ? 1.5 : 1.0;
            var stat = facts.Category == MoveDataService.Category.Physical
                ? pk.Stat_ATK
                : pk.Stat_SPA;
            var score = facts.EffectivePower * stab * stat / 100.0;
            attacking.Add(new AttackOption(type, name, SpeciesName, score, stab > 1, facts.HitsText));
        }
        Moves = display;
        AttackingMoves = attacking;

        // Group the matchups by multiplier so the tile reads as bands of type badges
        // rather than a sentence to parse.
        var byMultiplier = new SortedDictionary<double, List<TypeBadgeViewModel>>();
        foreach (var attacker in TypeChart.GetTypes(era))
        {
            var multiplier = MultiplierAgainst(attacker);
            if (multiplier == 1)
                continue;
            if (!byMultiplier.TryGetValue(multiplier, out var list))
                byMultiplier[multiplier] = list = [];
            list.Add(new TypeBadgeViewModel(attacker, strings));
        }

        var resist = new List<MatchupGroupViewModel>();
        var weak = new List<MatchupGroupViewModel>();
        foreach (var (multiplier, badges) in byMultiplier)
        {
            var group = new MatchupGroupViewModel(multiplier, badges);
            if (multiplier < 1)
                resist.Add(group);
            else
                weak.Add(group);
        }
        // Both bands read in ascending multiplier order, so resistances run
        // x0 -> x1/2 and weaknesses run x2 -> x4.
        Resistances = resist;
        Weaknesses = weak;
    }

    public string SpeciesName { get; }
    public string Nickname { get; }
    public string LevelText { get; }
    public Bitmap? Sprite { get; }
    public Bitmap? Artwork { get; }
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

    /// <summary>Incoming multipliers below 1, grouped ascending: immunities first.</summary>
    public IReadOnlyList<MatchupGroupViewModel> Resistances { get; } = [];

    /// <summary>Incoming multipliers above 1, grouped ascending: x2 then x4.</summary>
    public IReadOnlyList<MatchupGroupViewModel> Weaknesses { get; } = [];

    public bool HasResistances => Resistances.Count > 0;
    public bool HasWeaknesses => Weaknesses.Count > 0;

    /// <summary>Every move this member knows, for display.</summary>
    public IReadOnlyList<MoveChoice> Moves { get; }

    /// <summary>The damaging subset, scored, used for offensive coverage.</summary>
    public IReadOnlyList<AttackOption> AttackingMoves { get; }

    public bool HasMoves => Moves.Count > 0;

    /// <summary>This member's row in the grid, one cell per attacking type.</summary>
    public ObservableCollection<MatrixCellViewModel> Cells { get; } = [];

    internal void BuildCells(IReadOnlyList<int> attackingTypes)
    {
        Cells.Clear();
        foreach (var attacker in attackingTypes)
            Cells.Add(new MatrixCellViewModel(MultiplierAgainst(attacker), attacker, SpeciesName, _strings));
    }

    /// <summary>Incoming damage multiplier for an attacking type, abilities included.</summary>
    public double MultiplierAgainst(int attacker)
    {
        var raw = TypeChart.GetAgainst(attacker, _type1, _type2, _era);
        return TypeChart.ApplyAbility(raw, attacker, _ability);
    }
}

/// <summary>What the team's moves can do to one defending type.</summary>
public sealed class CoverageRowViewModel
{
    public CoverageRowViewModel(int typeId, GameStrings strings, double best,
                               IReadOnlyList<CoverageCandidate> candidates)
    {
        TypeId = typeId;
        TypeName = strings.TypeName(typeId);
        TypeIcon = TypeIconService.Get(typeId);
        TypeBrush = TypePalette.GetBrush(typeId);
        Best = best;
        IsCovered = best > 1;

        BestText = best == 0 ? "no damage" : $"{best.ToString("0.##", CultureInfo.InvariantCulture)}×";
        Candidates = candidates;

        // Name the hardest hitter; keep the rest for the tooltip.
        var top = candidates.Count > 0 ? candidates[0] : null;
        Source = top is null
            ? string.Empty
            : $"{top.Move} ({top.Owner})" + (top.HitsText.Length == 0 ? string.Empty : $" · {top.HitsText}");
        Tooltip = candidates.Count == 0
            ? $"Nothing on the team damages {TypeName}."
            : $"Best hits on {TypeName}:\n" + string.Join("\n",
                candidates.Take(4).Select(c => $"  {c.Move} ({c.Owner}) — {c.Damage:F0}"
                                               + (c.HasStab ? ", same type" : string.Empty)));
        Verdict = best > 1 ? "covered" : best == 0 ? "immune" : "neutral at best";
        VerdictBrush = best > 1 ? Palette.Good : best == 0 ? Palette.Bad : Palette.Muted;
    }

    public int TypeId { get; }
    public string TypeName { get; }
    public IImage? TypeIcon { get; }
    public IBrush? TypeBrush { get; }
    public bool HasIcon => TypeIcon is not null;
    public double Best { get; }
    public string BestText { get; }
    public string Source { get; }
    public IReadOnlyList<CoverageCandidate> Candidates { get; } = [];
    public string Tooltip { get; } = string.Empty;
    public bool IsCovered { get; }
    public string Verdict { get; }
    public IBrush VerdictBrush { get; }
}

/// <summary>
/// One attacking type: the grid's column header and the tallies underneath it.
/// </summary>
public sealed class MatrixColumnViewModel
{
    public MatrixColumnViewModel(int typeId, GameStrings strings, int weak, int resist, int immune, int quadWeak)
    {
        TypeId = typeId;
        TypeName = strings.TypeName(typeId);
        TypeIcon = TypeIconService.Get(typeId);
        TypeBrush = TypePalette.GetBrush(typeId);
        Weak = weak;
        Resist = resist + immune;
        IsSharedWeakness = weak >= 2 && resist + immune == 0;

        // Zeroes are noise in a grid this dense; only counts that matter are printed.
        WeakText = weak == 0 ? string.Empty : weak.ToString(CultureInfo.InvariantCulture);
        ResistText = Resist == 0 ? string.Empty : Resist.ToString(CultureInfo.InvariantCulture);
        WeakBrush = IsSharedWeakness ? Palette.Bad : Palette.Muted;
        ResistBrush = Resist > 0 ? Palette.Good : Palette.Muted;

        HeaderBrush = IsSharedWeakness ? Palette.Bad : Palette.Muted;
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
    public MatrixCellViewModel(double multiplier, int attacker, string member, GameStrings strings)
    {
        Multiplier = multiplier;
        Text = Format(multiplier);
        IsNeutral = multiplier == 1;

        Foreground = multiplier switch
        {
            >= 4 => Palette.Severe,
            > 1 => Palette.Bad,
            0 => Palette.Good,
            <= 0.25 => Palette.Quarter,
            _ => Palette.LoweredStatBar,
        };
        Background = multiplier switch
        {
            >= 4 => Palette.SevereFill,
            > 1 => Palette.BadFill,
            0 => Palette.ImmuneFill,
            < 1 => Palette.ResistFill,
            _ => Brushes.Transparent,
        };

        var type = strings.TypeName(attacker);
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

/// <summary>
/// One band of matchups sharing a multiplier — the "×¼" chip and the type badges
/// that sit beside it.
/// </summary>
public sealed class MatchupGroupViewModel
{
    public MatchupGroupViewModel(double multiplier, IReadOnlyList<TypeBadgeViewModel> types)
    {
        Multiplier = multiplier;
        Types = types;
        Label = multiplier switch
        {
            0 => "×0",
            0.25 => "×¼",
            0.5 => "×½",
            2 => "×2",
            4 => "×4",
            _ => "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture),
        };
        LabelBrush = multiplier switch
        {
            0 => Palette.Immune,
            < 1 => Palette.Resist,
            >= 4 => Palette.Severe,
            _ => Palette.Bad,
        };
        Tooltip = $"{Label} damage from {string.Join(", ", types.Select(t => t.TypeName))}";
    }

    public double Multiplier { get; }
    public string Label { get; }
    public IBrush LabelBrush { get; }
    public IReadOnlyList<TypeBadgeViewModel> Types { get; }
    public string Tooltip { get; }
}

/// <summary>
/// A damaging move a member can use, with its output before the type matchup is
/// applied. Score is a rough comparison figure, not a damage calculation: it folds in
/// effective power, the same-type bonus and the stat the move uses, but not the target's
/// defences, items, abilities, weather or terrain.
/// </summary>
public sealed record AttackOption(int Type, string Name, string Owner, double Score, bool HasStab, string HitsText);

/// <summary>One candidate answer against a defending type.</summary>
public sealed record CoverageCandidate(string Move, string Owner, double Damage, bool HasStab, string HitsText);
