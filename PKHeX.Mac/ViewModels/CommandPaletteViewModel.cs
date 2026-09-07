using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Type-to-go navigation over every destination in the app.
/// </summary>
/// <remarks>
/// There are roughly thirty-five panes behind nine sidebar entries and their tabs, and
/// nothing announced they existed: none of the keyboard shortcuts navigated, and a pane
/// like the badge editor was three clicks deep with no way to search for it.
/// </remarks>
public partial class CommandPaletteViewModel : ObservableObject
{
    /// <summary>How many recently used entries lead the list when nothing has been typed.</summary>
    private const int RecentEntries = 5;

    private readonly List<PaletteEntry> _entries = [];
    private readonly List<string> _recent = [];

    public ObservableCollection<PaletteEntry> Results { get; } = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _selectedIndex;

    public bool HasResults => Results.Count > 0;

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>Replaces the command set, typically when a save is opened or closed.</summary>
    public void SetEntries(IEnumerable<PaletteEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        if (IsOpen)
            Refresh();
    }

    [RelayCommand]
    public void Open()
    {
        Query = string.Empty;
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
            return;
        // Wrap, so holding the key cycles rather than sticking at the ends.
        SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
    }

    /// <summary>Runs the highlighted entry.</summary>
    public void Activate()
    {
        if ((uint)SelectedIndex >= (uint)Results.Count)
            return;
        var entry = Results[SelectedIndex];
        IsOpen = false;

        _recent.Remove(entry.Title);
        _recent.Insert(0, entry.Title);
        while (_recent.Count > RecentEntries)
            _recent.RemoveAt(_recent.Count - 1);

        entry.Run();
    }

    private void Refresh()
    {
        Results.Clear();
        var matches = string.IsNullOrWhiteSpace(Query)
            ? Ordered()
            : CommandSearch.Rank(_entries, Query, e => e.Title, e => e.Group);
        foreach (var entry in matches)
            Results.Add(entry);
        SelectedIndex = 0;
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>With no query, lead with what was used recently.</summary>
    private List<PaletteEntry> Ordered()
    {
        var result = new List<PaletteEntry>(_entries.Count);
        foreach (var title in _recent)
        {
            var match = _entries.Find(e => e.Title == title);
            if (match is not null)
                result.Add(match);
        }
        foreach (var entry in _entries)
        {
            if (!result.Contains(entry))
                result.Add(entry);
        }
        return result;
    }
}

/// <summary>One destination or action the palette can run.</summary>
public sealed class PaletteEntry(string title, string group, Action run)
{
    public string Title { get; } = title;

    /// <summary>Where it lives, shown on the right of the row and also searchable.</summary>
    public string Group { get; } = group;

    public void Run() => run();
}
