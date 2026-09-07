using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Sends the selected Pokémon to another open save, converting it on the way.
/// </summary>
/// <remarks>
/// The deliberate version of a trade. It shows what the conversion produces and whether
/// the result is legal in the destination game <em>before</em> anything is written, and
/// copies rather than moves so a failed idea costs nothing.
/// </remarks>
public partial class TransferViewModel : ObservableObject
{
    private readonly GameStrings _strings;
    private readonly Func<PKM?> _selected;
    private readonly Func<IReadOnlyList<TransferTarget>> _targets;
    private readonly Action<string> _onTransferred;

    public TransferViewModel(GameStrings strings, Func<PKM?> selected,
                            Func<IReadOnlyList<TransferTarget>> targets, Action<string> onTransferred)
    {
        _strings = strings;
        _selected = selected;
        _targets = targets;
        _onTransferred = onTransferred;
    }

    public ObservableCollection<TransferTarget> Targets { get; } = [];

    [ObservableProperty] private TransferTarget? _target;
    [ObservableProperty] private int _destinationBox;
    [ObservableProperty] private bool _assignHomeTracker = true;

    [ObservableProperty] private string _sourceName = string.Empty;
    [ObservableProperty] private Bitmap? _sourceSprite;
    [ObservableProperty] private string _verdict = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private bool _canSend;
    [ObservableProperty] private bool _isLegal;
    [ObservableProperty] private bool _needsTracker;
    [ObservableProperty] private string _status = string.Empty;

    public bool HasTargets => Targets.Count > 0;

    partial void OnTargetChanged(TransferTarget? value) => Refresh();
    partial void OnAssignHomeTrackerChanged(bool value) => Refresh();

    /// <summary>Rebuilds the destination list and re-plans. Called when the view opens.</summary>
    public void Reload()
    {
        var previous = Target?.Name;
        Targets.Clear();
        foreach (var target in _targets())
            Targets.Add(target);
        OnPropertyChanged(nameof(HasTargets));

        Target = Targets.FirstOrDefault(t => t.Name == previous) ?? Targets.FirstOrDefault();
        Refresh();
    }

    /// <summary>Re-evaluates without writing anything.</summary>
    public void Refresh()
    {
        var pk = _selected();
        if (pk is null || pk.Species == 0)
        {
            SourceName = string.Empty;
            SourceSprite = null;
            Verdict = "Select a Pokémon in the boxes to send.";
            Detail = string.Empty;
            CanSend = false;
            IsLegal = false;
            NeedsTracker = false;
            return;
        }

        SourceName = _strings.SpeciesName(pk);
        SourceSprite = SpriteService.GetPokemonSprite(pk);

        if (Target is not { } target)
        {
            Verdict = "Open another save to send it to.";
            Detail = string.Empty;
            CanSend = false;
            return;
        }

        var plan = SaveTransfer.Plan(pk, target.Save, _strings, AssignHomeTracker);
        CanSend = plan.CanTransfer;
        IsLegal = plan.IsLegal;
        NeedsTracker = plan.NeedsHomeTracker;

        if (!plan.CanTransfer)
        {
            Verdict = "Cannot be sent";
            Detail = plan.Problem ?? string.Empty;
            return;
        }

        Verdict = plan.IsLegal
            ? $"{SourceName} arrives legal in {target.Name}"
            : $"{SourceName} arrives with a legality problem";
        Detail = plan.IsLegal
            ? (plan.HomeTrackerAssigned
                ? "A Pokémon HOME tracker was generated, which is what makes it legal there."
                : "Nothing else needs changing.")
            : plan.Issue;
    }

    [RelayCommand]
    public void Send()
    {
        if (_selected() is not { } pk || Target is not { } target)
            return;

        var plan = SaveTransfer.Plan(pk, target.Save, _strings, AssignHomeTracker);
        if (!SaveTransfer.Commit(plan, target.Save, DestinationBox, out var message))
        {
            Status = message;
            return;
        }
        Status = message + (plan.IsLegal ? string.Empty : " It is not legal there.");
        // The receiving save now has unsaved work of its own.
        target.NoteChanged($"Received {plan.Species} from another save");
        _onTransferred(message);
    }
}

/// <summary>Another open save, as a transfer destination.</summary>
public sealed record TransferTarget(string Name, SaveFile Save, Action<string> NoteChanged)
{
    public override string ToString() => Name;
}
