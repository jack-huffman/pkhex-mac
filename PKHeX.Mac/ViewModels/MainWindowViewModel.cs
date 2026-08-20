using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    /// <summary>Whether the in-memory save differs from the file on disk.</summary>
    public SaveStateViewModel SaveState { get; } = new();

    /// <summary>Shown when closing would discard unsaved edits.</summary>
    [ObservableProperty] private bool _isClosePromptOpen;

    /// <summary>Extra line in the close prompt, for inspector edits not yet applied.</summary>
    [ObservableProperty] private string _closePromptNote = string.Empty;

    public bool HasUnappliedDetail => ClosePromptNote.Length > 0;

    partial void OnClosePromptNoteChanged(string value) => OnPropertyChanged(nameof(HasUnappliedDetail));

    /// <summary>
    /// Anything that would be lost by quitting: edits written into the in-memory save,
    /// plus edits typed into the inspector that have not been applied to a slot yet.
    /// </summary>
    public bool HasPendingWork => SaveState.HasUnsavedChanges || Detail.IsDirty;

    public PokemonDetailViewModel Detail { get; }
    public PokemonPreviewViewModel Preview { get; }

    // ---- In-window views (sidebar navigation) ----

    [ObservableProperty] private string _currentView = "boxes";
    [ObservableProperty] private TrainerEditorViewModel? _trainer;
    [ObservableProperty] private BagViewModel? _bag;
    [ObservableProperty] private AddPokemonViewModel? _addDb;
    [ObservableProperty] private GiftsViewModel? _giftDb;
    [ObservableProperty] private PokedexViewModel? _dex;
    [ObservableProperty] private ToolsViewModel? _tools;
    [ObservableProperty] private EventFlagsViewModel? _eventFlags;
    [ObservableProperty] private SaveBlocksViewModel? _saveBlocks;
    [ObservableProperty] private RaidsViewModel? _raids;
    [ObservableProperty] private TrainerStyleViewModel? _style;
    [ObservableProperty] private BlueberryViewModel? _blueberry;
    [ObservableProperty] private TrainerRecordsViewModel? _records;
    [ObservableProperty] private DaycareViewModel? _daycare;
    [ObservableProperty] private GiftAlbumViewModel? _giftAlbum;
    [ObservableProperty] private GameExtrasViewModel? _extras;
    [ObservableProperty] private MailViewModel? _mail;
    [ObservableProperty] private HallOfFameViewModel? _hallOfFame;
    [ObservableProperty] private SearchViewModel? _search;

    public bool IsBoxesView => CurrentView == "boxes";
    public bool IsSaveView => CurrentView == "save";
    public bool IsDexView => CurrentView == "dex";
    public bool IsToolsView => CurrentView == "tools";
    public bool IsFlagsView => CurrentView == "flags";
    public bool IsRaidsView => CurrentView == "raids";
    public bool IsSearchView => CurrentView == "search";
    public bool IsGameDataView => CurrentView == "gamedata";
    public bool IsAddView => CurrentView == "add";
    public bool IsGiftsView => CurrentView == "gifts";
    public bool IsDatabaseView => IsAddView || IsGiftsView;

    /// <summary>The box grid stays on screen while browsing a database, so a
    /// destination slot can be picked before adding.</summary>
    public bool ShowBoxes => IsBoxesView || IsDatabaseView;

    /// <summary>
    /// The databases only make sense when there is somewhere to put the result, so
    /// they unlock once an empty box slot is selected.
    /// </summary>
    public bool CanUseDatabases => HasSave && _selected is { IsParty: false, IsEmpty: true };

    [ObservableProperty] private string _databaseHint = "Select an empty box slot to add a Pokémon";

    /// <summary>Dims the box list while another view is showing, so its selection
    /// does not read as the active section.</summary>
    public double BoxListOpacity => IsBoxesView ? 1.0 : 0.5;

    /// <summary>Collapses the inspector column for the full-width Save view.</summary>
    public Avalonia.Controls.GridLength InspectorWidth =>
        IsSaveView || IsDexView || IsToolsView || IsFlagsView || IsRaidsView || IsSearchView || IsGameDataView
            ? new Avalonia.Controls.GridLength(0)
            : new Avalonia.Controls.GridLength(438);

    partial void OnCurrentViewChanged(string value)
    {
        OnPropertyChanged(nameof(IsBoxesView));
        OnPropertyChanged(nameof(IsSaveView));
        OnPropertyChanged(nameof(IsDexView));
        OnPropertyChanged(nameof(IsToolsView));
        OnPropertyChanged(nameof(IsFlagsView));
        OnPropertyChanged(nameof(IsRaidsView));
        OnPropertyChanged(nameof(IsSearchView));
        OnPropertyChanged(nameof(IsGameDataView));
        OnPropertyChanged(nameof(IsAddView));
        OnPropertyChanged(nameof(IsGiftsView));
        OnPropertyChanged(nameof(IsDatabaseView));
        OnPropertyChanged(nameof(ShowBoxes));
        OnPropertyChanged(nameof(InspectorWidth));
        OnPropertyChanged(nameof(BoxListOpacity));
    }

    [RelayCommand]
    public void SetView(string view)
    {
        if (_sav is null && view != "boxes")
        {
            StatusText = "Open a save file first (⌘O).";
            return;
        }
        if (CurrentView == view)
            return; // already here: keep any preview/selection intact

        if (view is "add" or "gifts" && !CanUseDatabases)
        {
            StatusText = "Select an empty slot in a box first — that's where the Pokémon will go.";
            return;
        }
        if (view == "search" && _sav is not null)
            Search ??= new SearchViewModel(_sav, _strings, GameInfo.FilteredSources);
        if (view == "gamedata" && _sav is not null)
        {
            Daycare ??= BuildDaycare(_sav);
            if (GiftAlbum is null)
            {
                StatusText = "Reading the Mystery Gift album…";
                GiftAlbum = new GiftAlbumViewModel(_sav, _strings, () =>
                    NoteChange("Gift album updated"));
            }
            Extras ??= new GameExtrasViewModel(_sav, () =>
                NoteChange("Save structure edited"));
            Mail ??= new MailViewModel(_sav, _strings, () =>
                NoteChange("Mail updated"));
            HallOfFame ??= new HallOfFameViewModel(_sav, _strings, () =>
                NoteChange("Hall of Fame updated"));
        }
        if (view == "raids" && _sav is not null)
        {
            Raids ??= new RaidsViewModel(_sav, () =>
                NoteChange("Raid records updated"));
        }
        if (view == "flags" && _sav is not null)
        {
            EventFlags ??= new EventFlagsViewModel(_sav, () =>
                NoteChange("Event flags updated"));
            if (SaveBlocks is null)
            {
                StatusText = "Reading save blocks…";
                SaveBlocks = new SaveBlocksViewModel(_sav, () =>
                    NoteChange("Save block changed"));
            }
        }
        if (view == "tools" && _sav is not null)
        {
            Tools ??= new ToolsViewModel(_sav, _strings, () =>
            {
                RefreshSlotViews();
                NoteChange("Batch changes applied.");
            });
            Tools.CurrentBox = CurrentBox;
        }
        if (view == "dex" && Dex is null && _sav is not null)
        {
            StatusText = "Loading the Pokédex…";
            Dex = new PokedexViewModel(_sav, _strings, () =>
                NoteChange("Pokédex updated"));
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
        if (!(IsDatabaseView && (view == "add" || view == "gifts")))
            Preview.Load(null);
        CurrentView = view;
        RefreshTargetSlotText();
    }

    /// <summary>
    /// Builds the daycare editor, wiring its box transfers to the selected slot so a
    /// boarded parent can be moved into storage and edited with the full inspector.
    /// </summary>
    private DaycareViewModel BuildDaycare(SaveFile sav)
    {
        var vm = new DaycareViewModel(sav, _strings, () =>
            NoteChange("Daycare updated"));
        vm.ReadSelectedSlot = () => _selected?.Pokemon;
        vm.WriteSelectedSlot = pk =>
        {
            if (_selected is null)
                return;
            WriteSlot(_selected, pk);
            RefreshSlotViews();
        };
        return vm;
    }

    /// <summary>Applies trainer identity and bag edits together, then returns to the boxes.</summary>
    public void ApplySave()
    {
        Trainer?.Apply();
        Bag?.Apply();
        RefreshTrainerCard();
        NoteChange("Trainer info and bag updated.");
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

    /// <summary>
    /// Writes the previewed entity into the slot the user selected, or the first
    /// empty slot in the current box when nothing is selected.
    /// </summary>
    public void AddPreviewToBox()
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
            var name = (uint)clone.Species < _strings.specieslist.Length ? _strings.specieslist[clone.Species] : $"#{clone.Species}";
            NoteChange($"Placed {name} in {CurrentBoxName}, slot {slot.Slot + 1}");
            SelectSlot(BoxSlots[slot.Slot]); // reselect so the editor shows what landed
            return;
        }
        if (!TryAddToCurrentBox(clone, out var message))
            StatusText = message;
        RefreshTargetSlotText();
    }

    [ObservableProperty] private string _targetSlotText = "Add to first empty slot";

    /// <summary>Describes where the next "add" will land, for the button label.</summary>
    /// <summary>
    /// Reports an edit: shown on the status line, counted as unsaved, and added to the
    /// session log. Everything that mutates the save should go through here.
    /// </summary>
    private void NoteChange(string description)
    {
        StatusText = description;
        SaveState.NoteChange(description);
    }

    public void RefreshTargetSlotText()
    {
        TargetSlotText = _selected is { IsParty: false } s
            ? $"Add to {CurrentBoxName}, slot {s.Slot + 1}"
            : "Add to first empty slot";
        OnPropertyChanged(nameof(CanUseDatabases));
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
    private bool _switchingBox;

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
            SaveState.Reset();          // a different save: previous edits are moot
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
            _switchingBox = true;
            CurrentBoxName = BoxNames.Count > 0 ? BoxNames[0] : string.Empty;
            _switchingBox = false;
            LoadBox(0);
            LoadParty();
            SelectSlot(null);

            // In-window editor views for this save.
            Trainer = new TrainerEditorViewModel(sav);
            Bag = new BagViewModel(sav, _strings);
            Style = new TrainerStyleViewModel(sav, () =>
                NoteChange("Trainer appearance updated"));
            Blueberry = new BlueberryViewModel(sav, () =>
                NoteChange("Blueberry Academy data updated"));
            // Unlocking throw styles writes to the club board, so keep that view honest.
            Style.BoardChanged = () => Blueberry?.Reload();
            Records = new TrainerRecordsViewModel(sav, () =>
                NoteChange("Trainer records updated"));
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
            // Back up whatever is already there before overwriting it. Writing a
            // corrupt save over the only copy would cost real playtime.
            var backup = TryBackup(path);
            var data = _sav.Write();
            File.WriteAllBytes(path, data.ToArray());
            _savPath = path;
            StatusText = backup is null
                ? $"Saved to {Path.GetFileName(path)}"
                : $"Saved to {Path.GetFileName(path)} (previous version kept as {Path.GetFileName(backup)})";
            SaveState.MarkSaved(Path.GetFileName(path));
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to write save file:\n{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Copies an existing save aside before it is overwritten. Returns the backup
    /// path, or null when there was nothing to back up. Never throws — a failed
    /// backup must not block the export.
    /// </summary>
    private static string? TryBackup(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var dir = Path.GetDirectoryName(path) ?? ".";
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backup = Path.Combine(dir, $"{Path.GetFileName(path)}.{stamp}.bak");
            File.Copy(path, backup, overwrite: false);
            return backup;
        }
        catch
        {
            return null;
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
        _switchingBox = true;
        CurrentBoxName = (uint)value < BoxNames.Count ? BoxNames[value] : $"Box {value + 1}";
        _switchingBox = false;
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
        RefreshTargetSlotText();
        OnPropertyChanged(nameof(CanUseDatabases));
        if (IsDatabaseView && !CanUseDatabases)
            CurrentView = "boxes"; // the chosen slot is no longer empty

    }

    [RelayCommand]
    public void ApplyDetailChanges()
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
        NoteChange("Trainer info updated.");
    }

    /// <summary>Reveals a search hit that lives in this save.</summary>
    [RelayCommand]
    public void GoToSearchResult(SearchResultViewModel? result)
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
    public void ImportSearchResult(SearchResultViewModel? result)
    {
        if (result is null || _sav is null)
            return;
        var pk = result.Candidate.Entity;
        if (pk.GetType() != _sav.PKMType)
        {
            var converted = EntityConverter.ConvertToType(pk, _sav.PKMType, out var res);
            if (converted is null)
            {
                StatusText = $"Cannot bring that Pokémon into this save ({res}).";
                return;
            }
            pk = converted;
        }
        if (!TryAddToCurrentBox(pk.Clone(), out var message))
            StatusText = message;
    }

    // =====================================================================
    // Folder import / export
    // =====================================================================

    /// <summary>Writes every Pokémon in the current box to a folder.</summary>
    public int DumpToFolder(string folder)
    {
        if (_sav is null || !_sav.HasBox)
            return 0;
        var written = 0;
        for (int index = 0; index < _sav.BoxSlotCount; index++)
        {
            var pk = _sav.GetBoxSlotAtIndex(CurrentBox, index);
            if (pk.Species == 0)
                continue;
            try
            {
                var data = new byte[pk.SIZE_PARTY];
                pk.WriteDecryptedDataParty(data);
                var name = PathUtil.CleanFileName(pk.FileName);
                var path = Path.Combine(folder, name);
                // Never clobber: same species+nickname can repeat in a box.
                var suffix = 1;
                while (File.Exists(path))
                {
                    var stem = Path.GetFileNameWithoutExtension(name);
                    path = Path.Combine(folder, $"{stem} ({++suffix}){Path.GetExtension(name)}");
                }
                File.WriteAllBytes(path, data);
                written++;
            }
            catch
            {
                // skip unwritable entries rather than aborting the dump
            }
        }
        StatusText = $"Exported {written} Pokémon to {Path.GetFileName(folder)}.";
        return written;
    }

    /// <summary>Loads every readable entity file in a folder into the current box's free slots.</summary>
    public (int loaded, int skipped) LoadFromFolder(string folder)
    {
        if (_sav is null || !_sav.HasBox)
            return (0, 0);
        int loaded = 0, skipped = 0;
        var files = Directory.EnumerateFiles(folder).OrderBy(f => f).ToList();
        var next = 0;

        foreach (var file in files)
        {
            if (next >= _sav.BoxSlotCount)
                break;
            PKM? pk;
            try
            {
                var data = File.ReadAllBytes(file);
                var prefer = EntityFileExtension.GetContextFromExtension(file, _sav.Context);
                pk = EntityFormat.GetFromBytes(data, prefer);
            }
            catch
            {
                pk = null;
            }
            if (pk is null || pk.Species == 0)
            {
                skipped++;
                continue;
            }
            if (pk.GetType() != _sav.PKMType)
            {
                pk = EntityConverter.ConvertToType(pk, _sav.PKMType, out _);
                if (pk is null)
                {
                    skipped++;
                    continue;
                }
            }
            // Fill the next empty slot.
            while (next < _sav.BoxSlotCount && _sav.GetBoxSlotAtIndex(CurrentBox, next).Species != 0)
                next++;
            if (next >= _sav.BoxSlotCount)
                break;
            pk.RefreshChecksum();
            _sav.SetBoxSlotAtIndex(pk, CurrentBox, next);
            loaded++;
            next++;
        }
        RefreshSlotViews();
        StatusText = $"Imported {loaded} Pokémon into {CurrentBoxName}"
                     + (skipped > 0 ? $" ({skipped} file(s) skipped)" : string.Empty) + ".";
        return (loaded, skipped);
    }

    // =====================================================================
    // Cross-box moves and search
    // =====================================================================

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
        if (!from.IsParty && targetBox == CurrentBox)
            return; // same box: the grid drag already handles this

        int empty = -1;
        for (int i = 0; i < _sav.BoxSlotCount; i++)
        {
            if (_sav.GetBoxSlotAtIndex(targetBox, i).Species == 0)
            {
                empty = i;
                break;
            }
        }
        var boxName = (uint)targetBox < BoxNames.Count ? BoxNames[targetBox] : $"Box {targetBox + 1}";
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
            _sav.SetBoxSlotAtIndex(_sav.BlankPKM, CurrentBox, from.Slot);
        RefreshSlotViews();
        var name = (uint)moved.Species < _strings.specieslist.Length ? _strings.specieslist[moved.Species] : $"#{moved.Species}";
        StatusText = $"Moved {name} to {boxName}, slot {empty + 1}.";
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
