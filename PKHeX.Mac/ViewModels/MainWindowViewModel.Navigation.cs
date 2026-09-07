using System;
using System.Collections.Generic;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>Which view is showing, the editors behind each, and the command palette.</summary>
public sealed partial class MainWindowViewModel
{
    private const int InspectorColumnWidth = 438;

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
    [ObservableProperty] private CrownTundraViewModel? _crownTundra;
    [ObservableProperty] private TrainerRecordsViewModel? _records;
    [ObservableProperty] private DaycareViewModel? _daycare;
    [ObservableProperty] private FusionViewModel? _fusions;
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

    /// <summary>Collapses the inspector column for the full-width views.</summary>
    public GridLength InspectorWidth => ShowBoxes ? new GridLength(InspectorColumnWidth) : new GridLength(0);

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
        if (_sav is not null)
            BuildLazyEditors(view, _sav);
        if (!(IsDatabaseView && (view == "add" || view == "gifts")))
            Preview.Load(null);
        CurrentView = view;
        RefreshTargetSlotText();
    }

    /// <summary>
    /// The expensive editors are built the first time their view opens: the block editor
    /// builds ten thousand rows, and the Pokédex and gift archive are heavier still.
    /// </summary>
    private void BuildLazyEditors(string view, SaveFile sav)
    {
        switch (view)
        {
            case "search":
                Search ??= new SearchViewModel(sav, _strings, _sources);
                break;
            case "gamedata":
                Daycare ??= BuildDaycare(sav);
                Fusions ??= BuildFusions(sav);
                if (GiftAlbum is null)
                {
                    StatusText = "Reading the Mystery Gift album…";
                    GiftAlbum = new GiftAlbumViewModel(sav, _strings, () => NoteChange("Gift album updated"));
                }
                Extras ??= new GameExtrasViewModel(sav, () => NoteChange("Save structure edited"));
                Mail ??= new MailViewModel(sav, _strings, () => NoteChange("Mail updated"));
                HallOfFame ??= new HallOfFameViewModel(sav, _strings, () => NoteChange("Hall of Fame updated"));
                break;
            case "raids":
                Raids ??= new RaidsViewModel(sav, () => NoteChange("Raid records updated"));
                break;
            case "flags":
                EventFlags ??= new EventFlagsViewModel(sav, () => NoteChange("Event flags updated"));
                if (SaveBlocks is null)
                {
                    StatusText = "Reading save blocks…";
                    SaveBlocks = new SaveBlocksViewModel(sav, () => NoteChange("Save block changed"));
                }
                break;
            case "tools":
                Tools ??= new ToolsViewModel(sav, _strings);
                break;
            case "dex":
                if (Dex is null)
                {
                    StatusText = "Loading the Pokédex…";
                    Dex = new PokedexViewModel(sav, _strings, () => NoteChange("Pokédex updated"));
                }
                break;
            case "gifts":
                if (GiftDb is null)
                {
                    StatusText = "Loading the Mystery Gift archive…";
                    GiftDb = new GiftsViewModel(sav, _strings)
                    {
                        PreviewReady = pk => Preview.Load(pk),
                        Blocked = reason => Preview.ShowBlocked(reason),
                    };
                }
                break;
        }
    }

    /// <summary>
    /// Builds the editors whose availability can only be known by looking.
    /// </summary>
    /// <remarks>
    /// Most of these report support as "did I find anything" rather than a type check,
    /// so nothing can say whether a save has mail or a daycare without building the
    /// editor. Doing it at load costs about 17ms and lets tabs and the palette hide
    /// what this save does not have, instead of offering a pane that explains itself
    /// away. The genuinely expensive ones stay lazy; see <see cref="BuildLazyEditors"/>.
    /// </remarks>
    private void BuildEditors(SaveFile sav)
    {
        Trainer = new TrainerEditorViewModel(sav, () => NoteChange("Trainer progression updated"));
        Bag = new BagViewModel(sav, _strings);
        Style = new TrainerStyleViewModel(sav, () => NoteChange("Trainer appearance updated"));
        Blueberry = new BlueberryViewModel(sav, () => NoteChange("Blueberry Academy data updated"));
        // Unlocking throw styles writes to the club board, so keep that view honest.
        Style.BoardChanged = () => Blueberry?.Reload();
        CrownTundra = new CrownTundraViewModel(sav, _strings, () => NoteChange("Crown Tundra data updated"));
        Records = new TrainerRecordsViewModel(sav, () => NoteChange("Trainer records updated"));
        AddDb = new AddPokemonViewModel(sav, _sources, _strings) { PreviewReady = pk => Preview.Load(pk) };
        Daycare = BuildDaycare(sav);
        Fusions = BuildFusions(sav);
        Extras = new GameExtrasViewModel(sav, () => NoteChange("Game data updated"));
        Mail = new MailViewModel(sav, _strings, () => NoteChange("Mail updated"));
        HallOfFame = new HallOfFameViewModel(sav, _strings, () => NoteChange("Hall of Fame updated"));
        EventFlags = new EventFlagsViewModel(sav, () => NoteChange("Event flags updated"));

        // These are rebuilt on first open for the new save.
        Tools?.Dispose();
        Tools = null;
        GiftDb = null;
        Dex = null;
        GiftAlbum = null;
        SaveBlocks = null;
        Raids = null;
        Search = null;
    }

    /// <summary>
    /// Builds the fusion-slot view, wiring extraction to the selected box slot so a
    /// parked donor can be recovered without unfusing in-game.
    /// </summary>
    private FusionViewModel BuildFusions(SaveFile sav)
    {
        var vm = new FusionViewModel(sav, _strings, NoteChange);
        vm.WriteSelectedSlot = pk =>
        {
            if (_selected is null)
                return;
            WriteSlot(_selected, pk);
            RefreshSlotViews();
        };
        return vm;
    }

    /// <summary>
    /// Builds the daycare editor, wiring its box transfers to the selected slot so a
    /// boarded parent can be moved into storage and edited with the full inspector.
    /// </summary>
    private DaycareViewModel BuildDaycare(SaveFile sav)
    {
        var vm = new DaycareViewModel(sav, _strings, () => NoteChange("Daycare updated"));
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
    [RelayCommand]
    private void ApplySave()
    {
        if (Trainer is { } trainer && !trainer.Apply(out var problem))
        {
            StatusText = problem;
            return;
        }
        Bag?.Apply();
        RefreshTrainerCard();
        NoteChange("Trainer info and bag updated.");
        CurrentView = "boxes";
    }

    /// <summary>Discards unapplied trainer/bag edits by rebuilding both editors from the save.</summary>
    [RelayCommand]
    private void ResetSave()
    {
        if (_sav is null)
            return;
        Trainer = new TrainerEditorViewModel(_sav, () => NoteChange("Trainer progression updated"));
        Bag = new BagViewModel(_sav, _strings);
        StatusText = "Reverted unsaved trainer and bag changes.";
    }

    // ---- Tabs inside the panels, so the palette can land on one directly ----

    [ObservableProperty] private int _trainerTab;
    [ObservableProperty] private int _toolsTab;
    [ObservableProperty] private int _gameDataTab;
    [ObservableProperty] private int _raidsTab;
    [ObservableProperty] private int _flagsTab;

    /// <summary>Jumps to a view, optionally selecting one of its tabs.</summary>
    private void GoTo(string view, Action? tab = null)
    {
        tab?.Invoke();
        SetView(view);
    }

    /// <summary>The Bag lives on the first tab of the trainer view.</summary>
    [RelayCommand]
    private void ShowBag() => GoTo("save", () => TrainerTab = 0);

    // ---- Command palette ----

    /// <summary>Type-to-go navigation; see <see cref="BuildPaletteEntries"/>.</summary>
    public CommandPaletteViewModel Palette { get; } = new();

    /// <summary>
    /// Everything the palette can reach. Rebuilt when a save opens, because the boxes
    /// and several destinations only exist once there is one.
    /// </summary>
    private void BuildPaletteEntries()
    {
        var entries = new List<PaletteEntry>
        {
            new("Boxes", "Navigate", () => GoTo("boxes")),
            new("Trainer & Bag", "Navigate", () => GoTo("save")),
            new("Pokédex", "Navigate", () => GoTo("dex")),
            new("Tools", "Navigate", () => GoTo("tools")),
            new("Raw Save Blocks", "Navigate", () => GoTo("flags")),
            new("Tera Raids", "Navigate", () => GoTo("raids")),
            new("Game Data", "Navigate", () => GoTo("gamedata")),
            new("Search & Database", "Navigate", () => GoTo("search")),
        };

        // A destination the save cannot offer is worse than a missing one: it promises
        // something and then explains itself away.
        void Tab(string title, string group, string view, Action select, bool available = true)
        {
            if (available)
                entries.Add(new PaletteEntry(title, group, () => GoTo(view, select)));
        }

        Tab("Bag / Items", "Trainer & Bag", "save", () => TrainerTab = 0, Bag?.HasPouches ?? false);
        Tab("Appearance & Style", "Trainer & Bag", "save", () => TrainerTab = 1, Style?.IsSupported ?? false);
        Tab("Ride abilities", "Trainer & Bag", "save", () => TrainerTab = 2, Trainer?.HasRideUpgrades ?? false);
        Tab("Gyms, Titans & Team Star", "Trainer & Bag", "save", () => TrainerTab = 3, Trainer?.HasBadges ?? false);
        Tab("Badges", "Trainer & Bag", "save", () => TrainerTab = 3, Trainer?.HasBadges ?? false);
        Tab("Blueberry Perks", "Trainer & Bag", "save", () => TrainerTab = 4, Blueberry?.IsSupported ?? false);
        Tab("Crown Tundra", "Trainer & Bag", "save", () => TrainerTab = 5, CrownTundra?.IsSupported ?? false);
        Tab("Max Lair", "Trainer & Bag", "save", () => TrainerTab = 5, CrownTundra?.IsSupported ?? false);
        Tab("Dynamax Adventure stuck", "Trainer & Bag", "save", () => TrainerTab = 5, CrownTundra?.IsSupported ?? false);
        Tab("Trainer Records", "Trainer & Bag", "save", () => TrainerTab = 6, Records?.IsSupported ?? false);

        // The transfer tab only exists with a second save open, so everything after it
        // shifts by one.
        var send = CanTransfer ? 1 : 0;
        Tab("Send to another save", "Tools", "tools", () => ToolsTab = 0, CanTransfer);
        Tab("Integrity audit", "Tools", "tools", () => ToolsTab = send);
        Tab("Box Report", "Tools", "tools", () => ToolsTab = send + 1);
        Tab("Breeding planner", "Tools", "tools", () => ToolsTab = send + 2);
        Tab("Egg moves", "Tools", "tools", () => ToolsTab = send + 2);
        Tab("Team Analysis", "Tools", "tools", () => ToolsTab = send + 3);
        Tab("Type coverage", "Tools", "tools", () => ToolsTab = send + 3);

        Tab("Daycare", "Game Data", "gamedata", () => GameDataTab = 0, Daycare?.IsSupported ?? false);
        Tab("Gift Album", "Game Data", "gamedata", () => GameDataTab = 1, _sav is IMysteryGiftStorageProvider);
        Tab("Fusions", "Game Data", "gamedata", () => GameDataTab = 2, Fusions?.IsSupported ?? false);
        Tab("Hall of Fame", "Game Data", "gamedata", () => GameDataTab = 3, HallOfFame?.IsSupported ?? false);
        Tab("Mail", "Game Data", "gamedata", () => GameDataTab = 4, Mail?.IsSupported ?? false);
        Tab("Extras", "Game Data", "gamedata", () => GameDataTab = 5, Extras?.IsSupported ?? false);

        var isSv = _sav is SAV9SV;
        Tab("Max Raid dens", "Raids", "raids", () => RaidsTab = 0, _sav is SAV8SWSH);
        Tab("Active raid dens", "Tera Raids", "raids", () => RaidsTab = 0, isSv);
        Tab("Event raid records", "Tera Raids", "raids", () => RaidsTab = 1, isSv);
        Tab("Raid progression", "Tera Raids", "raids", () => RaidsTab = 2, isSv);

        Tab("Event Flags", "Raw Save Blocks", "flags", () => FlagsTab = 0, EventFlags?.IsSupported ?? false);
        Tab("Save Blocks", "Raw Save Blocks", "flags", () => FlagsTab = 1, _sav is ISCBlockArray);

        entries.Add(new PaletteEntry("Review changes before export", "Action",
            () => _ = Review.OpenAsync()));
        entries.Add(new PaletteEntry("Export save", "Action", () => ExportRequested?.Invoke()));
        entries.Add(new PaletteEntry("Revert to saved file", "Action", RequestRevert));

        if (_sav is { HasBox: true })
        {
            for (int i = 0; i < _sav.BoxCount; i++)
            {
                var index = i;
                var name = (uint)i < BoxNames.Count ? BoxNames[i] : $"Box {i + 1}";
                entries.Add(new PaletteEntry(name, "Boxes", () =>
                {
                    CurrentBox = index;
                    SetView("boxes");
                }));
            }
        }

        Palette.SetEntries(entries);
    }
}
