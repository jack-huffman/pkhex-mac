using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The three scopes of undo — the whole save, one slot, or inspector edits not yet
/// applied — and the prompts that guard them.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Shown when closing would discard unsaved edits.</summary>
    [ObservableProperty] private bool _isClosePromptOpen;

    /// <summary>Shown when the user asks to throw away every edit and reload the file.</summary>
    [ObservableProperty] private bool _isRevertPromptOpen;

    /// <summary>Extra line in the close prompt, for inspector edits not yet applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnappliedDetail))]
    private string _closePromptNote = string.Empty;

    public bool HasUnappliedDetail => ClosePromptNote.Length > 0;

    /// <summary>Reverting needs a file to go back to.</summary>
    public bool CanRevert => HasSave && !string.IsNullOrEmpty(_savPath) && File.Exists(_savPath);

    /// <summary>What the revert prompt says will be thrown away.</summary>
    public string RevertSummary => SaveState.HasUnsavedChanges
        ? $"{SaveState.CountText} will be discarded."
        : "There are no unsaved changes; this simply re-reads the file.";

    /// <summary>Opens the confirmation, or explains why reverting is not possible.</summary>
    [RelayCommand]
    public void RequestRevert()
    {
        if (!CanRevert)
        {
            StatusText = HasSave
                ? "The file this save came from can no longer be found."
                : "Open a save file first (⌘O).";
            return;
        }
        OnPropertyChanged(nameof(RevertSummary));
        IsRevertPromptOpen = true;
    }

    /// <summary>
    /// Throws away every in-memory edit by reloading the file from disk. The file is
    /// only ever read here, so a revert cannot damage it.
    /// </summary>
    [RelayCommand]
    private void Revert()
    {
        IsRevertPromptOpen = false;
        if (_savPath is not { } path || !File.Exists(path))
        {
            StatusText = "The file this save came from can no longer be found.";
            return;
        }

        var discarded = SaveState.PendingChanges;
        if (!LoadSave(path, out var error))
        {
            StatusText = error.Replace('\n', ' ');
            return;
        }
        StatusText = discarded == 0
            ? $"Reloaded {Path.GetFileName(path)} from disk."
            : $"Reverted to {Path.GetFileName(path)} — {discarded} change{(discarded == 1 ? string.Empty : "s")} discarded.";
    }

    [RelayCommand]
    private void CancelRevert() => IsRevertPromptOpen = false;

    [RelayCommand]
    private void CancelClose() => IsClosePromptOpen = false;

    /// <summary>
    /// Restores one slot to what the file holds, whether or not the edit was applied.
    /// Works for both directions: a Pokémon that was changed goes back, and one that
    /// was added to an empty slot is removed again.
    /// </summary>
    public void RevertSlot(SlotViewModel? slot)
    {
        if (_sav is null || _pristine is null || slot is null)
        {
            StatusText = "Nothing to revert to — reopen the save file first.";
            return;
        }

        PKM original;
        try
        {
            if (IsRideSlot(slot))
            {
                // The reserved slot is past the last box, so read it the same way we
                // read the live one.
                original = RideLegendary.Read(_pristine) ?? _pristine.BlankPKM;
            }
            else if (!slot.IsParty)
            {
                original = _pristine.GetBoxSlotAtIndex(slot.Box, slot.Slot);
            }
            else if (slot.Slot < _pristine.PartyCount)
            {
                original = _pristine.GetPartySlotAtIndex(slot.Slot);
            }
            else
            {
                // The party was shorter on disk, so this slot held nothing. Clamping to
                // the last real member would restore the wrong Pokémon.
                original = _pristine.BlankPKM;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            StatusText = "That slot does not exist in the file on disk.";
            return;
        }

        var current = ReadSlot(slot);
        if (current is not null && current.Data.SequenceEqual(original.Data))
        {
            StatusText = "That slot already matches the file on disk.";
            return;
        }

        WriteSlot(slot, original);
        RefreshSlotViews();
        SelectSlot(slot);
        var name = original.Species == 0 ? "an empty slot" : _strings.SpeciesName(original);
        var where = slot.IsParty
            ? $"party slot {slot.Slot + 1}"
            : $"{CurrentBoxName}, slot {slot.Slot + 1}";
        NoteChange($"Reverted {where} to {name} as stored on disk");
    }

    /// <summary>
    /// Discards edits typed into the inspector by re-reading the selected slot. The
    /// save is untouched, because unapplied inspector edits never reached it.
    /// </summary>
    private void RevertDetailEdits()
    {
        if (_selected is null)
        {
            StatusText = "Select a Pokémon first.";
            return;
        }
        if (!Detail.IsDirty)
        {
            StatusText = "There are no unapplied edits to discard.";
            return;
        }
        RefreshSlotViews();          // re-read the slot from the save
        Detail.Load(_selected.Pokemon);
        StatusText = "Discarded the unapplied edits.";
    }
}
