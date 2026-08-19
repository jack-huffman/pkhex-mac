using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    private PointerPressedEventArgs? _dragPressArgs;
    private Avalonia.Point _dragStart;
    private bool _dragPending;

    private const string SlotDragPrefix = "pkhex-slot:";

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
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
                _dragPressArgs = e;
                _dragStart = point.Position;
                _dragPending = true;
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
        if (!_dragPending || _dragSource is null || _dragPressArgs is null)
            return;
        var pos = e.GetCurrentPoint(this).Position;
        var dx = Math.Abs(pos.X - _dragStart.X);
        var dy = Math.Abs(pos.Y - _dragStart.Y);
        if (dx < 6 && dy < 6)
            return;

        _dragPending = false;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText($"{SlotDragPrefix}{_dragSource.Box}:{_dragSource.Slot}"));
        _ = DragDrop.DoDragDropAsync(_dragPressArgs, transfer, DragDropEffects.Move);
    }

    private void OnSlotPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragPending = false;
    }

    // =====================================================================
    // Drag & drop targets
    // =====================================================================

    private bool IsSlotDrag(DragEventArgs e) =>
        e.DataTransfer.TryGetText() is { } text && text.StartsWith(SlotDragPrefix, StringComparison.Ordinal);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (IsSlotDrag(e))
            e.DragEffects = FindSlotTarget(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
        else if (e.DataTransfer.Contains(DataFormat.File))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (IsSlotDrag(e))
        {
            var target = FindSlotTarget(e);
            if (target is not null && _dragSource is not null && target != _dragSource)
                VM.MoveOrSwapSlot(_dragSource, target);
            _dragSource = null;
            return;
        }

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

    public void OnTrainerClicked(object? sender, EventArgs e) => VM.SetView("save");
    public void OnBagClicked(object? sender, EventArgs e) => VM.SetView("save");
    public void OnAddPokemonClicked(object? sender, EventArgs e) => VM.SetView("add");
    public void OnGiftsClicked(object? sender, EventArgs e) => VM.SetView("gifts");

    private void OnSaveApplyClicked(object? sender, RoutedEventArgs e) => VM.ApplySave();
    private void OnSaveRevertClicked(object? sender, RoutedEventArgs e) => VM.ResetSave();
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
