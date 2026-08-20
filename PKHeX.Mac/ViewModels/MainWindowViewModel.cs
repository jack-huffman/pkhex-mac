using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private SaveFile? _sav;
    private string? _savPath;
    private PKM? _clipboardPk;

    public MainWindowViewModel()
    {
        Detail = new PokemonDetailViewModel(_strings);
        Preview = new PokemonPreviewViewModel(_strings);
        for (int i = 0; i < 30; i++)
            BoxSlots.Add(new SlotViewModel(0, i));
        for (int i = 0; i < 6; i++)
            PartySlots.Add(new SlotViewModel(-1, i));
    }

    public PokemonDetailViewModel Detail { get; }
    public PokemonPreviewViewModel Preview { get; }

    // ---- In-window views (sidebar navigation) ----

    [ObservableProperty] private string _currentView = "boxes";
    [ObservableProperty] private TrainerEditorViewModel? _trainer;
    [ObservableProperty] private BagViewModel? _bag;
    [ObservableProperty] private AddPokemonViewModel? _addDb;
    [ObservableProperty] private GiftsViewModel? _giftDb;

    public bool IsBoxesView => CurrentView == "boxes";
    public bool IsSaveView => CurrentView == "save";
    public bool IsAddView => CurrentView == "add";
    public bool IsGiftsView => CurrentView == "gifts";
    public bool IsDatabaseView => IsAddView || IsGiftsView;

    /// <summary>Collapses the inspector column for the full-width Save view.</summary>
    public Avalonia.Controls.GridLength InspectorWidth =>
        IsSaveView ? new Avalonia.Controls.GridLength(0) : new Avalonia.Controls.GridLength(392);

    partial void OnCurrentViewChanged(string value)
    {
        OnPropertyChanged(nameof(IsBoxesView));
        OnPropertyChanged(nameof(IsSaveView));
        OnPropertyChanged(nameof(IsAddView));
        OnPropertyChanged(nameof(IsGiftsView));
        OnPropertyChanged(nameof(IsDatabaseView));
        OnPropertyChanged(nameof(InspectorWidth));
    }

    [RelayCommand]
    public void SetView(string view)
    {
        if (_sav is null && view != "boxes")
        {
            StatusText = "Open a save file first (⌘O).";
            return;
        }
        if (view == "gifts" && GiftDb is null && _sav is not null)
        {
            StatusText = "Loading the Mystery Gift archive…";
            GiftDb = new GiftsViewModel(_sav, _strings)
            {
                PreviewReady = pk => Preview.Load(pk),
                Blocked = reason => Preview.ShowBlocked(reason),
            };
        }
        Preview.Load(null);
        CurrentView = view;
    }

    /// <summary>Applies trainer identity and bag edits together, then returns to the boxes.</summary>
    public void ApplySave()
    {
        Trainer?.Apply();
        Bag?.Apply();
        RefreshTrainerCard();
        StatusText = "Trainer info and bag updated. Remember to export the save (⌘S).";
        CurrentView = "boxes";
    }

    /// <summary>Discards unapplied trainer/bag edits by rebuilding both editors from the save.</summary>
    public void ResetSave()
    {
        if (_sav is null)
            return;
        Trainer = new TrainerEditorViewModel(_sav);
        Bag = new BagViewModel(_sav, _strings);
        StatusText = "Reverted unsaved trainer and bag changes.";
    }

    public void AddPreviewToBox()
    {
        if (Preview.Current is not { } pk)
        {
            StatusText = "Nothing to add — pick an entry first.";
            return;
        }
        if (TryAddToCurrentBox(pk.Clone(), out var message))
            CurrentView = "boxes";
        else
            StatusText = message;
    }

    public ObservableCollection<SlotViewModel> BoxSlots { get; } = [];
    public ObservableCollection<SlotViewModel> PartySlots { get; } = [];
    public ObservableCollection<string> BoxNames { get; } = [];

    [ObservableProperty] private bool _hasSave;
    [ObservableProperty] private string _windowTitle = "PKHeX for Mac";
    [ObservableProperty] private string _statusText = "Open a save file to begin  (⌘O)";

    // Trainer card
    [ObservableProperty] private string _trainerName = string.Empty;
    [ObservableProperty] private string _gameName = string.Empty;
    [ObservableProperty] private string _trainerIds = string.Empty;
    [ObservableProperty] private string _playTime = string.Empty;
    [ObservableProperty] private string _generationText = string.Empty;

    [ObservableProperty] private int _currentBox;
    [ObservableProperty] private string _currentBoxName = string.Empty;
    [ObservableProperty] private bool _hasParty;

    // Update banner
    [ObservableProperty] private bool _showUpdateBanner;
    [ObservableProperty] private string _updateBannerText = string.Empty;

    private SlotViewModel? _selected;

    public SaveFile? SAV => _sav;
    public string? SavePath => _savPath;
    public SlotViewModel? SelectedSlot => _selected;

    // =====================================================================
    // Update check
    // =====================================================================

    public async Task CheckForUpstreamUpdateAsync()
    {
        var info = await UpdateCheckService.CheckAsync();
        if (info is { IsBehind: true })
        {
            UpdateBannerText = $"PKHeX {info.RemoteTag} was released {info.RemoteDate:MMM d, yyyy} — this app's engine is {info.LocalVersion}. " +
                               "Run scripts/update-upstream.sh and rebuild to catch up.";
            ShowUpdateBanner = true;
        }
    }

    [RelayCommand]
    public void DismissUpdateBanner() => ShowUpdateBanner = false;

    // =====================================================================
    // Save load / export
    // =====================================================================

    public bool LoadSave(string path, out string error)
    {
        error = string.Empty;
        try
        {
            if (!SaveUtil.TryGetSaveFile(path, out var sav))
            {
                error = "This file is not a recognized Pokémon save file.\nMake sure it is decrypted (exported with Checkpoint, JKSM, or a save manager).";
                return false;
            }

            _sav = sav;
            _savPath = path;
            sav.Metadata.SetExtraInfo(path);
            GameInfo.FilteredSources = new FilteredGameDataSource(sav, GameInfo.Sources);
            Detail.SetContext(sav, GameInfo.FilteredSources);
            HasSave = true;
            HasParty = sav.HasParty;

            TrainerName = sav.OT;
            GameName = GameInfo.GetVersionName(sav.Version);
            TrainerIds = $"TID {sav.DisplayTID:D6} · SID {sav.DisplaySID:D4}";
            PlayTime = sav.PlayTimeString;
            GenerationText = $"Generation {sav.Generation}";
            WindowTitle = $"PKHeX for Mac — {Path.GetFileName(path)} ({GameName})";
            StatusText = $"Loaded {Path.GetFileName(path)} · {GameName} · OT: {sav.OT}";

            BoxNames.Clear();
            if (sav.HasBox)
            {
                foreach (var n in BoxUtil.GetBoxNames(sav))
                    BoxNames.Add(n);
            }

            RebuildBoxSlots();
            CurrentBox = 0;
            CurrentBoxName = BoxNames.Count > 0 ? BoxNames[0] : string.Empty;
            LoadBox(0);
            LoadParty();
            SelectSlot(null);

            // In-window editor views for this save.
            Trainer = new TrainerEditorViewModel(sav);
            Bag = new BagViewModel(sav, _strings);
            AddDb = new AddPokemonViewModel(sav, GameInfo.FilteredSources, _strings);
            AddDb.PreviewReady = pk => Preview.Load(pk);
            // The gift archive is ~2.6k entries with sprites; build it on first open
            // so loading a save stays instant.
            GiftDb = null;
            Preview.Load(null);
            CurrentView = "boxes";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load save file:\n{ex.Message}";
            return false;
        }
    }

    public bool ExportSave(string path, out string error)
    {
        error = string.Empty;
        if (_sav is null)
        {
            error = "No save file loaded.";
            return false;
        }
        try
        {
            var data = _sav.Write();
            File.WriteAllBytes(path, data.ToArray());
            _savPath = path;
            StatusText = $"Saved to {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to write save file:\n{ex.Message}";
            return false;
        }
    }

    // =====================================================================
    // Box / party display
    // =====================================================================

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
    }

    private void LoadBox(int box)
    {
        if (_sav is null || !_sav.HasBox)
            return;
        for (int i = 0; i < BoxSlots.Count; i++)
        {
            var pk = _sav.GetBoxSlotAtIndex(box, i);
            BoxSlots[i].Update(pk, _strings);
        }
    }

    private void LoadParty()
    {
        if (_sav is null || !_sav.HasParty)
            return;
        for (int i = 0; i < 6; i++)
        {
            var pk = i < _sav.PartyCount ? _sav.GetPartySlotAtIndex(i) : null;
            PartySlots[i].Update(pk?.Species > 0 ? pk : null, _strings);
        }
    }

    private void RefreshSlotViews()
    {
        LoadBox(CurrentBox);
        LoadParty();
    }

    /// <summary>Reads the current contents of a slot from the save.</summary>
    private PKM? ReadSlot(SlotViewModel slot)
    {
        if (_sav is null)
            return null;
        if (slot.IsParty)
            return slot.Slot < _sav.PartyCount ? _sav.GetPartySlotAtIndex(slot.Slot) : null;
        return _sav.GetBoxSlotAtIndex(CurrentBox, slot.Slot);
    }

    /// <summary>Writes a PKM into a slot (party writes are compacted).</summary>
    private void WriteSlot(SlotViewModel slot, PKM pk)
    {
        if (_sav is null)
            return;
        pk.RefreshChecksum();
        if (slot.IsParty)
        {
            var index = Math.Min(slot.Slot, _sav.PartyCount);
            _sav.SetPartySlotAtIndex(pk, index);
        }
        else
        {
            _sav.SetBoxSlotAtIndex(pk, CurrentBox, slot.Slot);
        }
    }

    // =====================================================================
    // Slot operations
    // =====================================================================

    [RelayCommand]
    public void SelectSlot(SlotViewModel? slot)
    {
        if (_selected is not null)
            _selected.IsSelected = false;
        _selected = slot;
        if (slot is not null)
            slot.IsSelected = true;
        Detail.Load(slot?.Pokemon);
    }

    [RelayCommand]
    public void ApplyDetailChanges()
    {
        if (_sav is null || _selected is null || Detail.Pokemon is not { } pk)
            return;
        WriteSlot(_selected, pk);
        RefreshSlotViews();
        Detail.Load(pk);
        StatusText = $"Applied changes to {Detail.SpeciesName}. Remember to export the save (⌘S).";
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
            _sav.SetBoxSlotAtIndex(_sav.BlankPKM, CurrentBox, slot.Slot);
        }
        RefreshSlotViews();
        if (_selected == slot)
            Detail.Load(null);
        StatusText = "Slot cleared.";
    }

    public void CopySlot(SlotViewModel slot)
    {
        var pk = ReadSlot(slot);
        if (pk is null || pk.Species == 0)
            return;
        _clipboardPk = pk.Clone();
        StatusText = $"Copied {_strings.specieslist[pk.Species]}.";
    }

    public bool CanPaste => _clipboardPk is not null;

    public void PasteSlot(SlotViewModel slot)
    {
        if (_sav is null || _clipboardPk is null)
            return;
        WriteSlot(slot, _clipboardPk.Clone());
        RefreshSlotViews();
        StatusText = $"Pasted {_strings.specieslist[_clipboardPk.Species]}.";
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
            // Swap
            WriteSlot(from, pkTo);
            WriteSlot(to, pkFrom);
        }
        else
        {
            // Move
            WriteSlot(to, pkFrom);
            if (from.IsParty)
                DeleteSlot(from);
            else
                _sav.SetBoxSlotAtIndex(_sav.BlankPKM, CurrentBox, from.Slot);
        }
        RefreshSlotViews();
        SelectSlot(to.IsParty ? PartySlots[to.Slot] : BoxSlots[to.Slot]);
        StatusText = "Moved.";
    }

    /// <summary>Imports a .pk*/.pb*/etc entity file into a slot, converting format if needed.</summary>
    public bool ImportEntityFile(SlotViewModel slot, string path, out string message)
    {
        message = string.Empty;
        if (_sav is null)
            return false;
        try
        {
            var data = File.ReadAllBytes(path);
            var prefer = EntityFileExtension.GetContextFromExtension(path, _sav.Context);
            var pk = EntityFormat.GetFromBytes(data, prefer);
            if (pk is null)
            {
                message = "Not a recognizable Pokémon entity file.";
                return false;
            }
            if (pk.GetType() != _sav.PKMType)
            {
                pk = EntityConverter.ConvertToType(pk, _sav.PKMType, out var result);
                if (pk is null)
                {
                    message = $"Cannot convert to this save's format: {result}";
                    return false;
                }
            }
            WriteSlot(slot, pk);
            RefreshSlotViews();
            SelectSlot(slot.IsParty ? PartySlots[slot.Slot] : BoxSlots[slot.Slot]);
            message = $"Imported {_strings.specieslist[pk.Species]}.";
            StatusText = message;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public string? GetSlotShowdownText(SlotViewModel slot)
    {
        var pk = ReadSlot(slot);
        return pk is null || pk.Species == 0 ? null : new ShowdownSet(pk).Text;
    }

    /// <summary>Adds a Pokémon into the first empty slot of the current box.</summary>
    public bool TryAddToCurrentBox(PKM pk, out string message)
    {
        message = string.Empty;
        if (_sav is null || !_sav.HasBox)
        {
            message = "No save loaded.";
            return false;
        }
        int empty = -1;
        for (int i = 0; i < _sav.BoxSlotCount; i++)
        {
            if (_sav.GetBoxSlotAtIndex(CurrentBox, i).Species == 0)
            {
                empty = i;
                break;
            }
        }
        if (empty < 0)
        {
            message = $"{CurrentBoxName} is full — clear a slot or switch boxes.";
            return false;
        }
        pk.RefreshChecksum();
        _sav.SetBoxSlotAtIndex(pk, CurrentBox, empty);
        RefreshSlotViews();
        SelectSlot(BoxSlots[empty]);
        var name = (uint)pk.Species < _strings.specieslist.Length ? _strings.specieslist[pk.Species] : $"#{pk.Species}";
        message = $"Added {name} to {CurrentBoxName}, slot {empty + 1}.";
        StatusText = message;
        return true;
    }

    /// <summary>Re-reads trainer card fields after an external edit (trainer editor dialog).</summary>
    public void RefreshTrainerCard()
    {
        if (_sav is null)
            return;
        TrainerName = _sav.OT;
        TrainerIds = $"TID {_sav.DisplayTID:D6} · SID {_sav.DisplaySID:D4}";
        PlayTime = _sav.PlayTimeString;
        StatusText = "Trainer info updated. Remember to export the save (⌘S).";
    }

    // =====================================================================
    // Box tools
    // =====================================================================

    [RelayCommand]
    public void SortCurrentBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        var data = _sav.GetBoxData(CurrentBox);
        Array.Sort(data, (a, b) =>
        {
            if (a.Species == 0)
                return b.Species == 0 ? 0 : 1;
            if (b.Species == 0)
                return -1;
            var bySpecies = a.Species.CompareTo(b.Species);
            return bySpecies != 0 ? bySpecies : a.Form.CompareTo(b.Form);
        });
        for (int i = 0; i < data.Length; i++)
        {
            data[i].RefreshChecksum();
            _sav.SetBoxSlotAtIndex(data[i], CurrentBox, i);
        }
        RefreshSlotViews();
        StatusText = $"Sorted {CurrentBoxName} by species.";
    }

    [RelayCommand]
    public void ClearCurrentBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        for (int i = 0; i < _sav.BoxSlotCount; i++)
            _sav.SetBoxSlotAtIndex(_sav.BlankPKM, CurrentBox, i);
        RefreshSlotViews();
        Detail.Load(null);
        StatusText = $"Cleared {CurrentBoxName}.";
    }

    [RelayCommand]
    public void PreviousBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        CurrentBox = (CurrentBox - 1 + _sav.BoxCount) % _sav.BoxCount;
    }

    [RelayCommand]
    public void NextBox()
    {
        if (_sav is null || !_sav.HasBox)
            return;
        CurrentBox = (CurrentBox + 1) % _sav.BoxCount;
    }
}
