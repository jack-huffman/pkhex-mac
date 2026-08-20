using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Finds the save files on this Mac and offers them, instead of making the user go
/// hunting through a file dialog.
/// </summary>
public partial class SaveDiscoveryViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public ObservableCollection<DiscoveredSave> Results { get; } = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private DiscoveredSave? _selected;
    [ObservableProperty] private bool _hasScanned;

    public bool HasResults => Results.Count > 0;

    /// <summary>Invoked with the chosen file so the window can load it.</summary>
    public Action<DiscoveredSave>? OpenRequested { get; set; }

    /// <summary>Opens the panel, kicking off a first scan if none has run yet.</summary>
    public void Show()
    {
        IsOpen = true;
        if (!HasScanned && !IsScanning)
            _ = ScanAsync();
    }

    [RelayCommand]
    public void Close()
    {
        _cts?.Cancel();
        IsOpen = false;
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsScanning = true;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        Status = "Looking for save files…";

        try
        {
            // The walk touches the file system, so keep it off the UI thread.
            var found = await Task.Run(() => SaveDiscovery.Scan(token), token);
            if (token.IsCancellationRequested)
                return;

            foreach (var save in found)
                Results.Add(save);
            Selected = Results.Count > 0 ? Results[0] : null;
            Status = Results.Count switch
            {
                0 => "No save files found. Try Open Save File… and point me at one directly.",
                1 => "Found one save file.",
                _ => $"Found {Results.Count} save files, most recently played first.",
            };
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            HasScanned = true;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        Status = "Scan cancelled.";
        IsScanning = false;
    }

    /// <summary>Loads the highlighted save.</summary>
    [RelayCommand]
    public void OpenSelected()
    {
        if (Selected is not { } save)
        {
            Status = "Pick a save file first.";
            return;
        }
        OpenRequested?.Invoke(save);
        IsOpen = false;
    }
}
