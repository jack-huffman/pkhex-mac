using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Runs <see cref="IntegrityAudit"/> and presents its findings worst-first.
/// </summary>
public sealed partial class IntegrityAuditViewModel : ObservableObject, IDisposable
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly BackgroundRefresh _refresh = new();
    private AuditResult? _result;

    public IntegrityAuditViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
    }

    public ObservableCollection<AuditFindingViewModel> Findings { get; } = [];

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasRun;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _includeInformational = true;

    public bool HasFindings => Findings.Count > 0;

    partial void OnIncludeInformationalChanged(bool value) => Populate();

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Summary = "Comparing every stored Pokémon…";
        Findings.Clear();
        OnPropertyChanged(nameof(HasFindings));

        // Copy the slots on the UI thread; the comparison and legality work run off it.
        var entries = IntegrityAudit.Collect(_sav, _strings);
        var outcome = await _refresh.RunAsync(token => IntegrityAudit.Analyze(entries, _strings, token));
        if (outcome.IsSuperseded)
            return; // Cancel() already reported; a newer run owns the state

        IsRunning = false;
        HasRun = true;
        if (outcome.Error is { } error)
        {
            Summary = $"Audit failed: {error.Message}";
            return;
        }
        _result = outcome.Result;
        Populate();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRunning)
            return;
        _refresh.Cancel();
        IsRunning = false;
        HasRun = true;
        Summary = "Audit cancelled.";
    }

    private void Populate()
    {
        Findings.Clear();
        if (_result is null)
            return;

        foreach (var finding in _result.Findings)
        {
            if (!IncludeInformational && finding.Severity == AuditSeverity.Info)
                continue;
            Findings.Add(new AuditFindingViewModel(finding));
        }
        OnPropertyChanged(nameof(HasFindings));

        var real = _result.Findings.Count(f => f.Severity != AuditSeverity.Info);
        Summary = real == 0
            ? $"Compared {_result.Scanned} Pokémon — nothing questionable found."
            : $"Compared {_result.Scanned} Pokémon — {real} finding{(real == 1 ? string.Empty : "s")} worth a look"
              + $", plus {_result.Findings.Count - real} informational.";
    }

    public void Dispose() => _refresh.Dispose();
}

/// <summary>One finding, with its severity styling and the Pokémon it names.</summary>
public sealed class AuditFindingViewModel
{
    /// <summary>Long informational lists are collapsed; the interesting ones are short.</summary>
    private const int MaxEntriesShown = 12;

    public AuditFindingViewModel(AuditFinding finding)
    {
        Title = finding.Title;
        Detail = finding.Detail;
        Severity = finding.Severity switch
        {
            AuditSeverity.Conclusive => "CANNOT HAPPEN NORMALLY",
            AuditSeverity.Strong => "VERY LIKELY EDITED",
            AuditSeverity.Notable => "WORTH A LOOK",
            _ => "FOR INFORMATION",
        };
        SeverityBrush = finding.Severity switch
        {
            AuditSeverity.Conclusive => Palette.Severe,
            AuditSeverity.Strong => Palette.Warning,
            AuditSeverity.Notable => Palette.Notable,
            _ => Palette.Muted,
        };

        var shown = finding.Entries.Count > MaxEntriesShown ? finding.Entries.Take(MaxEntriesShown).ToList() : finding.Entries;
        Entries = shown.Select(e => new AuditEntryViewModel(e)).ToList();
        Overflow = finding.Entries.Count > shown.Count
            ? $"…and {finding.Entries.Count - shown.Count} more"
            : string.Empty;
        HasOverflow = Overflow.Length > 0;
    }

    public string Title { get; }
    public string Detail { get; }
    public string Severity { get; }
    public IBrush SeverityBrush { get; }
    public IReadOnlyList<AuditEntryViewModel> Entries { get; }
    public string Overflow { get; }
    public bool HasOverflow { get; }
}

/// <summary>One Pokémon named by a finding.</summary>
public sealed class AuditEntryViewModel
{
    public AuditEntryViewModel(AuditEntry entry)
    {
        Species = entry.Species;
        Location = entry.Location;
        Detail = entry.Detail;
        Sprite = SpriteService.GetPokemonSprite(entry.Entity);
    }

    public string Species { get; }
    public string Location { get; }
    public string Detail { get; }
    public Bitmap? Sprite { get; }
}
