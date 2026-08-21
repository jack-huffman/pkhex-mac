using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The panel beneath the box grid: what this box holds, and what needs attention.
/// </summary>
public partial class BoxInsightsViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    /// <summary>Raised when a listed problem is clicked, with the slot to select.</summary>
    public Action<int>? SlotRequested { get; set; }

    public ObservableCollection<BoxProblemViewModel> Problems { get; } = [];

    /// <summary>Set by the window: only shown when the layout has room for it.</summary>
    [ObservableProperty] private bool _hasRoom;

    [ObservableProperty] private bool _hasContents;
    [ObservableProperty] private string _fillText = string.Empty;
    [ObservableProperty] private string _levelText = string.Empty;
    [ObservableProperty] private string _originText = string.Empty;
    [ObservableProperty] private int _shinyCount;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _verdict = string.Empty;

    public bool HasShiny => ShinyCount > 0;
    public bool HasProblems => Problems.Count > 0;
    public bool IsVisible => HasRoom && HasContents;

    partial void OnShinyCountChanged(int value) => OnPropertyChanged(nameof(HasShiny));
    partial void OnHasRoomChanged(bool value) => OnPropertyChanged(nameof(IsVisible));
    partial void OnHasContentsChanged(bool value) => OnPropertyChanged(nameof(IsVisible));

    /// <summary>
    /// Recomputes for a box. Legality is the expensive part, so it runs off the UI
    /// thread and any in-flight pass for a previous box is abandoned.
    /// </summary>
    public async Task RefreshAsync(SaveFile? sav, int box, GameStrings strings)
    {
        _cts?.Cancel();
        if (sav is null || !sav.HasBox)
        {
            HasContents = false;
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsChecking = true;
        try
        {
            var summary = await Task.Run(() => BoxInsights.Analyze(sav, box, strings, token), token);
            if (token.IsCancellationRequested)
                return;

            HasContents = summary.HasContents;
            FillText = summary.FillText;
            LevelText = summary.LevelText;
            OriginText = summary.OriginText;
            ShinyCount = summary.Shiny;

            Problems.Clear();
            foreach (var problem in summary.Problems)
                Problems.Add(new BoxProblemViewModel(problem, this));
            OnPropertyChanged(nameof(HasProblems));

            Verdict = summary.Problems.Count == 0
                ? "Everything here passes a legality check"
                : $"{summary.Problems.Count} need"
                  + (summary.Problems.Count == 1 ? "s attention" : " attention");
        }
        catch (OperationCanceledException)
        {
            // A newer box replaced this pass.
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsChecking = false;
        }
    }

    internal void RequestSlot(int slot) => SlotRequested?.Invoke(slot);
}

/// <summary>One flagged Pokémon, clickable to jump to its slot.</summary>
public sealed class BoxProblemViewModel
{
    private readonly BoxInsightsViewModel _parent;
    private readonly int _slot;

    public BoxProblemViewModel(BoxProblem problem, BoxInsightsViewModel parent)
    {
        _parent = parent;
        _slot = problem.Slot;
        Species = problem.Species;
        SlotText = problem.SlotText;
        Issue = problem.Issue;
        Sprite = SpriteService.GetPokemonSprite(problem.Entity);
    }

    public string Species { get; }
    public string SlotText { get; }
    public string Issue { get; }
    public Bitmap? Sprite { get; }

    public void Select() => _parent.RequestSlot(_slot);
}
