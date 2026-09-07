using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// One editing session: an open save, the untouched copy it can be reverted to, and
/// every editor built on top of it. The window shows one session at a time; the
/// <see cref="WorkspaceViewModel"/> keeps the others.
/// </summary>
/// <remarks>
/// The class is one object because the editors genuinely share state — the selected
/// slot, the unsaved-changes counter, the dropdown sources — but it is split across
/// files by concern so each stays readable:
/// <list type="bullet">
/// <item><c>Navigation</c>: which view is showing, the editors behind each, the command palette.</item>
/// <item><c>Storage</c>: the box and party grids and every operation on a slot.</item>
/// <item><c>Files</c>: loading, exporting, and moving Pokémon files in and out.</item>
/// <item><c>Revert</c>: the prompts and the three scopes of undo.</item>
/// </list>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int PartySize = 6;
    private const int DefaultBoxSlotCount = 30;

    private readonly GameStrings _strings = GameInfo.GetStrings("en");

    /// <summary>
    /// This save's own dropdown data. PKHeX exposes it as a global that is replaced
    /// whenever a save loads, which is fine for one save and wrong for several: the
    /// second would silently take the first's species and move lists. Each session
    /// keeps the sources it was built with.
    /// </summary>
    private FilteredGameDataSource _sources = GameInfo.FilteredSources;
    private SaveFile? _sav;

    /// <summary>
    /// A second, untouched copy of the same save. Editing never goes near it, so it is
    /// what "revert" restores from — for the whole save or for one Pokémon. Costs a few
    /// MB, which buys per-slot revert without a change journal.
    /// </summary>
    private SaveFile? _pristine;

    private string? _savPath;
    private PKM? _clipboardPk;

    public MainWindowViewModel()
    {
        Detail = new PokemonDetailViewModel(_strings);
        // The inspector's own buttons touch the save, so the session handles them.
        Detail.ApplyRequested += ApplyDetailChanges;
        Detail.CloseRequested += () => SelectSlot(null);
        Detail.DiscardRequested += RevertDetailEdits;

        Review = new ExportReviewViewModel(() => (_sav, _pristine), _strings);
        Review.ExportRequested = () => ExportRequested?.Invoke();

        // Clicking a flagged Pokémon in the insights panel selects it in the grid.
        BoxInsights.SlotRequested = slot =>
        {
            if ((uint)slot < BoxSlots.Count)
                SelectSlot(BoxSlots[slot]);
        };

        Preview = new PokemonPreviewViewModel(_strings);
        for (int i = 0; i < DefaultBoxSlotCount; i++)
            BoxSlots.Add(new SlotViewModel(0, i));
        for (int i = 0; i < PartySize; i++)
            PartySlots.Add(new SlotViewModel(SlotViewModel.PartyBox, i));
    }

    public PokemonDetailViewModel Detail { get; }
    public PokemonPreviewViewModel Preview { get; }

    /// <summary>What is about to be written, compared against the file on disk.</summary>
    public ExportReviewViewModel Review { get; }

    /// <summary>Facts about the current box, shown beneath the grid when there is room.</summary>
    public BoxInsightsViewModel BoxInsights { get; } = new();

    /// <summary>Whether the in-memory save differs from the file on disk.</summary>
    public SaveStateViewModel SaveState { get; } = new();

    /// <summary>Persisted preferences, owned by the window and shared for recording.</summary>
    public AppSettings? Settings { get; set; }

    /// <summary>Asks the window to flush settings after something worth remembering.</summary>
    public Action? SettingsChanged { get; set; }

    /// <summary>Raised when an export needs a file dialog, which only the window can show.</summary>
    public Action? ExportRequested { get; set; }

    public SaveFile? Save => _sav;
    public string? SavePath => _savPath;

    /// <summary>
    /// Anything that would be lost by quitting: edits written into the in-memory save,
    /// plus edits typed into the inspector that have not been applied to a slot yet.
    /// </summary>
    public bool HasPendingWork => SaveState.HasUnsavedChanges || Detail.IsDirty;

    [ObservableProperty] private bool _hasSave;
    [ObservableProperty] private string _windowTitle = "PKHeX for Mac";
    [ObservableProperty] private string _statusText = "Open a save file to begin  (⌘O)";

    // ---- Trainer card ----
    [ObservableProperty] private string _trainerName = string.Empty;
    [ObservableProperty] private string _gameName = string.Empty;
    [ObservableProperty] private string _trainerIds = string.Empty;
    [ObservableProperty] private string _playTime = string.Empty;
    [ObservableProperty] private string _generationText = string.Empty;

    // ---- Update banner ----
    [ObservableProperty] private bool _showUpdateBanner;
    [ObservableProperty] private string _updateBannerText = string.Empty;

    /// <summary>
    /// Reports an edit: shown on the status line, counted as unsaved, and added to the
    /// session log. Everything that mutates the save should go through here.
    /// </summary>
    private void NoteChange(string description)
    {
        StatusText = description;
        SaveState.NoteChange(description);
    }

    /// <summary>
    /// Records a change made to this save from elsewhere, such as another tab sending a
    /// Pokémon here. The receiving save has unsaved work even though nobody edited it.
    /// </summary>
    public void NoteExternalChange(string description)
    {
        NoteChange(description);
        RefreshSlotViews();
    }

    /// <summary>Re-reads trainer card fields after the trainer editor writes them.</summary>
    public void RefreshTrainerCard()
    {
        if (_sav is null)
            return;
        TrainerName = _sav.OT;
        TrainerIds = $"TID {_sav.DisplayTID:D6} · SID {_sav.DisplaySID:D4}";
        PlayTime = _sav.PlayTimeString;
    }

    // ---- Sending Pokémon to another open save ----

    /// <summary>Sends the selected Pokémon to another open save.</summary>
    [ObservableProperty] private TransferViewModel? _transfer;

    /// <summary>Supplied by the workspace: the other saves currently open.</summary>
    public Func<IReadOnlyList<TransferTarget>>? OtherSaves { get; set; }

    /// <summary>True once a second save is open, which is when transferring is possible.</summary>
    [ObservableProperty] private bool _canTransfer;

    /// <summary>Built by the workspace once it can enumerate the other tabs.</summary>
    public void AttachTransfer()
    {
        Transfer = new TransferViewModel(_strings,
            () => _selected?.Pokemon,
            () => OtherSaves?.Invoke() ?? [],
            NoteChange);
        RefreshTransferTargets();
    }

    /// <summary>Re-reads the destination list, after a tab opens or closes.</summary>
    public void RefreshTransferTargets()
    {
        Transfer?.Reload();
        CanTransfer = Transfer?.HasTargets ?? false;
        if (_sav is not null)
            BuildPaletteEntries();
    }

    // ---- Update check ----

    public async Task CheckForUpstreamUpdateAsync()
    {
        var info = await UpdateCheckService.CheckAsync();
        if (info is { IsBehind: true })
        {
            UpdateBannerText = $"PKHeX {info.RemoteTag} was released {info.RemoteDate:MMM d, yyyy} — this app's engine is {info.LocalVersion}. " +
                               "Run scripts/update-upstream.sh and rebuild to catch up.";
            ShowUpdateBanner = true;
        }
    }

    [RelayCommand]
    private void DismissUpdateBanner() => ShowUpdateBanner = false;

    public void Dispose()
    {
        BoxInsights.Dispose();
        Review.Dispose();
        Tools?.Dispose();
    }
}
