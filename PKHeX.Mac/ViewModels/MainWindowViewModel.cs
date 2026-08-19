using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private SaveFile? _sav;
    private string? _savPath;

    public MainWindowViewModel()
    {
        Detail = new PokemonDetailViewModel(_strings);
        for (int i = 0; i < 30; i++)
            BoxSlots.Add(new SlotViewModel(0, i));
        for (int i = 0; i < 6; i++)
            PartySlots.Add(new SlotViewModel(-1, i));
    }

    public PokemonDetailViewModel Detail { get; }

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

    private SlotViewModel? _selected;

    public SaveFile? SAV => _sav;
    public string? SavePath => _savPath;

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
                var names = BoxUtil.GetBoxNames(sav);
                foreach (var n in names)
                    BoxNames.Add(n);
            }

            RebuildBoxSlots();
            CurrentBox = 0;
            CurrentBoxName = BoxNames.Count > 0 ? BoxNames[0] : string.Empty;
            LoadBox(0);
            LoadParty();
            SelectSlot(null);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load save file:\n{ex.Message}";
            return false;
        }
    }

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

        pk.RefreshChecksum();
        if (_selected.IsParty)
            _sav.SetPartySlotAtIndex(pk, _selected.Slot);
        else
            _sav.SetBoxSlotAtIndex(pk, CurrentBox, _selected.Slot);

        _selected.Update(pk, _strings);
        Detail.Load(pk);
        StatusText = $"Applied changes to {Detail.SpeciesName}. Remember to export the save (⌘S).";
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
