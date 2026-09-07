using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Multi-criteria search over the open save's storage, or over a folder of saved
/// entity files — PKHeX's "database" tool.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    /// <summary>Results are capped; the list is not virtualized and folders can be huge.</summary>
    private const int MaxResults = 500;

    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly BackgroundRefresh _refresh = new();

    public SearchViewModel(SaveFile sav, GameStrings strings, FilteredGameDataSource sources)
    {
        _sav = sav;
        _strings = strings;
        SpeciesChoices = [new ComboItem("Any species", 0), .. sources.Species.Where(s => s.Value != 0)];
        NatureChoices = [new ComboItem("Any nature", -1), .. sources.Natures];
        BallChoices = [new ComboItem("Any ball", -1), .. sources.Balls];
        MoveChoices = [new ComboItem("Any move", 0), .. sources.Moves.Where(m => m.Value != 0)];
    }

    public IReadOnlyList<ComboItem> SpeciesChoices { get; }
    public IReadOnlyList<ComboItem> NatureChoices { get; }
    public IReadOnlyList<ComboItem> BallChoices { get; }
    public IReadOnlyList<ComboItem> MoveChoices { get; }

    public IReadOnlyList<string> TriStateChoices { get; } = ["Any", "Yes", "No"];
    public IReadOnlyList<string> SourceChoices { get; } = ["This save's boxes & party", "A folder of Pokémon files"];

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    // ---- Filters ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderSource))]
    private int _sourceIndex;

    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _speciesValue;
    [ObservableProperty] private int _natureValue = -1;
    [ObservableProperty] private int _ballValue = -1;
    [ObservableProperty] private int _moveValue;
    [ObservableProperty] private int _shinyIndex;
    [ObservableProperty] private int _legalIndex;
    [ObservableProperty] private int _eggIndex;
    [ObservableProperty] private int _minLevel = 1;
    [ObservableProperty] private int _maxLevel = 100;
    [ObservableProperty] private int _minIvTotal;
    [ObservableProperty] private string _summary = "Set some filters and search.";
    [ObservableProperty] private bool _isSearching;

    public bool IsFolderSource => SourceIndex == 1;

    [RelayCommand]
    private void Reset()
    {
        Text = string.Empty;
        SpeciesValue = 0;
        NatureValue = -1;
        BallValue = -1;
        MoveValue = 0;
        ShinyIndex = 0;
        LegalIndex = 0;
        EggIndex = 0;
        MinLevel = 1;
        MaxLevel = 100;
        MinIvTotal = 0;
        Results.Clear();
        Summary = "Filters cleared.";
    }

    /// <summary>
    /// Runs the search off the UI thread. The save's slots are copied first; a folder is
    /// read entirely in the background. Legality is the expensive filter, so a search
    /// that asks for it takes visibly longer than one that does not.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        Results.Clear();
        IsSearching = true;
        Summary = "Searching…";

        var criteria = Snapshot();
        var candidates = IsFolderSource ? null : _sav.EnumerateOccupiedSlots().ToList();
        var folder = FolderPath;
        var context = _sav.Context;

        var outcome = await _refresh.RunAsync(token =>
        {
            var source = candidates is not null
                ? candidates.Select(s => new SearchCandidate(s.Entity, null, s.Box, s.Slot))
                : EnumerateFolder(folder, context);
            var hits = new List<(SearchCandidate Candidate, bool IsLegal)>();
            var scanned = 0;
            foreach (var candidate in source)
            {
                token.ThrowIfCancellationRequested();
                scanned++;
                if (criteria.Matches(candidate.Entity, out var legal))
                    hits.Add((candidate, legal ?? new LegalityAnalysis(candidate.Entity).Valid));
            }
            return (Hits: hits, Scanned: scanned);
        });
        if (outcome.IsSuperseded)
            return;

        IsSearching = false;
        if (outcome.Error is { } error)
        {
            Summary = $"Search failed: {error.Message}";
            return;
        }
        var (hits, scanned) = outcome.Result;

        foreach (var (candidate, isLegal) in hits.Take(MaxResults))
            Results.Add(new SearchResultViewModel(candidate, isLegal, _strings));

        Summary = hits.Count == 0
            ? $"No matches among {scanned:N0} Pokémon."
            : $"{hits.Count:N0} match{(hits.Count == 1 ? string.Empty : "es")} of {scanned:N0} scanned"
              + (hits.Count > MaxResults ? $" · showing the first {MaxResults}" : string.Empty);
    }

    /// <summary>The filters as plain values, so the background pass never reads bound properties.</summary>
    private SearchCriteria Snapshot() => new(
        SpeciesValue, NatureValue, BallValue, MoveValue, ShinyIndex, LegalIndex, EggIndex,
        MinLevel, MaxLevel, MinIvTotal, Text.Trim(), _strings);

    /// <summary>Every readable entity file below a folder. Saves and junk are skipped by size.</summary>
    private static IEnumerable<SearchCandidate> EnumerateFolder(string folder, EntityContext context)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            PKM? pk = null;
            try
            {
                var info = new FileInfo(file);
                if (info.Length is 0 or > 0x400)
                    continue;
                var data = File.ReadAllBytes(file);
                pk = EntityFormat.GetFromBytes(data, EntityFileExtension.GetContextFromExtension(file, context));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable file is not a result.
            }
            if (pk is { Species: > 0 })
                yield return new SearchCandidate(pk, file, -1, -1);
        }
    }

    public void Dispose() => _refresh.Dispose();
}

/// <summary>An immutable copy of the search filters, safe to evaluate on any thread.</summary>
internal sealed record SearchCriteria(
    int Species, int Nature, int Ball, int Move, int ShinyIndex, int LegalIndex, int EggIndex,
    int MinLevel, int MaxLevel, int MinIvTotal, string Text, GameStrings Strings)
{
    /// <summary>
    /// Whether an entity passes every filter. Legality is checked last because it is by
    /// far the most expensive test; when it was checked, the verdict is returned so the
    /// result row does not have to compute it again.
    /// </summary>
    public bool Matches(PKM pk, out bool? isLegal)
    {
        isLegal = null;
        if (Species != 0 && pk.Species != Species)
            return false;
        if (Nature >= 0 && (int)pk.Nature != Nature)
            return false;
        if (Ball >= 0 && pk.Ball != Ball)
            return false;
        if (pk.CurrentLevel < MinLevel || pk.CurrentLevel > MaxLevel)
            return false;
        if (!MatchTri(ShinyIndex, pk.IsShiny) || !MatchTri(EggIndex, pk.IsEgg))
            return false;

        if (MinIvTotal > 0)
        {
            var total = pk.IV_HP + pk.IV_ATK + pk.IV_DEF + pk.IV_SPA + pk.IV_SPD + pk.IV_SPE;
            if (total < MinIvTotal)
                return false;
        }

        if (Move != 0)
        {
            var move = (ushort)Move;
            if (pk.Move1 != move && pk.Move2 != move && pk.Move3 != move && pk.Move4 != move)
                return false;
        }

        if (Text.Length != 0)
        {
            var species = Strings.SpeciesName(pk);
            if (!species.Contains(Text, StringComparison.OrdinalIgnoreCase)
                && !pk.Nickname.Contains(Text, StringComparison.OrdinalIgnoreCase)
                && !pk.OriginalTrainerName.Contains(Text, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (LegalIndex != 0)
        {
            var legal = new LegalityAnalysis(pk).Valid;
            isLegal = legal;
            if (!MatchTri(LegalIndex, legal))
                return false;
        }
        return true;
    }

    private static bool MatchTri(int index, bool actual) => index switch
    {
        1 => actual,
        2 => !actual,
        _ => true,
    };
}

/// <summary>A Pokémon found by the search, plus where it lives.</summary>
public sealed record SearchCandidate(PKM Entity, string? FilePath, int Box, int Slot);

/// <summary>One search result row.</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(SearchCandidate candidate, bool isLegal, GameStrings strings)
    {
        Candidate = candidate;
        var pk = candidate.Entity;
        Species = strings.SpeciesName(pk);
        Nickname = pk.Nickname == Species ? string.Empty : pk.Nickname;
        Detail = $"Lv. {pk.CurrentLevel} · {strings.NatureName(pk.Nature)}"
                 + $" · IV {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
        IsShiny = pk.IsShiny;
        IsLegal = isLegal;
        Sprite = SpriteService.GetPokemonSprite(pk);
        IsFromFile = candidate.FilePath is not null;
        Location = candidate.FilePath is { } path
            ? Path.GetFileName(path)
            : candidate.Box < 0
                ? $"Party · slot {candidate.Slot + 1}"
                : $"Box {candidate.Box + 1} · slot {candidate.Slot + 1}";
        FullPath = candidate.FilePath ?? string.Empty;
    }

    public SearchCandidate Candidate { get; }
    public string Species { get; }
    public string Nickname { get; }
    public string Detail { get; }
    public string Location { get; }
    public string FullPath { get; }
    public bool IsShiny { get; }
    public bool IsLegal { get; }
    public bool IsFromFile { get; }
    public Bitmap? Sprite { get; }
}
