using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Tracks whether the open save has edits that have not been written to disk.
/// </summary>
/// <remarks>
/// Edits are made against an in-memory copy of the save and only reach the file on
/// export, which is easy to forget and previously cost the user their work silently:
/// the window closed without asking. Every editor now reports through
/// <see cref="NoteChange"/>, which drives a persistent indicator, a session log, and
/// the guard on closing the window.
///
/// This replaces the habit of appending "remember to export" to status messages —
/// eighteen sites were doing that, which is what you resort to when the state model
/// cannot express "you have unsaved work".
/// </remarks>
public partial class SaveStateViewModel : ObservableObject
{
    /// <summary>Enough history to answer "what did I just do?" without unbounded growth.</summary>
    private const int MaxLogEntries = 60;

    public ObservableCollection<ChangeLogEntry> Log { get; } = [];

    [ObservableProperty] private int _pendingChanges;
    [ObservableProperty] private string _lastChange = string.Empty;
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private string _savedPath = string.Empty;

    public bool HasUnsavedChanges => PendingChanges > 0;

    public string CountText => PendingChanges switch
    {
        0 => "No unsaved changes",
        1 => "1 unsaved change",
        _ => $"{PendingChanges} unsaved changes",
    };

    /// <summary>Shown once a save has been written, so the state is never ambiguous.</summary>
    public string SavedText => SavedPath.Length == 0
        ? "Not exported this session"
        : $"Exported to {SavedPath}";

    partial void OnPendingChangesChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CountText));
    }

    partial void OnSavedPathChanged(string value) => OnPropertyChanged(nameof(SavedText));

    /// <summary>Records one edit. The description is shown as-is, so keep it plain.</summary>
    public void NoteChange(string description)
    {
        PendingChanges++;
        LastChange = description;
        Log.Insert(0, new ChangeLogEntry(description, DateTime.Now));
        while (Log.Count > MaxLogEntries)
            Log.RemoveAt(Log.Count - 1);
    }

    /// <summary>Called after a successful export: the file now matches memory.</summary>
    public void MarkSaved(string path)
    {
        PendingChanges = 0;
        LastChange = string.Empty;
        SavedPath = path;
        Log.Insert(0, new ChangeLogEntry($"Exported to {path}", DateTime.Now, IsMilestone: true));
    }

    /// <summary>Called when a different save is opened.</summary>
    public void Reset()
    {
        PendingChanges = 0;
        LastChange = string.Empty;
        SavedPath = string.Empty;
        IsLogOpen = false;
        Log.Clear();
    }

    [RelayCommand]
    public void ToggleLog() => IsLogOpen = !IsLogOpen;
}

/// <summary>One entry in the session log.</summary>
public sealed record ChangeLogEntry(string Description, DateTime At, bool IsMilestone = false)
{
    public string TimeText => At.ToString("HH:mm:ss");
}
