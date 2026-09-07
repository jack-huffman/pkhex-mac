using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Read-only preview of a Pokémon shown in the inspector while browsing the
/// encounter / mystery gift databases, before it is added to the save.
/// </summary>
public partial class PokemonPreviewViewModel : ObservableObject
{
    private readonly GameStrings _strings;

    public PokemonPreviewViewModel(GameStrings strings) => _strings = strings;

    /// <summary>The previewed entity; cloned when added to the save.</summary>
    public PKM? Current { get; private set; }

    [ObservableProperty] private bool _hasPokemon;
    [ObservableProperty] private Bitmap? _artwork;
    [ObservableProperty] private Bitmap? _ballSprite;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _levelBadge = string.Empty;
    [ObservableProperty] private string _type1Name = string.Empty;
    [ObservableProperty] private string _type2Name = string.Empty;
    [ObservableProperty] private bool _hasType2;
    [ObservableProperty] private IBrush? _type1Brush;
    [ObservableProperty] private IBrush? _type2Brush;
    [ObservableProperty] private IImage? _type1Icon;
    [ObservableProperty] private IImage? _type2Icon;
    [ObservableProperty] private bool _hasType1Icon;
    [ObservableProperty] private bool _hasType2Icon;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _isLegal;
    [ObservableProperty] private string _legalityReport = string.Empty;
    [ObservableProperty] private bool _isBlocked;
    [ObservableProperty] private string _blockedReason = string.Empty;

    // ---- Detail shown alongside the artwork ----
    [ObservableProperty] private string _natureText = string.Empty;
    [ObservableProperty] private string _abilityText = string.Empty;
    [ObservableProperty] private string _itemText = string.Empty;
    [ObservableProperty] private string _originText = string.Empty;
    [ObservableProperty] private int _statTotal;
    [ObservableProperty] private bool _hasMoves;

    public ObservableCollection<MoveChoice> Moves { get; } = [];
    public ObservableCollection<PreviewStatRow> Stats { get; } = [];

    public Bitmap? ShinyIcon => SpriteService.GetOverlay("rare_icon");

    /// <summary>Shows why the selected entry cannot be added, instead of an empty pane.</summary>
    public void ShowBlocked(string reason)
    {
        Current = null;
        HasPokemon = false;
        IsBlocked = true;
        BlockedReason = reason;
    }

    public void Load(PKM? pk)
    {
        Current = pk;
        IsBlocked = false;
        BlockedReason = string.Empty;
        if (pk is null || pk.Species == 0)
        {
            HasPokemon = false;
            Moves.Clear();
            Stats.Clear();
            return;
        }

        HasPokemon = true;
        Artwork = SpriteService.GetPokemonArtwork(pk);
        BallSprite = SpriteService.GetBallSprite(pk.Ball);
        SpeciesName = _strings.SpeciesName(pk);
        LevelBadge = $"Lv. {pk.CurrentLevel}";
        IsShiny = pk.IsShiny;

        var pi = pk.PersonalInfo;
        Type1Name = _strings.TypeName(pi.Type1);
        Type2Name = _strings.TypeName(pi.Type2);
        HasType2 = pi.Type1 != pi.Type2;
        Type1Brush = TypePalette.GetBrush(pi.Type1);
        Type2Brush = TypePalette.GetBrush(pi.Type2);
        Type1Icon = TypeIconService.Get(pi.Type1);
        Type2Icon = TypeIconService.Get(pi.Type2);
        // Stellar has no symbol; those fall back to the coloured pill.
        HasType1Icon = Type1Icon is not null;
        HasType2Icon = Type2Icon is not null;

        NatureText = _strings.NatureName(pk.Nature);
        AbilityText = _strings.AbilityName(pk.Ability);
        ItemText = pk.HeldItem == 0 ? "No held item" : _strings.ItemName(pk.HeldItem);
        OriginText = $"{GameInfo.GetVersionName(pk.Version)} · met Lv. {pk.MetLevel}";

        BuildMoves(pk);
        BuildStats(pk);

        var la = new LegalityAnalysis(pk);
        IsLegal = la.Valid;
        LegalityReport = la.Report();
    }

    private void BuildMoves(PKM pk)
    {
        Moves.Clear();
        foreach (var move in new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 })
        {
            if (move != 0)
                Moves.Add(MoveChoice.For(move, pk.Context, _strings));
        }
        HasMoves = Moves.Count != 0;
    }

    private void BuildStats(PKM pk)
    {
        Stats.Clear();
        pk.ResetPartyStats();
        var (up, dn) = pk.StatAlignment.GetNatureModification();
        var upRow = up == dn ? -1 : NatureChoice.RowFor(up);
        var dnRow = up == dn ? -1 : NatureChoice.RowFor(dn);

        (string Label, int Value)[] rows =
        [
            ("HP", pk.Stat_HPMax), ("ATK", pk.Stat_ATK), ("DEF", pk.Stat_DEF),
            ("SATK", pk.Stat_SPA), ("SDEF", pk.Stat_SPD), ("SPE", pk.Stat_SPE),
        ];
        StatTotal = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var direction = i == upRow ? 1 : i == dnRow ? -1 : 0;
            Stats.Add(new PreviewStatRow(rows[i].Label, rows[i].Value, direction));
            StatTotal += rows[i].Value;
        }
    }
}

/// <summary>A read-only stat row in the database preview, matching the Stats tab's look.</summary>
public sealed class PreviewStatRow
{
    private const double BarScale = 500.0;

    public PreviewStatRow(string label, int value, int natureDirection)
    {
        Label = label;
        Value = value;
        BarPercent = Math.Min(100.0, value / BarScale * 100.0);
        LabelBrush = natureDirection switch { 1 => Palette.RaisedStat, -1 => Palette.LoweredStat, _ => Palette.Muted };
        BarBrush = natureDirection switch { 1 => Palette.Bad, -1 => Palette.LoweredStatBar, _ => Palette.Good };
        NatureBadge = natureDirection switch { 1 => "▲", -1 => "▼", _ => string.Empty };
        HasNatureBadge = natureDirection != 0;
        Tooltip = natureDirection switch
        {
            1 => $"{label} {value} — raised by nature",
            -1 => $"{label} {value} — lowered by nature",
            _ => $"{label} {value}",
        };
    }

    public string Label { get; }
    public int Value { get; }
    public double BarPercent { get; }
    public IBrush LabelBrush { get; }
    public IBrush BarBrush { get; }
    public string NatureBadge { get; }
    public bool HasNatureBadge { get; }
    public string Tooltip { get; }
}
