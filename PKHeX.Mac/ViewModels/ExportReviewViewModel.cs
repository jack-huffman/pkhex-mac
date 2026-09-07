using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// What is about to be written, shown before committing 4MB to disk.
/// </summary>
public sealed partial class ExportReviewViewModel : ObservableObject, IDisposable
{
    private readonly Func<(SaveFile? Live, SaveFile? Pristine)> _saves;
    private readonly GameStrings _strings;
    private readonly BackgroundRefresh _refresh = new();

    public ExportReviewViewModel(Func<(SaveFile?, SaveFile?)> saves, GameStrings strings)
    {
        _saves = saves;
        _strings = strings;
    }

    public ObservableCollection<ChangeGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>Raised when the user confirms; the window owns the file dialog.</summary>
    public Action? ExportRequested { get; set; }

    public bool HasChanges => Groups.Count > 0;

    [RelayCommand]
    public async Task OpenAsync()
    {
        IsOpen = true;
        IsBusy = true;
        Groups.Clear();
        OnPropertyChanged(nameof(HasChanges));

        var (live, pristine) = _saves();
        if (live is null || pristine is null)
        {
            Summary = "Nothing is open to compare.";
            IsBusy = false;
            return;
        }

        // The overlay blocks editing while the comparison runs, so reading the live save
        // off the UI thread here cannot race a write.
        var outcome = await _refresh.RunAsync(token => SaveDiff.Compare(live, pristine, _strings, token));
        if (outcome.IsSuperseded)
            return;

        IsBusy = false;
        if (outcome.Result is not { } changes)
        {
            Summary = "The comparison failed; export anyway if you trust your edits.";
            return;
        }

        foreach (var group in changes.GroupBy(c => c.Kind).OrderBy(g => g.Key))
        {
            Groups.Add(new ChangeGroupViewModel(
                group.Key == ChangeKind.Entity ? "POKÉMON" : "SAVE DATA",
                group.Select(c => new ChangeRowViewModel(c)).ToList()));
        }
        OnPropertyChanged(nameof(HasChanges));

        Summary = changes.Count == 0
            ? "Nothing has changed since this file was opened."
            : $"{changes.Count} change{(changes.Count == 1 ? string.Empty : "s")} will be written.";
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Export()
    {
        IsOpen = false;
        ExportRequested?.Invoke();
    }

    public void Dispose() => _refresh.Dispose();
}

/// <summary>A heading and the changes beneath it.</summary>
public sealed class ChangeGroupViewModel(string title, IReadOnlyList<ChangeRowViewModel> rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<ChangeRowViewModel> Rows { get; } = rows;
}

/// <summary>One line in the review.</summary>
public sealed class ChangeRowViewModel
{
    public ChangeRowViewModel(SaveChange change)
    {
        Where = DisplayNames.FromBlockLabel(change.Where);
        Description = change.Description;
        Sprite = change.Entity is { Species: > 0 } pk ? SpriteService.GetPokemonSprite(pk) : null;
        HasSprite = Sprite is not null;
    }

    public string Where { get; }
    public string Description { get; }
    public Bitmap? Sprite { get; }
    public bool HasSprite { get; }
}
