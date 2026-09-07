using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Mac.Services;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

/// <summary>
/// The one window. Its DataContext is always the active session; the
/// <see cref="Workspace"/> owns the sessions and the tab strip.
/// </summary>
/// <remarks>
/// Everything here needs the window itself: file and folder pickers, the clipboard, the
/// drag ghost, window placement, the native menu, and routing keys that no control
/// claimed. Anything that only needs the save lives on a view model.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>Height of the title-bar band, matching ExtendClientAreaTitleBarHeightHint.</summary>
    private const double TitleBarHeight = 46;

    /// <summary>Pointer travel before a press on a slot becomes a drag.</summary>
    private const double DragThreshold = 6;

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    /// <summary>The open saves. Bound by the tab strip through <c>#RootWindow.Workspace</c>.</summary>
    public WorkspaceViewModel Workspace { get; } = new();

    /// <summary>Window bounds, last view and recent saves, kept between launches.</summary>
    private readonly AppSettings _settings = AppSettings.Load();

    private NativeMenuItem? _recentMenu;

    public MainWindow()
    {
        InitializeComponent();
        Workspace.SessionCreated += WireSession;
        Workspace.PropertyChanged += OnWorkspaceChanged;
        DataContext = Workspace.AddEmptyTab().Session;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Tunnels, so the wheel is intercepted before a dropdown or spinner can act on it.
        AddHandler(PointerWheelChangedEvent, OnWheelBeforeControls, RoutingStrategies.Tunnel);
        Loaded += OnWindowLoaded;
        HookShutdownRequest();
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        RestoreWindow();
        _recentMenu = FindMenuItem(NativeMenu.GetMenu(this), "Open Recent");
        RebuildRecentMenu();
        _ = VM.CheckForUpstreamUpdateAsync();
    }

    /// <summary>Connects a new session to the services only the window can provide.</summary>
    private void WireSession(MainWindowViewModel session)
    {
        session.ExportRequested = () => Run(ExportAsync);
        session.Settings = _settings;
        session.SettingsChanged = () => _settings.Save();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.Active) || Workspace.Active is not { } tab)
            return;
        DataContext = tab.Session;
        // The new session has never seen the boxes column, so tell it the size now.
        tab.Session.UpdateBoxLayout(BoxesView.Bounds.Width, BoxesView.Bounds.Height);
    }

    /// <summary>The boxes column reports its own size; the insights panel fits or hides on it.</summary>
    private void OnBoxesViewSizeChanged(object? sender, SizeChangedEventArgs e) =>
        VM.UpdateBoxLayout(e.NewSize.Width, e.NewSize.Height);

    /// <summary>
    /// Runs a view action — pickers, clipboard, dialogs — and surfaces any failure. A
    /// discarded task that faults does not crash the process, but it does fail silently,
    /// which for a save editor is worse.
    /// </summary>
    private async void Run(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            await ShowMessage("Something Went Wrong", ex.Message);
        }
    }

    // =====================================================================
    // File open / export
    // =====================================================================

    /// <summary>Every entity extension the engine knows, plus the encrypted <c>.ek*</c> twins.</summary>
    private static readonly FilePickerFileType EntityFileType = new("Pokémon Entity Files")
    {
        Patterns = ["*.ek*", .. PKHeX.Core.EntityFileExtension.GetExtensions().Select(e => $"*.{e}")],
    };

    private static readonly FilePickerFileType BlockFileType = new("Save Block")
    {
        Patterns = ["*.bin"],
    };

    public void OnOpenClicked(object? sender, EventArgs e) => Run(OpenAsync);
    public void OnOpenButtonClicked(object? sender, RoutedEventArgs e) => Run(OpenAsync);
    public void OnExportClicked(object? sender, EventArgs e) => Run(ExportAsync);
    public void OnExportButtonClicked(object? sender, RoutedEventArgs e) => Run(ExportAsync);
    public void OnCloseTabClicked(object? sender, EventArgs e)
    {
        if (Workspace.Active is { } tab)
            Workspace.Close(tab);
    }

    private async Task OpenAsync()
    {
        // No FileTypeFilter: Switch saves ("main", "main (1)", …) have no extension and
        // macOS open panels grey out files that don't match the filter. The engine sniffs
        // the format from content, so allow selecting anything.
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Pokémon Save File",
            AllowMultiple = true,
        });
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                await OpenPathAsync(path);
        }
    }

    /// <summary>Opens a save by path, from a picker, a drop, or the Open Recent menu.</summary>
    private async Task OpenPathAsync(string path)
    {
        if (!Workspace.Open(path, out var error))
        {
            await ShowMessage("Could Not Open Save", error);
            // A file that no longer opens should stop being offered.
            _settings.Recent.Remove(path);
            _settings.Save();
        }
        RebuildRecentMenu();
    }

    private async Task ExportAsync()
    {
        if (VM.Save is null)
        {
            await ShowMessage("No Save Loaded", "Open a save file first (⌘O).");
            return;
        }

        var suggested = Path.GetFileName(VM.SavePath) ?? "main";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Save File",
            SuggestedFileName = suggested,
            // Start in the folder the save came from. ExportSave moves SavePath to the
            // file it wrote, so later exports open wherever it was last written instead.
            SuggestedStartLocation = await TryGetFolder(VM.SavePath),
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        if (!VM.ExportSave(path, out var error))
            await ShowMessage("Could Not Export Save", error);
    }

    /// <summary>
    /// Folder holding <paramref name="path"/>, for seeding a file picker's start location.
    /// Returns null when there is nothing usable, which leaves the picker at its own default.
    /// </summary>
    private async Task<IStorageFolder?> TryGetFolder(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(dir) ? null : await StorageProvider.TryGetFolderFromPathAsync(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that has since been deleted or become unreadable is not worth
            // failing the export over; fall back to the picker's default location.
            return null;
        }
    }

    // =====================================================================
    // Remembering where you were
    // =====================================================================

    private void RestoreWindow()
    {
        if (!_settings.HasWindowBounds)
            return;
        // Only restore a position that still lands on a screen; an external display
        // that is no longer attached would otherwise put the window out of reach.
        if (_settings is { WindowX: { } x, WindowY: { } y } && IsOnAScreen(x, y))
            Position = new PixelPoint((int)x, (int)y);
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private bool IsOnAScreen(double x, double y) =>
        Screens.All.Any(screen => screen.Bounds.Contains(new PixelPoint((int)x, (int)y)));

    private void RememberWindow()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
        }
        _settings.LastView = VM.CurrentView;
        _settings.Save();
    }

    /// <summary>Fills the Open Recent submenu from the saves that have been opened.</summary>
    private void RebuildRecentMenu()
    {
        if (_recentMenu?.Menu is not { } submenu)
            return;

        submenu.Items.Clear();
        _settings.PruneMissing();
        if (_settings.Recent.Count == 0)
        {
            submenu.Items.Add(new NativeMenuItem("No recent saves") { IsEnabled = false });
            return;
        }
        foreach (var path in _settings.Recent)
        {
            var item = new NativeMenuItem(Path.GetFileName(path)) { ToolTip = path };
            var target = path;
            item.Click += (_, _) => Run(() => OpenPathAsync(target));
            submenu.Items.Add(item);
        }
    }

    private static NativeMenuItem? FindMenuItem(NativeMenu? menu, string header)
    {
        if (menu is null)
            return null;
        foreach (var item in menu.Items)
        {
            if (item is not NativeMenuItem entry)
                continue;
            if (entry.Header == header)
                return entry;
            if (FindMenuItem(entry.Menu, header) is { } found)
                return found;
        }
        return null;
    }

    // =====================================================================
    // Keyboard
    // =====================================================================

    public void OnPaletteOpenClicked(object? sender, EventArgs e) => OpenPalette();

    private void OpenPalette()
    {
        VM.Palette.Open();
        // Focus has to wait for the overlay to be realised before it can take it.
        Dispatcher.UIThread.Post(() => PaletteBox.Focus());
    }

    private void OnPaletteActivate(object? sender, TappedEventArgs e) => VM.Palette.Activate();

    /// <summary>
    /// Drives the palette and the grids from the keyboard. Handled here rather than on
    /// the controls so the shortcuts work from anywhere.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var palette = VM.Palette;
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            OpenPalette();
            e.Handled = true;
            return;
        }
        if (!palette.IsOpen && HandleGridKey(e))
        {
            e.Handled = true;
            return;
        }
        if (palette.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    palette.Close();
                    e.Handled = true;
                    return;
                case Key.Down:
                    palette.MoveSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    palette.MoveSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    palette.Activate();
                    e.Handled = true;
                    return;
            }
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// Arrow keys, delete and clipboard shortcuts over the box and party grids.
    /// </summary>
    /// <remarks>
    /// Only acts when a slot is selected and the focus is not in a field, so typing a
    /// nickname or a seed is never intercepted.
    /// </remarks>
    private bool HandleGridKey(KeyEventArgs e)
    {
        if (!VM.ShowBoxes || VM.SelectedSlot is not { } slot || IsTypingSomewhere())
            return false;

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Left:
                VM.MoveSelection(-1, 0);
                return true;
            case Key.Right:
                VM.MoveSelection(1, 0);
                return true;
            case Key.Up:
                VM.MoveSelection(0, -1);
                return true;
            case Key.Down:
                VM.MoveSelection(0, 1);
                return true;
            case Key.Delete or Key.Back:
                VM.DeleteSlot(slot);
                return true;
            case Key.C when command:
                VM.CopySlot(slot);
                return true;
            case Key.V when command:
                VM.PasteSlot(slot);
                return true;
        }
        return false;
    }

    /// <summary>True when a text field has focus, so its keys are its own.</summary>
    private bool IsTypingSomewhere() =>
        FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or NumericUpDown;

    /// <summary>
    /// Stops the scroll wheel changing values, and scrolls the page instead.
    /// </summary>
    /// <remarks>
    /// A combo box changes its selection on wheel and a spinner increments, so scrolling
    /// down a dense panel silently rewrote whatever the pointer passed over — an ability,
    /// a nature, a ball. In a save editor that is data loss disguised as navigation, and
    /// with 97 such controls it has to be handled once rather than per control.
    ///
    /// An open dropdown keeps the wheel, because there the scroll means its list.
    /// </remarks>
    private void OnWheelBeforeControls(object? sender, PointerWheelEventArgs e)
    {
        if (e.Source is not Control source)
            return;

        var combo = source.FindAncestorOfType<ComboBox>();
        if (combo is { IsDropDownOpen: true })
            return;

        Control? guarded = combo;
        guarded ??= source.FindAncestorOfType<NumericUpDown>();
        if (guarded is null)
            return;

        e.Handled = true;
        ScrollAround(guarded, e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X);
    }

    /// <summary>Applies the wheel to the nearest scrollable ancestor.</summary>
    private static void ScrollAround(Control from, double delta)
    {
        if (delta == 0 || from.FindAncestorOfType<ScrollViewer>() is not { } viewer)
            return;
        // Roughly three lines per notch, matching the default scroll step.
        var target = viewer.Offset.Y - (delta * 50);
        var limit = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(target, 0, limit));
    }

    // =====================================================================
    // Window chrome
    // =====================================================================

    /// <summary>
    /// Lets the whole width of the title-bar band drag the window, not just the strips
    /// the sidebar and inspector happen to reserve.
    /// </summary>
    /// <remarks>
    /// The client area is extended to the window edge so content can reach the top,
    /// which left the band above the party and the inspector inert — the window could
    /// only be moved by grabbing the sidebar. This runs only for presses no control
    /// consumed, so tabs, buttons and slots in that band keep working; anything that
    /// reaches here is background.
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled)
            return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || point.Position.Y > TitleBarHeight)
            return;
        BeginMoveDrag(e);
    }

    public void OnTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SaveTabViewModel tab })
            Workspace.Active = tab;
    }

    // =====================================================================
    // Closing with unsaved edits
    // =====================================================================

    /// <summary>Set once the user has decided, so the second close attempt goes through.</summary>
    private bool _closeConfirmed;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Recorded before the unsaved-changes guard, so the window is remembered even
        // when the close is then cancelled.
        RememberWindow();
        if (!_closeConfirmed && Workspace.HasPendingWork)
        {
            // Editing happens against a copy in memory, so closing would silently
            // discard it. Hold the window open and ask.
            e.Cancel = true;
            PromptBeforeLeaving();
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// Cmd+Q asks the application to quit rather than closing the window, so it never
    /// reaches OnClosing. Without this, the most common way a Mac user leaves an app
    /// would still discard their edits silently.
    /// </summary>
    private void HookShutdownRequest()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime life)
            return;
        life.ShutdownRequested += (_, e) =>
        {
            if (_closeConfirmed || !Workspace.HasPendingWork)
            {
                RememberWindow();
                return;
            }
            e.Cancel = true;
            PromptBeforeLeaving();
        };
    }

    /// <summary>Brings the first dirty tab forward and asks what to do with it.</summary>
    private void PromptBeforeLeaving()
    {
        if (Workspace.FirstPendingTab() is { } pending && !ReferenceEquals(Workspace.Active, pending))
            Workspace.Active = pending;

        var multi = Workspace.DescribePendingTabs();
        VM.ClosePromptNote = multi.Length > 0
            ? multi
            : VM.Detail.IsDirty
                ? "A Pokémon in the inspector also has edits that were never applied to its slot."
                : string.Empty;
        VM.IsClosePromptOpen = true;
    }

    public void OnCloseDiscardClicked(object? sender, RoutedEventArgs e)
    {
        _closeConfirmed = true;
        VM.IsClosePromptOpen = false;
        Close();
    }

    public void OnCloseExportClicked(object? sender, RoutedEventArgs e) => Run(ExportThenCloseAsync);

    /// <summary>
    /// Exports the active save, then closes only if nothing anywhere is still unsaved.
    /// A cancelled picker keeps the work; a second dirty tab gets its own prompt.
    /// </summary>
    private async Task ExportThenCloseAsync()
    {
        VM.IsClosePromptOpen = false;
        await ExportAsync();
        if (VM.HasPendingWork)
            return;
        if (Workspace.HasPendingWork)
        {
            PromptBeforeLeaving();
            return;
        }
        _closeConfirmed = true;
        Close();
    }

    // =====================================================================
    // Slot selection and drag between slots
    // =====================================================================

    private SlotViewModel? _dragSource;
    private SlotViewModel? _dragOverSlot;
    private Point _dragStart;
    private bool _dragPending;
    private bool _dragging;
    private int _dragOverBoxIndex = -1;

    private static SlotViewModel? SlotOf(object? sender) =>
        (sender as Control)?.DataContext as SlotViewModel;

    private void OnSlotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (SlotOf(sender) is not { } slot)
            return;
        // A slot press is ours. Saying so stops the window-drag fallback treating it as
        // a click on empty chrome, which would fight the slot's own drag.
        e.Handled = true;
        var point = e.GetCurrentPoint(this);
        VM.SelectSlot(slot);
        if (point.Properties.IsLeftButtonPressed && !slot.IsEmpty)
        {
            _dragSource = slot;
            _dragStart = point.Position;
            _dragPending = true;
            // Capture so we keep receiving moves/release anywhere in the window.
            e.Pointer.Capture(sender as IInputElement);
        }
    }

    private void OnSlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null)
            return;
        var pos = e.GetCurrentPoint(this).Position;

        if (_dragPending)
        {
            if (Math.Abs(pos.X - _dragStart.X) < DragThreshold && Math.Abs(pos.Y - _dragStart.Y) < DragThreshold)
                return;
            // Threshold crossed: start the drag and show the sprite ghost.
            _dragPending = false;
            _dragging = true;
            DragGhost.Source = _dragSource.Sprite;
            DragGhost.IsVisible = true;
        }

        if (!_dragging)
            return;

        // Ghost follows the pointer, centred on it.
        Canvas.SetLeft(DragGhost, pos.X - (DragGhost.Width / 2));
        Canvas.SetTop(DragGhost, pos.Y - (DragGhost.Height / 2));
        HighlightDropTarget(SlotAt(pos));
    }

    private void OnSlotPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasDragging = _dragging;
        var source = _dragSource;
        var target = wasDragging ? SlotAt(e.GetCurrentPoint(this).Position) : null;
        var boxIndex = _dragOverBoxIndex;

        EndDrag();
        e.Pointer.Capture(null);

        if (!wasDragging || source is null)
            return;
        if (target is not null && target != source)
            VM.MoveOrSwapSlot(source, target);
        else if (target is null && boxIndex >= 0)
            VM.MoveSlotToBox(source, boxIndex); // dropped on a box name in the sidebar
    }

    private void EndDrag()
    {
        _dragPending = false;
        _dragging = false;
        _dragSource = null;
        _dragOverBoxIndex = -1;
        DragGhost.IsVisible = false;
        DragGhost.Source = null;
        HighlightDropTarget(null);
    }

    /// <summary>Applies the drag-over highlight to a single slot at a time.</summary>
    private void HighlightDropTarget(SlotViewModel? slot)
    {
        if (ReferenceEquals(_dragOverSlot, slot))
            return;
        if (_dragOverSlot is not null)
            _dragOverSlot.IsDragOver = false;
        _dragOverSlot = slot;
        if (slot is not null && !ReferenceEquals(slot, _dragSource))
            slot.IsDragOver = true;
    }

    /// <summary>Finds the slot under a window-relative point, if any.</summary>
    private SlotViewModel? SlotAt(Point point) =>
        this.GetVisualsAt(point).Select(SlotUnder).FirstOrDefault(s => s is not null);

    /// <summary>Walks up from a visual to the slot card containing it, if any.</summary>
    private static SlotViewModel? SlotUnder(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is Border { DataContext: SlotViewModel slot } b && b.Classes.Contains("slot"))
                return slot;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    /// <summary>While dragging a Pokémon, hovering a box name marks it as the drop target.</summary>
    private void OnBoxNamePointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_dragging || sender is not Control { DataContext: string name })
            return;
        _dragOverBoxIndex = VM.BoxNames.IndexOf(name);
    }

    /// <summary>Leaving the name un-marks it, so a drop elsewhere does not land in a box brushed past.</summary>
    private void OnBoxNamePointerExited(object? sender, PointerEventArgs e) => _dragOverBoxIndex = -1;

    /// <summary>Any tap in the box list returns to the box grid, even when the
    /// tapped box was already the selected one (no SelectionChanged fires then).</summary>
    private void OnBoxListTapped(object? sender, TappedEventArgs e) => VM.SetView("boxes");

    // =====================================================================
    // Files dragged in from Finder
    // =====================================================================

    // Slot-to-slot moves are handled by the custom ghost drag above; these handlers
    // exist for files dragged in from Finder (save files and .pk* entities).
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;
        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (files is not { Count: > 0 })
            return;
        var target = SlotUnder(e.Source as Visual);
        Run(() => HandleFileDropAsync(files, target));
    }

    /// <summary>
    /// Entity files import into slots — the dropped-on slot first, then the selected one,
    /// then whatever is empty in the current box. Anything else is treated as a save.
    /// </summary>
    private async Task HandleFileDropAsync(IReadOnlyList<string> paths, SlotViewModel? dropTarget)
    {
        var slot = dropTarget;
        foreach (var path in paths)
        {
            if (!EntityFiles.HasEntityExtension(path) || VM.Save is null)
            {
                await OpenPathAsync(path);
                continue;
            }
            slot ??= VM.SelectedSlot ?? VM.BoxSlots.FirstOrDefault(s => s.IsEmpty);
            if (slot is null)
            {
                await ShowMessage("No Room", "No empty slot available in the current box.");
                return;
            }
            if (!VM.ImportEntityFile(slot, path, out var message))
                await ShowMessage("Import Failed", message);
            slot = null; // the next file finds its own empty slot
        }
    }

    // =====================================================================
    // Slot context menu
    // =====================================================================

    public void OnSlotCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            VM.CopySlot(slot);
    }

    public void OnSlotPasteClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            VM.PasteSlot(slot);
    }

    public void OnSlotDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            VM.DeleteSlot(slot);
    }

    public void OnSlotRevertClicked(object? sender, RoutedEventArgs e) => VM.RevertSlot(SlotOf(sender));

    public void OnSlotImportClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            Run(() => ImportEntityAsync(slot));
    }

    public void OnSlotExportClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            Run(() => ExportEntityAsync(slot));
    }

    public void OnSlotShowdownClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            Run(() => CopyShowdownAsync(slot));
    }

    private async Task ImportEntityAsync(SlotViewModel slot)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Pokémon File",
            AllowMultiple = false,
            FileTypeFilter = [EntityFileType, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        if (!VM.ImportEntityFile(slot, path, out var message))
            await ShowMessage("Import Failed", message);
    }

    private async Task ExportEntityAsync(SlotViewModel slot)
    {
        if (slot.Pokemon is not { Species: > 0 } pk)
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Pokémon File",
            SuggestedFileName = pk.FileName,
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        if (!VM.ExportEntityFile(slot, path, out var message))
            await ShowMessage("Export Failed", message);
    }

    private async Task CopyShowdownAsync(SlotViewModel slot)
    {
        var text = VM.GetSlotShowdownText(slot);
        if (text is not null && Clipboard is not null)
            await Clipboard.SetTextAsync(text);
    }

    // =====================================================================
    // Showdown via menu
    // =====================================================================

    public void OnShowdownImportClicked(object? sender, EventArgs e) => Run(ImportShowdownAsync);
    public void OnShowdownExportClicked(object? sender, EventArgs e) => Run(ExportShowdownAsync);

    private async Task ImportShowdownAsync()
    {
        if (Clipboard is null)
            return;
        var text = await Clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await ShowMessage("Clipboard Empty", "Copy a Showdown set to the clipboard first.");
            return;
        }
        if (!VM.Detail.HasPokemon)
        {
            await ShowMessage("No Pokémon Selected", "Select a Pokémon to apply the Showdown set to.");
            return;
        }
        if (!VM.Detail.ImportShowdownSet(text, out var message))
            await ShowMessage("Showdown Import Failed", message);
    }

    private async Task ExportShowdownAsync()
    {
        var text = VM.Detail.GetShowdownText();
        if (text is null)
        {
            await ShowMessage("No Pokémon Selected", "Select a Pokémon first.");
            return;
        }
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(text);
    }

    // =====================================================================
    // Menus: trainer, databases, box tools
    // =====================================================================

    public void OnTrainerClicked(object? sender, EventArgs e) => VM.SetView("save");
    public void OnBagClicked(object? sender, EventArgs e) => VM.ShowBagCommand.Execute(null);
    public void OnAddPokemonClicked(object? sender, EventArgs e) => VM.SetView("add");
    public void OnGiftsClicked(object? sender, EventArgs e) => VM.SetView("gifts");
    public void OnSortBoxClicked(object? sender, EventArgs e) => VM.SortCurrentBoxCommand.Execute(null);
    public void OnClearBoxClicked(object? sender, EventArgs e) => Run(ConfirmClearBoxAsync);
    public void OnRevertAllMenuClicked(object? sender, EventArgs e) => VM.RequestRevert();
    public void OnDumpBoxClicked(object? sender, EventArgs e) => Run(DumpBoxAsync);
    public void OnLoadFolderClicked(object? sender, EventArgs e) => Run(LoadFolderAsync);

    /// <summary>Leaves a database view without having to click a box slot to escape it.</summary>
    public void OnCloseDatabaseClicked(object? sender, RoutedEventArgs e) => VM.SetView("boxes");

    private void OnAddPreviewClicked(object? sender, RoutedEventArgs e) => VM.AddPreviewToBoxCommand.Execute(null);

    private async Task ConfirmClearBoxAsync()
    {
        if (VM.Save is null)
            return;
        var confirmed = await ShowConfirm("Clear Box",
            $"Delete every Pokémon in \"{VM.CurrentBoxName}\"? This cannot be undone (until you re-open the save without exporting).");
        if (confirmed)
            VM.ClearCurrentBoxCommand.Execute(null);
    }

    private async Task DumpBoxAsync()
    {
        if (VM.Save is null)
        {
            await ShowMessage("No Save Loaded", "Open a save file first (⌘O).");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export Box To…",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir)
            return;
        if (VM.DumpToFolder(dir) == 0)
            await ShowMessage("Nothing Exported", "There were no Pokémon to write.");
    }

    private async Task LoadFolderAsync()
    {
        if (VM.Save is null)
        {
            await ShowMessage("No Save Loaded", "Open a save file first (⌘O).");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import Pokémon Files From…",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir)
            return;
        var result = VM.LoadFromFolder(dir);
        if (result.Loaded == 0)
        {
            await ShowMessage("Nothing Imported",
                result.Skipped > 0
                    ? $"None of the {result.Skipped} file(s) could be read as Pokémon for this save."
                    : "That folder contains no Pokémon files.");
        }
    }

    // =====================================================================
    // Panels that need the window: block backup, search folder, report clipboard
    // =====================================================================

    public void OnExportBlockClicked(object? sender, RoutedEventArgs e) => Run(ExportBlockAsync);
    public void OnImportBlockClicked(object? sender, RoutedEventArgs e) => Run(ImportBlockAsync);
    public void OnChooseSearchFolderClicked(object? sender, RoutedEventArgs e) => Run(ChooseSearchFolderAsync);

    public void OnCopyReportClicked(object? sender, RoutedEventArgs e)
    {
        if (VM.Tools is { } tools && Clipboard is not null)
            Run(() => Clipboard.SetTextAsync(tools.BuildReportText()));
    }

    private async Task ExportBlockAsync()
    {
        if (VM.SaveBlocks is not { } blocks || blocks.GetSelectedBytes() is not { } data)
        {
            await ShowMessage("No Block Selected", "Pick a block in the list first.");
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Save Block",
            SuggestedFileName = $"{blocks.SelectedRow?.KeyText ?? "block"}.bin",
            ShowOverwritePrompt = true,
            FileTypeChoices = [BlockFileType],
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        await File.WriteAllBytesAsync(path, data);
        await ShowMessage("Block Exported", $"Wrote {data.Length} bytes to {Path.GetFileName(path)}.");
    }

    private async Task ImportBlockAsync()
    {
        if (VM.SaveBlocks is not { } blocks)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Save Block",
            AllowMultiple = false,
            FileTypeFilter = [BlockFileType, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        var data = await File.ReadAllBytesAsync(path);
        if (!blocks.ImportSelectedBytes(data, out var message))
            await ShowMessage("Import Failed", message);
    }

    private async Task ChooseSearchFolderAsync()
    {
        if (VM.Search is not { } search)
            return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a Folder of Pokémon Files",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } dir)
            search.FolderPath = dir;
    }

    // =====================================================================
    // Dialogs
    // =====================================================================

    private Task ShowMessage(string title, string message) => ShowDialog(title, message, confirm: false);

    private Task<bool> ShowConfirm(string title, string message) => ShowDialog(title, message, confirm: true);

    private async Task<bool> ShowDialog(string title, string message, bool confirm)
    {
        var result = false;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (confirm)
        {
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = confirm ? "Confirm" : "OK", IsDefault = true, IsCancel = !confirm };
        ok.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        buttons.Children.Add(ok);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            MaxWidth = 440,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }
}
