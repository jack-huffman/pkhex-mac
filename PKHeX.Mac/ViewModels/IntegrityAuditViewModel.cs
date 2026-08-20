using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
public partial class IntegrityAuditViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private CancellationTokenSource? _cts;
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
    public async Task RunAsync()
    {
        if (IsRunning)
            return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsRunning = true;
        Summary = "Comparing every stored Pokémon…";
        Findings.Clear();
        OnPropertyChanged(nameof(HasFindings));

        try
        {
            // Legality analysis dominates the cost, so keep it off the UI thread.
            _result = await Task.Run(() => IntegrityAudit.Run(_sav, _strings, token), token);
            if (token.IsCancellationRequested)
                return;
            Populate();
        }
        catch (OperationCanceledException)
        {
            Summary = "Audit cancelled.";
        }
        catch (Exception ex)
        {
            Summary = $"Audit failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            HasRun = true;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        IsRunning = false;
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
}

/// <summary>One finding, with its severity styling and the Pokémon it names.</summary>
public sealed class AuditFindingViewModel
{
    private static readonly IBrush Conclusive = new SolidColorBrush(Color.Parse("#FF6B5B"));
    private static readonly IBrush Strong = new SolidColorBrush(Color.Parse("#E0A33D"));
    private static readonly IBrush Notable = new SolidColorBrush(Color.Parse("#D8C05A"));
    private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#8FA6B8"));

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
            AuditSeverity.Conclusive => Conclusive,
            AuditSeverity.Strong => Strong,
            AuditSeverity.Notable => Notable,
            _ => Info,
        };

        // Long informational lists are collapsed; the interesting ones are short.
        var shown = finding.Entries.Count > 12 ? finding.Entries.Take(12).ToList() : finding.Entries;
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
