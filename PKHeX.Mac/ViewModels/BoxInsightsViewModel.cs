using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The panel beneath the box grid: what this box holds, and what needs attention.
/// </summary>
public sealed partial class BoxInsightsViewModel : ObservableObject, IDisposable
{
    private readonly BackgroundRefresh _refresh = new();

    /// <summary>Raised when a listed problem is clicked, with the slot to select.</summary>
    public Action<int>? SlotRequested { get; set; }

    public ObservableCollection<BoxProblemViewModel> Problems { get; } = [];

    /// <summary>Set by the window: only shown when the layout has room for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _hasRoom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _hasContents;

    [ObservableProperty] private string _fillText = string.Empty;
    [ObservableProperty] private string _levelText = string.Empty;
    [ObservableProperty] private string _originText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShiny))]
    private int _shinyCount;

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _verdict = string.Empty;

    public bool HasShiny => ShinyCount > 0;
    public bool HasProblems => Problems.Count > 0;
    public bool IsVisible => HasRoom && HasContents;

    /// <summary>
    /// Recomputes for a box. The slots are copied here, on the UI thread, and the
    /// legality work runs off it; a pass for a previous box is abandoned.
    /// </summary>
    public async Task RefreshAsync(SaveFile? sav, int box, GameStrings strings)
    {
        if (sav is null || !sav.HasBox)
        {
            _refresh.Cancel();
            HasContents = false;
            IsChecking = false;
            return;
        }

        var snapshot = BoxInsights.Snapshot(sav, box);
        IsChecking = true;
        var outcome = await _refresh.RunAsync(token => BoxInsights.Analyze(snapshot, strings, token));
        if (outcome.IsSuperseded)
            return; // a newer box owns the panel now

        IsChecking = false;
        if (outcome.Result is not { } summary)
        {
            Verdict = "The legality check failed for this box.";
            return;
        }

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

    internal void RequestSlot(int slot) => SlotRequested?.Invoke(slot);

    public void Dispose() => _refresh.Dispose();
}

/// <summary>One flagged Pokémon, clickable to jump to its slot.</summary>
public sealed partial class BoxProblemViewModel
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

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Select() => _parent.RequestSlot(_slot);
}
