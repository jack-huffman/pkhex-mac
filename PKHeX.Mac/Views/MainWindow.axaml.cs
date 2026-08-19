using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
    }

    private static readonly FilePickerFileType SaveFileType = new("Pokémon Save Files")
    {
        Patterns = ["main", "*.sav", "*.dsv", "*.dat", "*.gci", "*.bin", "*.raw", "*.sav.bak", "*"],
    };

    public void OnOpenClicked(object? sender, EventArgs e) => _ = OpenAsync();
    public void OnOpenButtonClicked(object? sender, RoutedEventArgs e) => _ = OpenAsync();
    public void OnExportClicked(object? sender, EventArgs e) => _ = ExportAsync();

    private async Task OpenAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Pokémon Save File",
                AllowMultiple = false,
                FileTypeFilter = [SaveFileType, FilePickerFileTypes.All],
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

    private void OnSlotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SlotViewModel slot })
            VM.SelectSlot(slot);
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        VM.ApplyDetailChanges();
    }

    private async Task ShowError(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                MaxWidth = 420,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right },
                },
            },
        };
        if (dialog.Content is StackPanel { Children: [_, Button ok] })
            ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}
