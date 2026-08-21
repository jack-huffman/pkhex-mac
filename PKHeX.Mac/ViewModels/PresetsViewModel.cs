using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Saved stat spreads, applied to whichever Pokémon the inspector is showing.
/// </summary>
/// <remarks>
/// Applying writes into the inspector's working copy rather than the save, so it
/// behaves like any other edit: review it, then Apply Changes, or select something else
/// and lose it. Building a team of six that all want 252/252 is otherwise a lot of
/// identical typing.
/// </remarks>
public partial class PresetsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _persist;
    private readonly Func<PKM?> _target;
    private readonly Action _afterApply;

    public PresetsViewModel(AppSettings settings, Action persist, Func<PKM?> target, Action afterApply)
    {
        _settings = settings;
        _persist = persist;
        _target = target;
        _afterApply = afterApply;
        Reload();
    }

    public ObservableCollection<SpreadPreset> Presets { get; } = [];

    [ObservableProperty] private SpreadPreset? _selected;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public bool HasSelection => Selected is not null;

    partial void OnSelectedChanged(SpreadPreset? value) => OnPropertyChanged(nameof(HasSelection));

    private void Reload()
    {
        Presets.Clear();
        foreach (var preset in _settings.Presets)
            Presets.Add(preset);
        Selected = Presets.FirstOrDefault();
    }

    [RelayCommand]
    public void Apply()
    {
        if (Selected is not { } preset)
        {
            Status = "Pick a preset first.";
            return;
        }
        if (_target() is not { } pk)
        {
            Status = "Select a Pokémon first.";
            return;
        }

        var changed = preset.ApplyTo(pk);
        Status = changed.Count == 0
            ? $"Already matches {preset.Name}."
            : $"Applied {preset.Name} — {string.Join(", ", changed)} changed. Apply Changes to keep it.";
        if (changed.Count > 0)
            _afterApply();
    }

    /// <summary>Captures the inspector's current values as a new preset.</summary>
    [RelayCommand]
    public void SaveCurrent()
    {
        if (_target() is not { } pk)
        {
            Status = "Select a Pokémon to capture.";
            return;
        }
        var name = NewName.Trim();
        if (name.Length == 0)
        {
            Status = "Give the preset a name first.";
            return;
        }

        // Same name replaces, so refining a spread does not leave duplicates behind.
        var existing = _settings.Presets.FindIndex(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var preset = SpreadPreset.From(pk, name);
        if (existing >= 0)
            _settings.Presets[existing] = preset;
        else
            _settings.Presets.Add(preset);

        _persist();
        Reload();
        Selected = Presets.FirstOrDefault(p => p.Name == name);
        NewName = string.Empty;
        Status = existing >= 0 ? $"Replaced {name}." : $"Saved {name}.";
    }

    [RelayCommand]
    public void Delete()
    {
        if (Selected is not { } preset)
            return;
        _settings.Presets.RemoveAll(p => ReferenceEquals(p, preset) || p.Name == preset.Name);
        _persist();
        Reload();
        Status = $"Deleted {preset.Name}.";
    }
}
