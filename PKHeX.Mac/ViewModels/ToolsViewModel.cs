using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Save-wide tools: PKHeX's batch editor (bulk property edits driven by text
/// instructions), a box report listing every stored Pokémon, and team analysis.
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly Action _onChanged;

    public ToolsViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _sav = sav;
        _strings = strings;
        _onChanged = onChanged;
        Team = new TeamAnalysisViewModel(sav, strings);
        Breeding = new BreedingViewModel(sav, strings);
        BuildReport();
    }

    /// <summary>Type coverage and shared weaknesses for the party or a box.</summary>
    public TeamAnalysisViewModel Team { get; }

    /// <summary>Egg groups, egg moves and which partners can pass them.</summary>
    public BreedingViewModel Breeding { get; }

    // =====================================================================
    // Batch editor
    // =====================================================================

    [ObservableProperty] private string _instructions = string.Empty;
    [ObservableProperty] private int _scopeIndex; // 0 = current box, 1 = all boxes, 2 = party
    [ObservableProperty] private string _batchResult = string.Empty;
    [ObservableProperty] private int _currentBox;


    public IReadOnlyList<string> ScopeChoices { get; } = ["Current box", "All boxes", "Party"];

    /// <summary>Shown under the editor so the syntax is discoverable.</summary>
    public string BatchHelp =>
        "One instruction per line. Prefix with = to require a match, ! to exclude, . to assign.\n" +
        "Examples:\n" +
        "  .Nature=Adamant           set every match's nature\n" +
        "  =Species=25               only act on Pikachu\n" +
        "  .EV_ATK=252  .EV_SPE=252  max two EV stats\n" +
        "  .IV_HP=31 .IV_ATK=31      set individual IVs\n" +
        "  .OriginalTrainerName=Jack rename the OT\n" +
        "  .IsNicknamed=false        clear nicknames";

    [RelayCommand]
    public void RunBatch()
    {
        var text = Instructions.Trim();
        if (text.Length == 0)
        {
            BatchResult = "Enter at least one instruction.";
            return;
        }

        StringInstructionSet[] sets;
        try
        {
            sets = StringInstructionSet.GetBatchSets(text);
        }
        catch (Exception ex)
        {
            BatchResult = $"Could not parse the instructions: {ex.Message}";
            return;
        }
        if (sets.Length == 0)
        {
            BatchResult = "No usable instructions found.";
            return;
        }

        var targets = GetTargets().ToList();
        int modified = 0, skipped = 0, errors = 0;
        foreach (var (pk, write) in targets)
        {
            var anyApplied = false;
            foreach (var set in sets)
            {
                var result = EntityBatchEditor.Instance.TryModify(pk, set.Filters, set.Instructions);
                if (result == ModifyResult.Modified)
                    anyApplied = true;
                else if (result == ModifyResult.Error)
                    errors++;
            }
            if (anyApplied)
            {
                pk.RefreshChecksum();
                write(pk);
                modified++;
            }
            else
            {
                skipped++;
            }
        }

        BuildReport();
        _onChanged();
        BatchResult = $"Modified {modified} of {targets.Count} Pokémon"
                      + (skipped > 0 ? $", {skipped} unchanged" : string.Empty)
                      + (errors > 0 ? $", {errors} instruction error(s)" : string.Empty)
                      + ". Remember to export the save (⌘S).";
    }

    /// <summary>The entities the selected scope covers, each with a write-back action.</summary>
    private IEnumerable<(PKM pk, Action<PKM> write)> GetTargets()
    {
        if (ScopeIndex == 2)
        {
            if (!_sav.HasParty)
                yield break;
            for (int i = 0; i < _sav.PartyCount; i++)
            {
                var pk = _sav.GetPartySlotAtIndex(i);
                if (pk.Species == 0)
                    continue;
                var index = i;
                yield return (pk, p => _sav.SetPartySlotAtIndex(p, index));
            }
            yield break;
        }

        if (!_sav.HasBox)
            yield break;
        var first = ScopeIndex == 1 ? 0 : CurrentBox;
        var last = ScopeIndex == 1 ? _sav.BoxCount - 1 : CurrentBox;
        for (int box = first; box <= last; box++)
        {
            for (int slot = 0; slot < _sav.BoxSlotCount; slot++)
            {
                var pk = _sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0)
                    continue;
                var (b, sl) = (box, slot);
                yield return (pk, p => _sav.SetBoxSlotAtIndex(p, b, sl));
            }
        }
    }

    // =====================================================================
    // Box report
    // =====================================================================

    public ObservableCollection<ReportRowViewModel> ReportRows { get; } = [];
    private readonly List<ReportRowViewModel> _allRows = [];

    [ObservableProperty] private string _reportSearch = string.Empty;
    [ObservableProperty] private bool _illegalOnly;
    [ObservableProperty] private string _reportSummary = string.Empty;

    partial void OnReportSearchChanged(string value) => FilterReport();
    partial void OnIllegalOnlyChanged(bool value) => FilterReport();

    private void BuildReport()
    {
        _allRows.Clear();
        if (_sav.HasBox)
        {
            for (int box = 0; box < _sav.BoxCount; box++)
            {
                var boxName = box < BoxUtil.GetBoxNames(_sav).Length ? BoxUtil.GetBoxNames(_sav)[box] : $"Box {box + 1}";
                for (int slot = 0; slot < _sav.BoxSlotCount; slot++)
                {
                    var pk = _sav.GetBoxSlotAtIndex(box, slot);
                    if (pk.Species == 0)
                        continue;
                    _allRows.Add(new ReportRowViewModel(pk, boxName, slot, _strings));
                }
            }
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
}

/// <summary>One row of the box report.</summary>
public sealed class ReportRowViewModel
{
    public ReportRowViewModel(PKM pk, string boxName, int slot, GameStrings strings)
    {
        BoxName = boxName;
        SlotText = $"{slot + 1}";
        Species = (uint)pk.Species < strings.specieslist.Length ? strings.specieslist[pk.Species] : $"#{pk.Species}";
        Nickname = pk.Nickname;
        Level = pk.CurrentLevel;
        Nature = (uint)pk.Nature < strings.natures.Length ? strings.natures[(int)pk.Nature] : pk.Nature.ToString();
        Ability = (uint)pk.Ability < strings.abilitylist.Length ? strings.abilitylist[pk.Ability] : $"#{pk.Ability}";
        HeldItem = pk.HeldItem == 0
            ? "—"
            : (uint)pk.HeldItem < strings.itemlist.Length ? strings.itemlist[pk.HeldItem] : $"#{pk.HeldItem}";
        IsShiny = pk.IsShiny;
        OtName = pk.OriginalTrainerName;
        Ivs = $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
        Evs = $"{pk.EV_HP}/{pk.EV_ATK}/{pk.EV_DEF}/{pk.EV_SPA}/{pk.EV_SPD}/{pk.EV_SPE}";
        Sprite = SpriteService.GetPokemonSprite(pk);
        IsLegal = new LegalityAnalysis(pk).Valid;
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
