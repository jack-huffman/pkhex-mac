using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>The box and party grids, and every operation on a slot.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Slots per row in the box grid; the party is one row of six.</summary>
    private const int GridColumns = 6;

    public ObservableCollection<SlotViewModel> BoxSlots { get; } = [];
    public ObservableCollection<SlotViewModel> PartySlots { get; } = [];
    public ObservableCollection<string> BoxNames { get; } = [];

    /// <summary>
    /// The ride legendary, presented as an ordinary slot beneath the boxes.
    /// </summary>
    /// <remarks>
    /// It is universal to Scarlet and Violet and always present once obtained, so it
    /// belongs on the storage screen rather than behind a tab. Treating it as a slot
    /// rather than a card means clicking it loads the inspector and Apply writes it
    /// back, with no copying in and out.
    /// </remarks>
    public ObservableCollection<SlotViewModel> RideSlots { get; } = [];

    [ObservableProperty] private bool _hasRideSlot;
    [ObservableProperty] private string _rideLabel = string.Empty;
    [ObservableProperty] private int _currentBox;
    [ObservableProperty] private string _currentBoxName = string.Empty;
    [ObservableProperty] private bool _hasParty;
    [ObservableProperty] private string _targetSlotText = "Add to first empty slot";

    private SlotViewModel? _selected;

    /// <summary>Box the current selection lives in; -1 for the party or no selection.</summary>
    private int _selectedBox = -1;

    public SlotViewModel? SelectedSlot => _selected;

    public bool CanPaste => _clipboardPk is not null;

    // ---- Layout ----

    /// <summary>
    /// Works out whether the insights panel fits beneath the grid, given the space the
    /// boxes column actually has.
    /// </summary>
    /// <remarks>
    /// Slot size is deliberately fixed. The pixel sprites are authored at exactly 68x56,
    /// which is the sprite box in a 94x82 slot, so they draw 1:1 and stay crisp. Growing
    /// the slot would either upscale that art with visible blockiness or leave it at
    /// native size while the Gen 9 species — which only exist as 512x512 renders —
    /// filled the larger box, so the two families would disagree in size. The art
    /// decides the slot, not the window.
    /// </remarks>
    public void UpdateBoxLayout(double contentWidth, double contentHeight)
    {
        if (contentHeight <= 0)
            return;
        // Party card, box header, padding, status line, and the five rows of slots.
        const double slotHeight = 82;
        var used = 150 + (HasParty ? slotHeight + 44 : 0) + (slotHeight * 5) + 60;
        BoxInsights.HasRoom = contentHeight - used >= 170;
    }

    // ---- Loading the grids from the save ----

    private void RebuildBoxSlots()
    {
        if (_sav is null)
            return;
        var count = _sav.HasBox ? _sav.BoxSlotCount : 0;
        if (BoxSlots.Count == count)
            return;
        BoxSlots.Clear();
        for (int i = 0; i < count; i++)
            BoxSlots.Add(new SlotViewModel(0, i));
    }

    partial void OnCurrentBoxChanged(int value)
    {
        if (_sav is null || !_sav.HasBox || (uint)value >= _sav.BoxCount)
            return;
        CurrentBoxName = (uint)value < BoxNames.Count ? BoxNames[value] : $"Box {value + 1}";
        LoadBox(value);
        CurrentView = "boxes"; // clicking a box in the sidebar returns to the box view
        if (Settings is { } settings)
        {
            settings.LastBox = value;
            SettingsChanged?.Invoke();
        }
    }

    private void LoadBox(int box)
    {
        if (_sav is null || !_sav.HasBox)
            return;
        for (int i = 0; i < BoxSlots.Count; i++)
        {
            var pk = _sav.GetBoxSlotAtIndex(box, i);
            BoxSlots[i].Box = box;
            BoxSlots[i].Update(pk, _strings);
        }
        _ = BoxInsights.RefreshAsync(_sav, box, _strings);
        RefreshSelectionHighlight(box);
    }

    /// <summary>
    /// Shows the selection ring only while its own box is on screen. Switching away
    /// hides it; coming back restores it, because the selection itself is untouched.
    /// </summary>
    private void RefreshSelectionHighlight(int box)
    {
        var selectedSlot = _selected is { IsParty: false } && _selectedBox == box ? _selected.Slot : -1;
        for (int i = 0; i < BoxSlots.Count; i++)
            BoxSlots[i].IsSelected = i == selectedSlot;
        // The ride is not in any box, so a box change never owns its highlight.
        foreach (var ride in RideSlots)
            ride.IsSelected = ReferenceEquals(_selected, ride);
    }

    private void LoadParty()
    {
        if (_sav is null || !_sav.HasParty)
            return;
        for (int i = 0; i < PartySize; i++)
        {
            var pk = i < _sav.PartyCount ? _sav.GetPartySlotAtIndex(i) : null;
            PartySlots[i].Update(pk?.Species > 0 ? pk : null, _strings);
        }
    }

    private void LoadRideSlot()
    {
        RideSlots.Clear();
        HasRideSlot = _sav is not null && RideLegendary.IsSupported(_sav);
        if (!HasRideSlot || _sav is null)
            return;

        RideLabel = _sav.Version switch
        {
            GameVersion.SL => "KORAIDON · YOUR RIDE",
            GameVersion.VL => "MIRAIDON · YOUR RIDE",
            _ => "YOUR RIDE",
        };
        // Box index one past the last reachable box marks it as the reserved slot.
        var slot = new SlotViewModel(_sav.BoxCount, 0);
        slot.Update(RideLegendary.Read(_sav), _strings);
        RideSlots.Add(slot);
    }

    /// <summary>Re-reads the reserved slot without rebuilding it, so selection survives.</summary>
    private void RefreshRideSlot()
    {
        if (_sav is null || RideSlots.Count == 0)
            return;
        RideSlots[0].Update(RideLegendary.Read(_sav), _strings);
    }

    private void RefreshSlotViews()
    {
        LoadBox(CurrentBox);
        LoadParty();
        RefreshRideSlot();
    }

    // ---- Reading and writing slots ----

    /// <summary>Reads the current contents of a slot from the save.</summary>
    private PKM? ReadSlot(SlotViewModel slot)
    {
        if (_sav is null)
            return null;
        if (slot.IsParty)
            return slot.Slot < _sav.PartyCount ? _sav.GetPartySlotAtIndex(slot.Slot) : null;
        // The ride legendary sits one box past the last the player can open, so the
        // normal box accessor cannot reach it.
        if (IsRideSlot(slot))
            return RideLegendary.Read(_sav);
        return _sav.GetBoxSlotAtIndex(slot.Box, slot.Slot);
    }

    /// <summary>Writes a PKM into a slot (party writes are compacted).</summary>
    private void WriteSlot(SlotViewModel slot, PKM pk)
    {
        if (_sav is null)
            return;
        pk.RefreshChecksum();
        if (slot.IsParty)
            _sav.SetPartySlotAtIndex(pk, Math.Min(slot.Slot, _sav.PartyCount));
        else if (IsRideSlot(slot))
            RideLegendary.Write(_sav, pk);
        else
            _sav.SetBoxSlotAtIndex(pk, slot.Box, slot.Slot);
    }

    /// <summary>True for the reserved slot holding the ride legendary.</summary>
    private bool IsRideSlot(SlotViewModel slot) =>
        _sav is not null && !slot.IsParty && slot.Box == _sav.BoxCount;

    // ---- Selection ----

    [RelayCommand]
    public void SelectSlot(SlotViewModel? slot)
    {
        if (_selected is not null)
            _selected.IsSelected = false;
        _selected = slot;
        _selectedBox = slot is null || slot.IsParty ? -1 : slot.Box;
        if (slot is not null)
            slot.IsSelected = true;
        Detail.Load(slot?.Pokemon);
        Transfer?.Refresh();
        RefreshTargetSlotText();
        if (IsDatabaseView && !CanUseDatabases)
            CurrentView = "boxes"; // the chosen slot is no longer empty
    }

    /// <summary>Describes where the next "add" will land, for the button label.</summary>
    public void RefreshTargetSlotText()
    {
        TargetSlotText = _selected is { IsParty: false } s
            ? $"Add to {CurrentBoxName}, slot {s.Slot + 1}"
            : "Add to first empty slot";
        OnPropertyChanged(nameof(CanUseDatabases));
    }

    /// <summary>
    /// Moves the selection by a step within whichever grid holds it.
    /// </summary>
    /// <remarks>
    /// The party is one row of six and a box is six across, so a vertical step is a
    /// row's width in the box and does nothing in the party. Moving off the left or
    /// right edge of a box carries on into the neighbouring box, which is how the
    /// grids are read anyway.
    /// </remarks>
    public void MoveSelection(int columns, int rows)
    {
        if (_sav is null || _selected is null)
            return;

        if (_selected.IsParty)
        {
            if (rows != 0)
                return;
            var next = Math.Clamp(_selected.Slot + columns, 0, PartySlots.Count - 1);
            SelectSlot(PartySlots[next]);
            return;
        }
        if (IsRideSlot(_selected))
            return;

        var index = _selected.Slot + columns + (rows * GridColumns);
        if (index < 0)
        {
            // Off the top or the left: step back a box and land on the mirror slot.
            if (CurrentBox == 0)
                return;
            CurrentBox--;
            index += BoxSlots.Count;
        }
        else if (index >= BoxSlots.Count)
        {
            if (CurrentBox >= _sav.BoxCount - 1)
                return;
            CurrentBox++;
            index -= BoxSlots.Count;
        }
        SelectSlot(BoxSlots[Math.Clamp(index, 0, BoxSlots.Count - 1)]);
    }

    // ---- Slot operations ----

    private void ApplyDetailChanges()
    {
        if (_sav is null || _selected is null || Detail.Pokemon is not { } pk)
            return;
        WriteSlot(_selected, pk);
        RefreshSlotViews();
        Detail.Load(pk);
        NoteChange($"Applied changes to {Detail.SpeciesName}");
    }

    public void DeleteSlot(SlotViewModel slot)
    {
        if (_sav is null)
            return;
        if (slot.IsParty)
        {
            if (slot.Slot >= _sav.PartyCount)
                return;
            // Compact the party: shift later members up, blank the last.
            for (int i = slot.Slot; i < _sav.PartyCount - 1; i++)
            {
                var next = _sav.GetPartySlotAtIndex(i + 1);
                next.RefreshChecksum();
                _sav.SetPartySlotAtIndex(next, i);
            }
            _sav.SetPartySlotAtIndex(_sav.BlankPKM, _sav.PartyCount - 1);
        }
        else
        {
            WriteSlot(slot, _sav.BlankPKM);
        }
        RefreshSlotViews();
        if (_selected == slot)
            Detail.Load(null);
        NoteChange("Slot cleared.");
    }

    public void CopySlot(SlotViewModel slot)
    {
        var pk = ReadSlot(slot);
        if (pk is null || pk.Species == 0)
            return;
        _clipboardPk = pk.Clone();
        OnPropertyChanged(nameof(CanPaste));
        StatusText = $"Copied {_strings.SpeciesName(pk)}.";
    }

    public void PasteSlot(SlotViewModel slot)
    {
        if (_sav is null || _clipboardPk is null)
            return;
        var pasted = _clipboardPk.Clone();
        WriteSlot(slot, pasted);
        RefreshSlotViews();
        NoteChange($"Pasted {_strings.SpeciesName(pasted)}.");
    }

    /// <summary>Moves (or swaps) the contents of two slots. Used by drag-and-drop.</summary>
    public void MoveOrSwapSlot(SlotViewModel from, SlotViewModel to)
    {
        if (_sav is null || from == to)
            return;
        var pkFrom = ReadSlot(from);
        if (pkFrom is null || pkFrom.Species == 0)
            return;
        var pkTo = ReadSlot(to);

        if (from.IsParty && !to.IsParty && _sav.PartyCount <= 1 && (pkTo is null || pkTo.Species == 0))
        {
            StatusText = "Cannot remove the last party member.";
            return;
        }

        if (pkTo is not null && pkTo.Species != 0)
        {
            WriteSlot(from, pkTo);
            WriteSlot(to, pkFrom);
        }
        else
        {
            WriteSlot(to, pkFrom);
            if (from.IsParty)
                DeleteSlot(from);
            else
                WriteSlot(from, _sav.BlankPKM);
        }
        RefreshSlotViews();
        SelectSlot(to.IsParty ? PartySlots[to.Slot] : BoxSlots[to.Slot]);
        NoteChange("Moved.");
    }

    /// <summary>
    /// Moves the dragged Pokémon into the first free slot of another box. Used when
    /// a slot is dropped onto a box name in the sidebar.
    /// </summary>
    public void MoveSlotToBox(SlotViewModel from, int targetBox)
    {
        if (_sav is null || !_sav.HasBox || (uint)targetBox >= _sav.BoxCount)
            return;
        var pk = ReadSlot(from);
        if (pk is null || pk.Species == 0)
            return;
        if (!from.IsParty && targetBox == from.Box)
            return; // same box: the grid drag already handles this

        var boxName = (uint)targetBox < BoxNames.Count ? BoxNames[targetBox] : $"Box {targetBox + 1}";
        var empty = _sav.FindFirstEmptySlot(targetBox);
        if (empty < 0)
        {
            StatusText = $"{boxName} is full.";
            return;
        }

        var moved = pk.Clone();
        moved.RefreshChecksum();
        _sav.SetBoxSlotAtIndex(moved, targetBox, empty);
        if (from.IsParty)
            DeleteSlot(from);
        else
            WriteSlot(from, _sav.BlankPKM);
        RefreshSlotViews();
        NoteChange($"Moved {_strings.SpeciesName(moved)} to {boxName}, slot {empty + 1}.");
    }

    /// <summary>Adds a Pokémon into the first empty slot of the current box.</summary>
    public bool TryAddToCurrentBox(PKM pk, out string message)
    {
        if (_sav is null || !_sav.HasBox)
        {
            message = "No save loaded.";
            return false;
        }
        var empty = _sav.FindFirstEmptySlot(CurrentBox);
        if (empty < 0)
        {
            message = $"{CurrentBoxName} is full — clear a slot or switch boxes.";
            return false;
        }
        pk.RefreshChecksum();
        _sav.SetBoxSlotAtIndex(pk, CurrentBox, empty);
        RefreshSlotViews();
        SelectSlot(BoxSlots[empty]);
        message = $"Added {_strings.SpeciesName(pk)} to {CurrentBoxName}, slot {empty + 1}.";
        NoteChange(message);
        return true;
    }

    /// <summary>
    /// Writes the previewed entity into the slot the user selected, or the first
    /// empty slot in the current box when nothing is selected.
    /// </summary>
    [RelayCommand]
    private void AddPreviewToBox()
    {
        if (_sav is null)
            return;
        if (Preview.Current is not { } pk)
        {
            StatusText = "Nothing to add — pick an entry first.";
            return;
        }

        var clone = pk.Clone();
        if (_selected is { IsParty: false } slot)
        {
            WriteSlot(slot, clone);
            RefreshSlotViews();
            NoteChange($"Placed {_strings.SpeciesName(clone)} in {CurrentBoxName}, slot {slot.Slot + 1}");
            SelectSlot(BoxSlots[slot.Slot]); // reselect so the editor shows what landed
            return;
        }
        if (!TryAddToCurrentBox(clone, out var message))
            StatusText = message;
        RefreshTargetSlotText();
    }

    // ---- Search results ----

    /// <summary>Reveals a search hit that lives in this save.</summary>
    [RelayCommand]
    private void GoToSearchResult(SearchResultViewModel? result)
    {
        if (result is null || result.IsFromFile || _sav is null)
            return;
        var c = result.Candidate;
        CurrentView = "boxes";
        if (c.Box >= 0)
        {
            CurrentBox = c.Box;
            if ((uint)c.Slot < BoxSlots.Count)
                SelectSlot(BoxSlots[c.Slot]);
        }
        else if ((uint)c.Slot < PartySlots.Count)
        {
            SelectSlot(PartySlots[c.Slot]);
        }
    }

    /// <summary>Copies a search hit found on disk into the current box.</summary>
    [RelayCommand]
    private void ImportSearchResult(SearchResultViewModel? result)
    {
        if (result is null || _sav is null)
            return;
        var pk = EntityFiles.ConvertFor(result.Candidate.Entity, _sav, out var error);
        if (pk is null)
        {
            StatusText = $"Cannot bring that Pokémon into this save. {error}";
            return;
        }
        if (!TryAddToCurrentBox(pk.Clone(), out var message))
            StatusText = message;
    }

    // ---- Box tools ----

    /// <summary>Sorts the current box by species, then form, with empties last — PKHeX's own default order.</summary>
    [RelayCommand]
    private void SortCurrentBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        _sav.SortBoxes(CurrentBox, CurrentBox);
        RefreshSlotViews();
        NoteChange($"Sorted {CurrentBoxName} by species.");
    }

    [RelayCommand]
    private void ClearCurrentBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        _sav.ClearBoxes(CurrentBox, CurrentBox);
        RefreshSlotViews();
        Detail.Load(null);
        NoteChange($"Cleared {CurrentBoxName}.");
    }

    [RelayCommand]
    private void PreviousBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        CurrentBox = (CurrentBox - 1 + _sav.BoxCount) % _sav.BoxCount;
    }

    [RelayCommand]
    private void NextBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        CurrentBox = (CurrentBox + 1) % _sav.BoxCount;
    }
}
