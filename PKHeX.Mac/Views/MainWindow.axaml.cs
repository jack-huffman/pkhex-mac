using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PKHeX.Mac.Services;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private SlotViewModel? _dragSource;
    private SlotViewModel? _dragOverSlot;
    private Avalonia.Point _dragStart;
    private bool _dragPending;
    private bool _dragging;
    private int _dragOverBoxIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Loaded += OnWindowLoaded;
        HookShutdownRequest();
        // The box grid sizes itself to the window, and the second box appears once
        // there is room for it, so both need recomputing on every resize.
        SizeChanged += (_, _) => RefreshBoxLayout();
        // Tunnels, so the wheel is intercepted before a dropdown or spinner can act on it.
        AddHandler(PointerWheelChangedEvent, OnWheelBeforeControls, RoutingStrategies.Tunnel);
    }

    /// <summary>Window bounds, last view and recent saves, kept between launches.</summary>
    private readonly AppSettings _settings = AppSettings.Load();

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        // DataContext is assigned after construction, so this cannot be set up earlier.
        RestoreWindow();
        var first = new SaveTabViewModel(VM);
        Tabs.Add(first);
        _activeTab = first;
        first.IsActive = true;
        WireSession(VM);
        RebuildRecentMenu();
        RefreshBoxLayout();
        _ = VM.CheckForUpstreamUpdateAsync();
    }

    // =====================================================================
    // File open / export
    // =====================================================================

    private static readonly FilePickerFileType SaveFileType = new("Pokémon Save Files")
    {
        Patterns = ["main", "*.sav", "*.dsv", "*.dat", "*.gci", "*.bin", "*.raw", "*.sav.bak", "*"],
    };

    private static readonly FilePickerFileType EntityFileType = new("Pokémon Entity Files")
    {
        Patterns = ["*.pk*", "*.pb7", "*.pb8", "*.ck3", "*.xk3", "*.sk2", "*.bk4", "*.rk4", "*.ek*"],
    };

    public void OnOpenClicked(object? sender, EventArgs e) => _ = OpenAsync();
    public void OnOpenButtonClicked(object? sender, RoutedEventArgs e) => _ = OpenAsync();

    public void OnExportClicked(object? sender, EventArgs e) => _ = ExportAsync();

    /// <summary>
    /// Hands the boxes column's dimensions to the view model. The inspector is a fixed
    /// column, so subtracting it and the sidebar gives the space the grid actually has.
    /// </summary>
    private void RefreshBoxLayout()
    {
        var width = Bounds.Width - 238 - VM.InspectorWidth.Value;
        var height = Bounds.Height - 46 - 26;      // drag strip and status line
        if (width > 0 && height > 0)
            VM.UpdateBoxLayout(width, height);
    }

    /// <summary>Jumps to a Pokémon the insights panel flagged.</summary>
    public void OnBoxProblemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BoxProblemViewModel problem })
            problem.Select();
    }

    /// <summary>Opens a save by path, used by the Open Recent menu.</summary>
    private async void OpenPath(string path)
    {
        try
        {
            if (!OpenInTab(path, out var error))
            {
                await ShowError("Could Not Open Save", error);
                // A file that no longer opens should stop being offered.
                _settings.Recent.Remove(path);
                _settings.Save();
            }
            RebuildRecentMenu();
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
        }
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
            case Key.Left:  VM.MoveSelection(-1, 0); return true;
            case Key.Right: VM.MoveSelection(1, 0); return true;
            case Key.Up:    VM.MoveSelection(0, -1); return true;
            case Key.Down:  VM.MoveSelection(0, 1); return true;

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

    // ---- Open saves, one per tab ----

    /// <summary>
    /// Whether to show the tab strip. A styled property so the view can bind to it;
    /// a plain field would never notify.
    /// </summary>
    public static readonly StyledProperty<bool> ShowTabStripProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(ShowTabStrip));

    public bool ShowTabStrip
    {
        get => GetValue(ShowTabStripProperty);
        set => SetValue(ShowTabStripProperty, value);
    }

    /// <summary>
    /// Every open save. The window's DataContext is always the active session, so the
    /// rest of the interface is unaware that more than one exists.
    /// </summary>
    public ObservableCollection<SaveTabViewModel> Tabs { get; } = [];

    private SaveTabViewModel? _activeTab;

    /// <summary>Every open save except the one asking, as transfer destinations.</summary>
    private List<TransferTarget> OtherSavesFor(MainWindowViewModel asking)
    {
        var targets = new List<TransferTarget>();
        foreach (var tab in Tabs)
        {
            if (ReferenceEquals(tab.Session, asking) || tab.Session.SAV is not { } sav)
                continue;
            var session = tab.Session;
            targets.Add(new TransferTarget($"{tab.Title} · {tab.Subtitle}", sav,
                note => session.NoteExternalChange(note)));
        }
        return targets;
    }

    /// <summary>Tells every tab that the set of open saves changed.</summary>
    private void RefreshAllTransferTargets()
    {
        foreach (var tab in Tabs)
            tab.Session.RefreshTransferTargets();
    }

    /// <summary>Switches the whole interface to another open save.</summary>
    public void Activate(SaveTabViewModel tab)
    {
        if (ReferenceEquals(_activeTab, tab))
            return;
        if (_activeTab is not null)
            _activeTab.IsActive = false;
        _activeTab = tab;
        tab.IsActive = true;
        DataContext = tab.Session;
        WireSession(tab.Session);
        RefreshBoxLayout();
    }

    /// <summary>Opens a save in a new tab, reusing the current one if it is empty.</summary>
    private bool OpenInTab(string path, out string error)
    {
        // An untouched empty tab is a placeholder, not a document worth keeping.
        if (_activeTab is { Session.SAV: null } empty)
        {
            var ok = empty.Session.LoadSave(path, out error);
            if (ok)
            {
                empty.Refresh();
                RefreshAllTransferTargets();
            }
            return ok;
        }

        var session = new MainWindowViewModel();
        if (!session.LoadSave(path, out error))
            return false;

        var tab = new SaveTabViewModel(session);
        Tabs.Add(tab);
        ShowTabStrip = Tabs.Count > 1;
        Activate(tab);
        RefreshAllTransferTargets();
        return true;
    }

    /// <summary>Closes a tab, keeping at least one open so the window is never blank.</summary>
    public void CloseTab(SaveTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.Remove(tab);
        ShowTabStrip = Tabs.Count > 1;
        RefreshAllTransferTargets();
        if (Tabs.Count == 0)
        {
            var session = new MainWindowViewModel();
            var replacement = new SaveTabViewModel(session);
            Tabs.Add(replacement);
            _activeTab = null;
            Activate(replacement);
            return;
        }
        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = null;
            Activate(Tabs[Math.Min(index, Tabs.Count - 1)]);
        }
    }

    public void OnTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SaveTabViewModel tab })
            Activate(tab);
    }

    public void OnTabCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SaveTabViewModel tab })
            CloseTab(tab);
    }

    /// <summary>Connects a session to the window-level services it needs.</summary>
    private void WireSession(MainWindowViewModel session)
    {
        session.LayoutChanged = RefreshBoxLayout;
        session.ExportRequested = () => _ = ExportAsync();
        session.Settings = _settings;
        session.SettingsChanged = () => _settings.Save();
        session.SaveState.PropertyChanged += (_, _) => _activeTab?.Refresh();
        session.OtherSaves = () => OtherSavesFor(session);
        session.AttachTransfer();
    }

    // ---- Remembering where you were ----

    private void RestoreWindow()
    {
        if (!_settings.HasWindowBounds)
            return;
        // Only restore a position that still lands on a screen; an external display
        // that is no longer attached would otherwise put the window out of reach.
        if (!double.IsNaN(_settings.WindowX) && !double.IsNaN(_settings.WindowY)
            && IsOnAScreen(_settings.WindowX, _settings.WindowY))
        {
            Position = new PixelPoint((int)_settings.WindowX, (int)_settings.WindowY);
        }
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private bool IsOnAScreen(double x, double y)
    {
        foreach (var screen in Screens.All)
        {
            if (screen.Bounds.Contains(new PixelPoint((int)x, (int)y)))
                return true;
        }
        return false;
    }

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
        var menu = NativeMenu.GetMenu(this);
        var recentItem = FindRecentMenuItem(menu);
        if (recentItem?.Menu is not { } submenu)
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
            var item = new NativeMenuItem(Path.GetFileName(path))
            {
                ToolTip = path,
            };
            var target = path;
            item.Click += (_, _) => OpenPath(target);
            submenu.Items.Add(item);
        }
    }

    private static NativeMenuItem? FindRecentMenuItem(NativeMenu? menu)
    {
        if (menu is null)
            return null;
        foreach (var item in menu.Items)
        {
            if (item is not NativeMenuItem entry)
                continue;
            if (entry.Header == "Open Recent")
                return entry;
            if (FindRecentMenuItem(entry.Menu) is { } found)
                return found;
        }
        return null;
    }

    // ---- Command palette ----

    public void OnPaletteOpenClicked(object? sender, EventArgs e) => OpenPalette();

    private void OpenPalette()
    {
        VM.Palette.Open();
        // Focus has to wait for the overlay to be realised before it can take it.
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("PaletteBox")?.Focus());
    }

    private void OnPaletteActivate(object? sender, TappedEventArgs e) => VM.Palette.Activate();

    /// <summary>
    /// Drives the palette from the keyboard. Handled here rather than on the overlay so
    /// the shortcut works from anywhere, including while a text field has focus.
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

    // ---- Closing with unsaved edits ----

    /// <summary>Set once the user has decided, so the second close attempt goes through.</summary>
    private bool _closeConfirmed;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Recorded before the unsaved-changes guard, so the window is remembered even
        // when the close is then cancelled.
        RememberWindow();
        if (!_closeConfirmed && AnyTabHasPendingWork())
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
            // Cmd+Q never reaches OnClosing, so the window state has to be recorded here
            // as well or quitting the usual way would forget it.
            RememberWindow();
            if (_closeConfirmed || !AnyTabHasPendingWork())
                return;
            e.Cancel = true;
            PromptBeforeLeaving();
        };
    }

    /// <summary>
    /// True when any open save has unwritten edits. Checking only the visible tab would
    /// let a background one be discarded in silence, which is the exact failure the
    /// guard exists to prevent.
    /// </summary>
    private bool AnyTabHasPendingWork()
    {
        foreach (var tab in Tabs)
        {
            if (tab.Session.HasPendingWork)
                return true;
        }
        return false;
    }

    /// <summary>Names the tabs with unsaved work, so the prompt is specific.</summary>
    private string DescribePendingTabs()
    {
        var names = new List<string>();
        foreach (var tab in Tabs)
        {
            if (tab.Session.HasPendingWork)
                names.Add(tab.Title);
        }
        return names.Count switch
        {
            0 => string.Empty,
            1 => string.Empty,      // the usual case; the existing wording covers it
            _ => $"Unsaved in {names.Count} open saves: {string.Join(", ", names)}.",
        };
    }

    private void PromptBeforeLeaving()
    {
        var multi = DescribePendingTabs();
        VM.ClosePromptNote = multi.Length > 0
            ? multi
            : VM.Detail.IsDirty
                ? "A Pokémon in the inspector also has edits that were never applied to its slot."
                : string.Empty;
        VM.IsClosePromptOpen = true;
    }

    // ---- Reverting ----

    /// <summary>Clears the slot selection, which empties the inspector.</summary>
    public void OnCloseDetailClicked(object? sender, RoutedEventArgs e) => VM.SelectSlot(null);

    /// <summary>Leaves a database view without having to click a box slot to escape it.</summary>
    public void OnCloseDatabaseClicked(object? sender, RoutedEventArgs e) => VM.SetView("boxes");

    public void OnRevertAllClicked(object? sender, RoutedEventArgs e) => VM.RequestRevert();

    public void OnRevertAllMenuClicked(object? sender, EventArgs e) => VM.RequestRevert();

    public void OnSlotRevertClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            VM.RevertSlot(slot);
    }

    /// <summary>Throws away edits typed into the inspector without touching the save.</summary>
    public void OnDetailRevertClicked(object? sender, RoutedEventArgs e) => VM.RevertDetailEdits();

    public void OnCloseCancelClicked(object? sender, RoutedEventArgs e) => VM.IsClosePromptOpen = false;

    public void OnCloseDiscardClicked(object? sender, RoutedEventArgs e)
    {
        _closeConfirmed = true;
        VM.IsClosePromptOpen = false;
        Close();
    }

    public void OnCloseExportClicked(object? sender, RoutedEventArgs e) => _ = ExportThenCloseAsync();

    private async Task ExportThenCloseAsync()
    {
        VM.IsClosePromptOpen = false;
        await ExportAsync();
        // Only leave if the export actually landed; a cancelled picker keeps the work.
        if (!VM.HasPendingWork)
        {
            _closeConfirmed = true;
            Close();
        }
    }
    public void OnExportButtonClicked(object? sender, RoutedEventArgs e) => _ = ExportAsync();

    /// <summary>
    /// Height of the title-bar band, matching ExtendClientAreaTitleBarHeightHint.
    /// </summary>
    private const double TitleBarHeight = 46;

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

    private async Task OpenAsync()
    {
        try
        {
            // No FileTypeFilter: Switch saves ("main", "main (1)", …) have no extension and
            // macOS open panels grey out files that don't match the filter. The engine sniffs
            // the format from content, so allow selecting anything.
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Pokémon Save File",
                AllowMultiple = false,
            });
            if (files.Count == 0)
                return;
            var path = files[0].TryGetLocalPath();
            if (path is null)
                return;
            if (!OpenInTab(path, out var error))
                await ShowError("Could Not Open Save", error);
            else
                RebuildRecentMenu();
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
        }
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
            if (string.IsNullOrEmpty(dir))
                return null;
            return await StorageProvider.TryGetFolderFromPathAsync(dir);
        }
        catch
        {
            // A folder that has since been deleted or become unreadable is not worth
            // failing the export over; fall back to the picker's default location.
            return null;
        }
    }

    private async Task ExportAsync()
    {
        try
        {
            if (VM.SAV is null)
            {
                await ShowError("No Save Loaded", "Open a save file first (⌘O).");
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
            if (file is null)
                return;
            var path = file.TryGetLocalPath();
            if (path is null)
                return;
            if (!VM.ExportSave(path, out var error))
                await ShowError("Could Not Export Save", error);
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
        }
    }

    // =====================================================================
    // Slot selection + drag source
    // =====================================================================

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
        if (point.Properties.IsLeftButtonPressed)
        {
            VM.SelectSlot(slot);
            if (!slot.IsEmpty)
            {
                _dragSource = slot;
                _dragStart = point.Position;
                _dragPending = true;
                // Capture so we keep receiving moves/release anywhere in the window.
                e.Pointer.Capture(sender as IInputElement);
            }
        }
        else
        {
            // Right-click: select so the context menu applies to this slot.
            VM.SelectSlot(slot);
        }
    }

    private void OnSlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null)
            return;
        var pos = e.GetCurrentPoint(this).Position;

        if (_dragPending)
        {
            if (Math.Abs(pos.X - _dragStart.X) < 6 && Math.Abs(pos.Y - _dragStart.Y) < 6)
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
    private SlotViewModel? SlotAt(Avalonia.Point point)
    {
        var visual = this.GetVisualsAt(point)
            .FirstOrDefault(v => FindSlotBorder(v) is not null);
        return visual is null ? null : FindSlotBorder(visual)?.DataContext as SlotViewModel;
    }

    private static Border? FindSlotBorder(Avalonia.Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is Border { DataContext: SlotViewModel } b && b.Classes.Contains("slot"))
                return b;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    // =====================================================================
    // Drag & drop targets
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
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (files is null || files.Count == 0)
                return;
            _ = HandleFileDropAsync(files[0], FindSlotTarget(e));
        }
    }

    private static SlotViewModel? FindSlotTarget(DragEventArgs e)
    {
        var visual = e.Source as Avalonia.Visual;
        while (visual is not null)
        {
            if (visual is Border { DataContext: SlotViewModel slot } b && b.Classes.Contains("slot"))
                return slot;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    private async Task HandleFileDropAsync(string path, SlotViewModel? targetSlot)
    {
        // Entity files (.pk*, .pb*, etc.) import into a slot; anything else is treated as a save.
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var isEntity = ext.Length is >= 3 and <= 4
                       && (ext.StartsWith("pk") || ext.StartsWith("pb") || ext.StartsWith("ek")
                           || ext is "ck3" or "xk3" or "sk2" or "bk4" or "rk4");

        if (isEntity && VM.SAV is not null)
        {
            var slot = targetSlot ?? VM.SelectedSlot ?? VM.BoxSlots.FirstOrDefault(s => s.IsEmpty);
            if (slot is null)
            {
                await ShowError("No Room", "No empty slot available in the current box.");
                return;
            }
            if (!VM.ImportEntityFile(slot, path, out var message))
                await ShowError("Import Failed", message);
        }
        else
        {
            if (!OpenInTab(path, out var error))
                await ShowError("Could Not Open File", error);
            else
                RebuildRecentMenu();
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

    public void OnSlotImportClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            _ = ImportEntityAsync(slot);
    }

    public void OnSlotExportClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            _ = ExportEntityAsync(slot);
    }

    public void OnSlotShowdownClicked(object? sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot)
            _ = CopyShowdownAsync(slot);
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
            await ShowError("Import Failed", message);
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
        if (file is null || file.TryGetLocalPath() is not { } path)
            return;
        try
        {
            var data = new byte[pk.SIZE_PARTY];
            pk.WriteDecryptedDataParty(data);
            await File.WriteAllBytesAsync(path, data);
        }
        catch (Exception ex)
        {
            await ShowError("Export Failed", ex.Message);
        }
    }

    private async Task CopyShowdownAsync(SlotViewModel slot)
    {
        var text = VM.GetSlotShowdownText(slot);
        if (text is null || Clipboard is null)
            return;
        await Clipboard.SetTextAsync(text);
    }

    // =====================================================================
    // Showdown via menu
    // =====================================================================

    public void OnShowdownImportClicked(object? sender, EventArgs e) => _ = ImportShowdownAsync();
    public void OnShowdownExportClicked(object? sender, EventArgs e) => _ = ExportShowdownAsync();

    private async Task ImportShowdownAsync()
    {
        if (Clipboard is null)
            return;
        var text = await Clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await ShowError("Clipboard Empty", "Copy a Showdown set to the clipboard first.");
            return;
        }
        if (!VM.Detail.HasPokemon)
        {
            await ShowError("No Pokémon Selected", "Select a Pokémon to apply the Showdown set to.");
            return;
        }
        if (!VM.Detail.ImportShowdownSet(text, out var message))
            await ShowError("Showdown Import Failed", message);
    }

    private async Task ExportShowdownAsync()
    {
        var text = VM.Detail.GetShowdownText();
        if (text is null)
        {
            await ShowError("No Pokémon Selected", "Select a Pokémon first.");
            return;
        }
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(text);
    }

    // =====================================================================
    // Trainer / database views (in-window, via sidebar navigation)
    // =====================================================================

    /// <summary>Any tap in the box list returns to the box grid, even when the
    /// tapped box was already the selected one (no SelectionChanged fires then).</summary>
    private void OnBoxListTapped(object? sender, Avalonia.Input.TappedEventArgs e) => VM.SetView("boxes");

    /// <summary>While dragging a Pokémon, hovering a box name marks it as the drop target.</summary>
    private void OnBoxNamePointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_dragging || sender is not Control { DataContext: string name })
            return;
        _dragOverBoxIndex = VM.BoxNames.IndexOf(name);
    }

    public void OnDumpBoxClicked(object? sender, EventArgs e) => _ = DumpBoxAsync();
    public void OnLoadFolderClicked(object? sender, EventArgs e) => _ = LoadFolderAsync();

    private async Task DumpBoxAsync()
    {
        if (VM.SAV is null)
        {
            await ShowError("No Save Loaded", "Open a save file first (⌘O).");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export Box To…",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir)
            return;
        var written = VM.DumpToFolder(dir);
        if (written == 0)
            await ShowError("Nothing Exported", "There were no Pokémon to write.");
    }

    private async Task LoadFolderAsync()
    {
        if (VM.SAV is null)
        {
            await ShowError("No Save Loaded", "Open a save file first (⌘O).");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import Pokémon Files From…",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir)
            return;
        var (loaded, skipped) = VM.LoadFromFolder(dir);
        if (loaded == 0)
            await ShowError("Nothing Imported",
                skipped > 0
                    ? $"None of the {skipped} file(s) could be read as Pokémon for this save."
                    : "That folder contains no Pokémon files.");
    }

    // ---- Save block backup / restore ----

    private static readonly FilePickerFileType BlockFileType = new("Save Block")
    {
        Patterns = ["*.bin"],
    };

    public void OnExportBlockClicked(object? sender, RoutedEventArgs e) => _ = ExportBlockAsync();
    public void OnImportBlockClicked(object? sender, RoutedEventArgs e) => _ = ImportBlockAsync();

    private async Task ExportBlockAsync()
    {
        if (VM.SaveBlocks is not { } blocks || blocks.GetSelectedBytes() is not { } data)
        {
            await ShowError("No Block Selected", "Pick a block in the list first.");
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Save Block",
            SuggestedFileName = $"{blocks.SelectedRow?.KeyText ?? "block"}.bin",
            ShowOverwritePrompt = true,
            FileTypeChoices = [BlockFileType],
        });
        if (file is null || file.TryGetLocalPath() is not { } path)
            return;
        try
        {
            await File.WriteAllBytesAsync(path, data);
            await ShowError("Block Exported", $"Wrote {data.Length} bytes to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            await ShowError("Export Failed", ex.Message);
        }
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
        try
        {
            var data = await File.ReadAllBytesAsync(path);
            if (!blocks.ImportSelectedBytes(data, out var message))
                await ShowError("Import Failed", message);
        }
        catch (Exception ex)
        {
            await ShowError("Import Failed", ex.Message);
        }
    }

    public void OnChooseSearchFolderClicked(object? sender, RoutedEventArgs e) => _ = ChooseSearchFolderAsync();

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

    public void OnCopyReportClicked(object? sender, RoutedEventArgs e)
    {
        if (VM.Tools is { } tools && Clipboard is not null)
            _ = Clipboard.SetTextAsync(tools.BuildReportText());
    }


    public void OnTrainerClicked(object? sender, EventArgs e) => VM.SetView("save");
    public void OnBagClicked(object? sender, EventArgs e) => VM.SetView("save");
    public void OnAddPokemonClicked(object? sender, EventArgs e) => VM.SetView("add");
    public void OnGiftsClicked(object? sender, EventArgs e) => VM.SetView("gifts");

    private void OnAddPreviewClicked(object? sender, RoutedEventArgs e) => VM.AddPreviewToBox();

    // =====================================================================
    // Box tools
    // =====================================================================

    public void OnSortBoxClicked(object? sender, EventArgs e) => VM.SortCurrentBox();

    public void OnClearBoxClicked(object? sender, EventArgs e) => _ = ConfirmClearBoxAsync();

    private async Task ConfirmClearBoxAsync()
    {
        if (VM.SAV is null)
            return;
        var confirmed = await ShowConfirm("Clear Box",
            $"Delete every Pokémon in \"{VM.CurrentBoxName}\"? This cannot be undone (until you re-open the save without exporting).");
        if (confirmed)
            VM.ClearCurrentBox();
    }

    // =====================================================================
    // Editor
    // =====================================================================

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        VM.ApplyDetailChanges();
    }

    // =====================================================================
    // Dialog helpers
    // =====================================================================

    private Task ShowError(string title, string message) => ShowDialog(title, message, confirm: false);

    private async Task<bool> ShowConfirm(string title, string message) =>
        await ShowDialog(title, message, confirm: true);

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
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = confirm ? "Confirm" : "OK" };
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        buttons.Children.Add(ok);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
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
