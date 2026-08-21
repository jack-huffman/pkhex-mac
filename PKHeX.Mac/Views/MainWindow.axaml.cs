using System;
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
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        // DataContext is assigned after construction, so this cannot be set up earlier.
        VM.LayoutChanged = RefreshBoxLayout;
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

    // ---- Closing with unsaved edits ----

    /// <summary>Set once the user has decided, so the second close attempt goes through.</summary>
    private bool _closeConfirmed;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && VM.HasPendingWork)
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
            if (_closeConfirmed || !VM.HasPendingWork)
                return;
            e.Cancel = true;
            PromptBeforeLeaving();
        };
    }

    private void PromptBeforeLeaving()
    {
        VM.ClosePromptNote = VM.Detail.IsDirty
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

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
            if (!VM.LoadSave(path, out var error))
                await ShowError("Could Not Open Save", error);
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
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
            if (!VM.LoadSave(path, out var error))
                await ShowError("Could Not Open File", error);
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
