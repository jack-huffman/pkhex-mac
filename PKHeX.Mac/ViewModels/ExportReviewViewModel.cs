using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
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
public partial class ExportReviewViewModel : ObservableObject
{
    private readonly Func<(SaveFile? Live, SaveFile? Pristine)> _saves;
    private readonly GameStrings _strings;
    private CancellationTokenSource? _cts;

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

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var (live, pristine) = _saves();
        if (live is null || pristine is null)
        {
            Summary = "Nothing is open to compare.";
            IsBusy = false;
            return;
        }

        try
        {
            var changes = await Task.Run(() => SaveDiff.Compare(live, pristine, _strings, token), token);
            if (token.IsCancellationRequested)
                return;

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
        catch (OperationCanceledException)
        {
            // Superseded by a newer review.
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    public void Export()
    {
        IsOpen = false;
        ExportRequested?.Invoke();
    }
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
        Where = Prettify(change.Where);
        Description = change.Description;
        Sprite = change.Entity is { Species: > 0 } pk ? SpriteService.GetPokemonSprite(pk) : null;
        HasSprite = Sprite is not null;
    }

    public string Where { get; }
    public string Description { get; }
    public Bitmap? Sprite { get; }
    public bool HasSprite { get; }

    /// <summary>
    /// PKHeX's own block labels are identifiers — KUnlockedUpgradeFly, KMoney. They are
    /// the only names these blocks have, so they are worth showing, but not raw.
    /// </summary>
    private static string Prettify(string name)
    {
        if (name.Length < 2 || name[0] != 'K' || !char.IsUpper(name[1]))
            return name;

        var sb = new StringBuilder(name.Length + 8);
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 1 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(i == 1 ? c : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
