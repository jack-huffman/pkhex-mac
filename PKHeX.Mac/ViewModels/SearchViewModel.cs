using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
public partial class SearchViewModel : ObservableObject
{
    /// <summary>Results are capped; the list is not virtualized and folders can be huge.</summary>
    private const int MaxResults = 500;

    private readonly SaveFile _sav;
    private readonly GameStrings _strings;

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
    [ObservableProperty] private int _sourceIndex;
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

    partial void OnSourceIndexChanged(int value) => OnPropertyChanged(nameof(IsFolderSource));

    [RelayCommand]
    public void Reset()
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

    [RelayCommand]
    public void Search()
    {
        Results.Clear();
        IsSearching = true;
        try
        {
            var scanned = 0;
            var matched = 0;
            foreach (var candidate in Enumerate())
            {
                scanned++;
                if (!Matches(candidate.Entity))
                    continue;
                matched++;
                if (Results.Count < MaxResults)
                    Results.Add(new SearchResultViewModel(candidate, _strings));
            }

            Summary = matched == 0
                ? $"No matches among {scanned:N0} Pokémon."
                : $"{matched:N0} match{(matched == 1 ? string.Empty : "es")} of {scanned:N0} scanned"
                  + (matched > MaxResults ? $" · showing the first {MaxResults}" : string.Empty);
        }
        catch (Exception ex)
        {
            Summary = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Every candidate from the chosen source, with where it came from.</summary>
    private IEnumerable<SearchCandidate> Enumerate()
    {
        if (IsFolderSource)
        {
            if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
                yield break;
            foreach (var file in Directory.EnumerateFiles(FolderPath, "*", SearchOption.AllDirectories))
            {
                PKM? pk = null;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length is 0 or > 0x400) // entity files are small; skip saves and junk
                        continue;
                    var data = File.ReadAllBytes(file);
                    pk = EntityFormat.GetFromBytes(data, EntityFileExtension.GetContextFromExtension(file, _sav.Context));
                }
                catch
                {
                    pk = null;
                }
                if (pk is { Species: > 0 })
                    yield return new SearchCandidate(pk, file, -1, -1);
            }
            yield break;
        }

        if (_sav.HasBox)
        {
            for (int box = 0; box < _sav.BoxCount; box++)
            {
                for (int slot = 0; slot < _sav.BoxSlotCount; slot++)
                {
                    var pk = _sav.GetBoxSlotAtIndex(box, slot);
                    if (pk.Species != 0)
                        yield return new SearchCandidate(pk, null, box, slot);
                }
            }
        }
        if (_sav.HasParty)
        {
            for (int i = 0; i < _sav.PartyCount; i++)
            {
                var pk = _sav.GetPartySlotAtIndex(i);
                if (pk.Species != 0)
                    yield return new SearchCandidate(pk, null, -1, i);
            }
        }
    }

    private bool Matches(PKM pk)
    {
        if (SpeciesValue != 0 && pk.Species != SpeciesValue)
            return false;
        if (NatureValue >= 0 && (int)pk.Nature != NatureValue)
            return false;
        if (BallValue >= 0 && pk.Ball != BallValue)
            return false;
        if (pk.CurrentLevel < MinLevel || pk.CurrentLevel > MaxLevel)
            return false;

        if (!MatchTri(ShinyIndex, pk.IsShiny))
            return false;
        if (!MatchTri(EggIndex, pk.IsEgg))
            return false;

        if (MinIvTotal > 0)
        {
            var total = pk.IV_HP + pk.IV_ATK + pk.IV_DEF + pk.IV_SPA + pk.IV_SPD + pk.IV_SPE;
            if (total < MinIvTotal)
                return false;
        }

        if (MoveValue != 0)
        {
            var move = (ushort)MoveValue;
            if (pk.Move1 != move && pk.Move2 != move && pk.Move3 != move && pk.Move4 != move)
                return false;
        }

        var query = Text.Trim();
        if (query.Length != 0)
        {
            var species = (uint)pk.Species < _strings.specieslist.Length ? _strings.specieslist[pk.Species] : string.Empty;
            if (!species.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !pk.Nickname.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !pk.OriginalTrainerName.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Legality last: it is the most expensive check by far.
        if (LegalIndex != 0 && !MatchTri(LegalIndex, new LegalityAnalysis(pk).Valid))
            return false;

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
    public SearchResultViewModel(SearchCandidate candidate, GameStrings strings)
    {
        Candidate = candidate;
        var pk = candidate.Entity;
        Species = (uint)pk.Species < strings.specieslist.Length ? strings.specieslist[pk.Species] : $"#{pk.Species}";
        Nickname = pk.Nickname == Species ? string.Empty : pk.Nickname;
        Detail = $"Lv. {pk.CurrentLevel} · {NatureName(pk, strings)}"
                 + $" · IV {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
        IsShiny = pk.IsShiny;
        IsLegal = new LegalityAnalysis(pk).Valid;
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

    private static string NatureName(PKM pk, GameStrings strings) =>
        (uint)pk.Nature < strings.natures.Length ? strings.natures[(int)pk.Nature] : pk.Nature.ToString();
}
