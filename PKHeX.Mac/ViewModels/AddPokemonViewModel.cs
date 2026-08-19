using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Encounter database: pick a species, list every legal way it can be obtained
/// for this save, and generate a legal Pokémon from the chosen encounter.
/// </summary>
public partial class AddPokemonViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private List<IEncounterable> _encounters = [];

    public AddPokemonViewModel(SaveFile sav, FilteredGameDataSource sources, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        SpeciesChoices = sources.Species.Where(s => s.Value != 0).ToList();
    }

    public IReadOnlyList<ComboItem> SpeciesChoices { get; }
    public ObservableCollection<string> EncounterDescriptions { get; } = [];

    [ObservableProperty] private int _speciesValue;
    [ObservableProperty] private int _selectedEncounterIndex = -1;
    [ObservableProperty] private bool _makeShiny;
    [ObservableProperty] private string _statusText = "Pick a species to list its legal encounters.";

    /// <summary>The generated Pokémon, set when Generate succeeds.</summary>
    public PKM? Result { get; private set; }

    partial void OnSpeciesValueChanged(int value)
    {
        EncounterDescriptions.Clear();
        _encounters.Clear();
        Result = null;
        if (value <= 0)
            return;

        var blank = _sav.BlankPKM;
        blank.Species = (ushort)value;
        blank.Gender = blank.GetSaneGender();

        _encounters = EncounterMovesetGenerator.GenerateEncounters(blank, _sav, ReadOnlyMemory<ushort>.Empty)
            .Take(60)
            .ToList();

        foreach (var enc in _encounters)
        {
            var version = enc.Version.ToString();
            var levels = enc.LevelMin == enc.LevelMax ? $"Lv.{enc.LevelMin}" : $"Lv.{enc.LevelMin}–{enc.LevelMax}";
            EncounterDescriptions.Add($"{enc.LongName} · {version} · {levels}{(enc.IsShiny ? " ★" : string.Empty)}");
        }

        StatusText = _encounters.Count == 0
            ? "No legal encounter exists for this species in this save's game."
            : $"{_encounters.Count} legal encounter(s) found. Pick one and Generate.";
        if (_encounters.Count > 0)
            SelectedEncounterIndex = 0;
    }

    [RelayCommand]
    public void Generate()
    {
        Result = null;
        if ((uint)SelectedEncounterIndex >= _encounters.Count)
        {
            StatusText = "Select an encounter first.";
            return;
        }
        var enc = _encounters[SelectedEncounterIndex];
        var pk = enc.ConvertToPKM(_sav);
        if (pk.GetType() != _sav.PKMType)
        {
            pk = EntityConverter.ConvertToType(pk, _sav.PKMType, out var res);
            if (pk is null)
            {
                StatusText = $"Conversion failed: {res}";
                return;
            }
        }
        if (MakeShiny && !pk.IsShiny)
            CommonEdits.SetShiny(pk);
        pk.Heal();
        pk.RefreshChecksum();

        var la = new LegalityAnalysis(pk);
        Result = pk;
        var name = (uint)pk.Species < _strings.specieslist.Length ? _strings.specieslist[pk.Species] : $"#{pk.Species}";
        StatusText = $"Generated {name} (Lv.{pk.CurrentLevel}){(pk.IsShiny ? " ★" : string.Empty)} — legal: {(la.Valid ? "yes" : "no")}. Click Add to Box.";
    }
}
