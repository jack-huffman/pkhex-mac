using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// One open save, as it appears in the tab strip.
/// </summary>
/// <remarks>
/// Wraps a whole <see cref="MainWindowViewModel"/>: each tab is an independent session
/// with its own save, its own untouched copy for reverting, and its own unsaved-change
/// tracking. Nothing is shared between them except the sprite cache and preferences.
/// </remarks>
public partial class SaveTabViewModel : ObservableObject
{
    public SaveTabViewModel(MainWindowViewModel session)
    {
        Session = session;
        Refresh();
    }

    public MainWindowViewModel Session { get; }

    [ObservableProperty] private string _title = "No save";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _isActive;

    /// <summary>Shown as a dot on the tab while the session has unwritten edits.</summary>
    public bool IsDirty => Session.SaveState.HasUnsavedChanges;

    /// <summary>Re-reads the label after a save is opened or exported.</summary>
    public void Refresh()
    {
        var sav = Session.SAV;
        if (sav is null)
        {
            Title = "No save";
            Subtitle = string.Empty;
            return;
        }
        // Trainer name distinguishes two files of the same game far better than the
        // file name does; saves are almost always called "main".
        Title = string.IsNullOrWhiteSpace(sav.OT) ? FileName() : sav.OT;
        Subtitle = Session.GameName;
        OnPropertyChanged(nameof(IsDirty));
    }

    private string FileName()
    {
        var path = Session.SavePath;
        return string.IsNullOrEmpty(path) ? "Untitled" : Path.GetFileName(path);
    }
}
