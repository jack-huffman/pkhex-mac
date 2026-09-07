using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Save-wide tools: a box report listing every stored Pokémon, team analysis, the
/// breeding planner and the integrity audit.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject, IDisposable
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly BackgroundRefresh _refresh = new();

    public ToolsViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        Team = new TeamAnalysisViewModel(sav, strings);
        Breeding = new BreedingViewModel(sav, strings);
        Integrity = new IntegrityAuditViewModel(sav, strings);
        _ = BuildReportAsync();
    }

    /// <summary>Type coverage and shared weaknesses for the party or a box.</summary>
    public TeamAnalysisViewModel Team { get; }

    /// <summary>Egg groups, egg moves and which partners can pass them.</summary>
    public BreedingViewModel Breeding { get; }

    /// <summary>Cross-entity checks PKHeX's per-Pokémon legality cannot make.</summary>
    public IntegrityAuditViewModel Integrity { get; }

    // =====================================================================
    // Box report
    // =====================================================================

    public ObservableCollection<ReportRowViewModel> ReportRows { get; } = [];
    private readonly List<ReportRowViewModel> _allRows = [];

    [ObservableProperty] private string _reportSearch = string.Empty;
    [ObservableProperty] private bool _illegalOnly;
    [ObservableProperty] private string _reportSummary = string.Empty;
    [ObservableProperty] private bool _isBuildingReport;

    partial void OnReportSearchChanged(string value) => FilterReport();
    partial void OnIllegalOnlyChanged(bool value) => FilterReport();

    /// <summary>
    /// Re-reads every stored Pokémon. The legality verdicts — up to a thousand of them on a
    /// full Scarlet/Violet save — are computed off the UI thread from a snapshot; the rows,
    /// which load sprites, are built back on it.
    /// </summary>
    [RelayCommand]
    private async Task BuildReportAsync()
    {
        IsBuildingReport = true;
        ReportSummary = "Reading every stored Pokémon…";

        var boxNames = BoxUtil.GetBoxNames(_sav);
        var slots = _sav.EnumerateOccupiedSlots().Where(s => !s.IsParty).ToList();
        var outcome = await _refresh.RunAsync(token =>
        {
            var verdicts = new bool[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                verdicts[i] = new LegalityAnalysis(slots[i].Entity).Valid;
            }
            return verdicts;
        });
        if (outcome.IsSuperseded)
            return;

        IsBuildingReport = false;
        if (outcome.Result is not { } legal)
        {
            ReportSummary = "The legality check failed while building the report.";
            return;
        }

        _allRows.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            var boxName = (uint)slots[i].Box < boxNames.Length ? boxNames[slots[i].Box] : $"Box {slots[i].Box + 1}";
            _allRows.Add(new ReportRowViewModel(slots[i].Entity, boxName, slots[i].Slot, legal[i], _strings));
        }
        FilterReport();
    }

    private void FilterReport()
    {
        ReportRows.Clear();
        var query = ReportSearch.Trim();
        foreach (var row in _allRows)
        {
            if (IllegalOnly && row.IsLegal)
                continue;
            if (query.Length != 0 && !row.Matches(query))
                continue;
            ReportRows.Add(row);
        }
        var illegal = _allRows.Count(r => !r.IsLegal);
        ReportSummary = $"{_allRows.Count} stored · {illegal} with legality issues · showing {ReportRows.Count}";
    }

    /// <summary>Tab-separated dump of the visible rows, for pasting into a spreadsheet.</summary>
    public string BuildReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Box\tSlot\tSpecies\tNickname\tLevel\tNature\tAbility\tHeld Item\tShiny\tLegal\tIVs\tEVs\tOT");
        foreach (var r in ReportRows)
            sb.AppendLine(r.ToTsv());
        return sb.ToString();
    }

    public void Dispose()
    {
        _refresh.Dispose();
        Integrity.Dispose();
    }
}

/// <summary>One row of the box report.</summary>
public sealed class ReportRowViewModel
{
    public ReportRowViewModel(PKM pk, string boxName, int slot, bool isLegal, GameStrings strings)
    {
        BoxName = boxName;
        SlotText = $"{slot + 1}";
        Species = strings.SpeciesName(pk);
        Nickname = pk.Nickname;
        Level = pk.CurrentLevel;
        Nature = strings.NatureName(pk.Nature);
        Ability = strings.AbilityName(pk.Ability);
        HeldItem = pk.HeldItem == 0 ? "—" : strings.ItemName(pk.HeldItem);
        IsShiny = pk.IsShiny;
        OtName = pk.OriginalTrainerName;
        Ivs = $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
        Evs = $"{pk.EV_HP}/{pk.EV_ATK}/{pk.EV_DEF}/{pk.EV_SPA}/{pk.EV_SPD}/{pk.EV_SPE}";
        Sprite = SpriteService.GetPokemonSprite(pk);
        IsLegal = isLegal;
    }

    public string BoxName { get; }
    public string SlotText { get; }
    public string Species { get; }
    public string Nickname { get; }
    public int Level { get; }
    public string Nature { get; }
    public string Ability { get; }
    public string HeldItem { get; }
    public string OtName { get; }
    public string Ivs { get; }
    public string Evs { get; }
    public bool IsShiny { get; }
    public bool IsLegal { get; }
    public Bitmap? Sprite { get; }

    public bool Matches(string query) =>
        Species.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Nickname.Contains(query, StringComparison.OrdinalIgnoreCase)
        || OtName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Nature.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Ability.Contains(query, StringComparison.OrdinalIgnoreCase);

    public string ToTsv() =>
        string.Join('\t', BoxName, SlotText, Species, Nickname, Level, Nature, Ability, HeldItem,
            IsShiny ? "yes" : "no", IsLegal ? "yes" : "no", Ivs, Evs, OtName);
}
