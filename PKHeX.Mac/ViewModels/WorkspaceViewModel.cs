using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The open saves — one <see cref="MainWindowViewModel"/> session per tab — and which
/// of them the window is showing.
/// </summary>
/// <remarks>
/// The window's DataContext is always the active session, so the rest of the interface
/// never learns that more than one exists. Everything that spans sessions lives here:
/// opening into a fresh tab or the empty placeholder, closing with a replacement so the
/// window is never blank, offering the other saves as transfer destinations, and knowing
/// whether <em>any</em> tab still has unsaved work when the window wants to close.
/// </remarks>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly Func<MainWindowViewModel> _createSession;

    public WorkspaceViewModel() : this(() => new MainWindowViewModel())
    {
    }

    /// <param name="createSession">How to make a session; injectable so tests can watch it.</param>
    public WorkspaceViewModel(Func<MainWindowViewModel> createSession)
    {
        _createSession = createSession;
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowTabStrip));
    }

    public ObservableCollection<SaveTabViewModel> Tabs { get; } = [];

    /// <summary>The tab the window is showing.</summary>
    [ObservableProperty] private SaveTabViewModel? _active;

    /// <summary>Hidden until there is more than one save, so nothing changes for the usual case.</summary>
    public bool ShowTabStrip => Tabs.Count > 1;

    /// <summary>
    /// Raised once for every session as it is created, before it loads anything, so the
    /// window can connect the services only it can provide: file dialogs, settings, layout.
    /// </summary>
    public event Action<MainWindowViewModel>? SessionCreated;

    partial void OnActiveChanged(SaveTabViewModel? oldValue, SaveTabViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        if (newValue is not null)
            newValue.IsActive = true;
    }

    /// <summary>Adds an empty session and shows it. Used at startup and when the last tab closes.</summary>
    public SaveTabViewModel AddEmptyTab()
    {
        var tab = CreateTab();
        Tabs.Add(tab);
        Active = tab;
        return tab;
    }

    /// <summary>Opens a save in a new tab, reusing the active one if it is still empty.</summary>
    public bool Open(string path, out string error)
    {
        // An untouched empty tab is a placeholder, not a document worth keeping.
        if (Active is { Session.Save: null } empty)
        {
            if (!empty.Session.LoadSave(path, out error))
                return false;
            empty.Refresh();
            RefreshTransferTargets();
            return true;
        }

        var tab = CreateTab();
        if (!tab.Session.LoadSave(path, out error))
        {
            tab.Session.Dispose();
            return false;
        }
        tab.Refresh(); // the tab was built before the session had a save to describe
        Tabs.Add(tab);
        Active = tab;
        RefreshTransferTargets();
        return true;
    }

    /// <summary>Closes a tab, keeping at least one open so the window is never blank.</summary>
    public void Close(SaveTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.RemoveAt(index);
        tab.Session.Dispose();

        if (Tabs.Count == 0)
            AddEmptyTab();
        else if (ReferenceEquals(Active, tab))
            Active = Tabs[Math.Min(index, Tabs.Count - 1)];
        RefreshTransferTargets();
    }

    /// <summary>
    /// True when any open save has unwritten edits. Checking only the visible tab would
    /// let a background one be discarded in silence, which is the exact failure the
    /// close guard exists to prevent.
    /// </summary>
    public bool HasPendingWork => Tabs.Any(t => t.Session.HasPendingWork);

    /// <summary>The first tab with unsaved work, to bring forward before asking about it.</summary>
    public SaveTabViewModel? FirstPendingTab() => Tabs.FirstOrDefault(t => t.Session.HasPendingWork);

    /// <summary>Names the tabs with unsaved work when there are several, so the prompt is specific.</summary>
    public string DescribePendingTabs()
    {
        var names = Tabs.Where(t => t.Session.HasPendingWork).Select(t => t.Title).ToList();
        // A single dirty tab is the usual case, and the prompt's own wording covers it.
        return names.Count < 2
            ? string.Empty
            : $"Unsaved in {names.Count} open saves: {string.Join(", ", names)}.";
    }

    private SaveTabViewModel CreateTab()
    {
        var session = _createSession();
        session.OtherSaves = () => OtherSavesFor(session);
        SessionCreated?.Invoke(session);
        session.AttachTransfer();

        var tab = new SaveTabViewModel(session, this);
        // The tab follows its own session's state — not whichever tab happens to be
        // active when the change arrives, which is how a transfer used to leave the
        // receiving tab's dirty dot stale.
        session.SaveState.PropertyChanged += (_, _) => tab.Refresh();
        return tab;
    }

    /// <summary>Every open save except the one asking, as transfer destinations.</summary>
    private List<TransferTarget> OtherSavesFor(MainWindowViewModel asking)
    {
        var targets = new List<TransferTarget>();
        foreach (var tab in Tabs)
        {
            if (ReferenceEquals(tab.Session, asking) || tab.Session.Save is not { } sav)
                continue;
            var session = tab.Session;
            targets.Add(new TransferTarget($"{tab.Title} · {tab.Subtitle}", sav, session.NoteExternalChange));
        }
        return targets;
    }

    /// <summary>Tells every tab that the set of open saves changed.</summary>
    private void RefreshTransferTargets()
    {
        foreach (var tab in Tabs)
            tab.Session.RefreshTransferTargets();
    }

    public void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Session.Dispose();
    }
}
