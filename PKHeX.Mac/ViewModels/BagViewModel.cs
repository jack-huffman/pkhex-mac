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

/// <summary>Editor for the trainer's inventory pouches. Writes to the save on Apply.</summary>
public partial class BagViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly PlayerBag _bag;

    /// <summary>Item id to the move its machine teaches, for TM pouches.</summary>
    private readonly Dictionary<int, string> _machineMoves = [];

    public BagViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        _bag = sav.Inventory;
        BuildMachineMoveNames();
        foreach (var pouch in _bag.Pouches)
            PouchNames.Add(pouch.Type.ToString());
        HasPouches = _bag.Pouches.Count > 0;
        if (HasPouches)
            SelectedPouchIndex = 0;
    }

    public bool HasPouches { get; }
    public ObservableCollection<string> PouchNames { get; } = [];
    public ObservableCollection<BagItemRowViewModel> Rows { get; } = [];

    [ObservableProperty] private int _selectedPouchIndex = -1;
    [ObservableProperty] private BagItemRowViewModel? _selectedRow;
    [ObservableProperty] private IReadOnlyList<ComboItem> _itemPickerChoices = [];
    [ObservableProperty] private int _pickerValue;
    [ObservableProperty] private int _maxCount = 999;
    [ObservableProperty] private int _giveAllCount = 1;
    [ObservableProperty] private string _pouchSummary = string.Empty;

    /// <summary>
    /// TM items are named only "TM01" in the game's string table. The move each one
    /// teaches comes from the technical-record ordering, where record 0 is TM00 — so
    /// the TM's printed number indexes that list directly.
    /// </summary>
    private void BuildMachineMoveNames()
    {
        try
        {
            if (_sav.BlankPKM is not ITechRecord record)
                return;
            var moves = record.Permit.RecordPermitIndexes;
            var pouch = _bag.Pouches.FirstOrDefault(p => p.Type == InventoryType.TMHMs);
            if (pouch is null)
                return;

            foreach (var id in _bag.Info.GetItems(InventoryType.TMHMs))
            {
                if (id >= _strings.itemlist.Length)
                    continue;
                var name = _strings.itemlist[id];
                if (!name.StartsWith("TM", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!int.TryParse(name.AsSpan(2), out var number) || (uint)number >= moves.Length)
                    continue;
                var move = moves[number];
                if (move != 0 && move < _strings.movelist.Length)
                    _machineMoves[id] = _strings.movelist[move];
            }
        }
        catch
        {
            _machineMoves.Clear(); // labels are a nicety; never block the editor
        }
    }

    /// <summary>Item label, with the taught move appended for machines.</summary>
    internal string DescribeItem(int id)
    {
        if (id == 0)
            return "—";
        var name = (uint)id < _strings.itemlist.Length ? _strings.itemlist[id] : $"#{id}";
        return _machineMoves.TryGetValue(id, out var move) ? $"{name} · {move}" : name;
    }

    private InventoryPouch? CurrentPouch =>
        (uint)SelectedPouchIndex < _bag.Pouches.Count ? _bag.Pouches[SelectedPouchIndex] : null;

    partial void OnSelectedPouchIndexChanged(int value)
    {
        Rows.Clear();
        if (CurrentPouch is not { } pouch)
            return;
        MaxCount = pouch.MaxCount;
        GiveAllCount = Math.Min(pouch.MaxCount, 1);
        foreach (var item in pouch.Items)
            Rows.Add(new BagItemRowViewModel(item, DescribeItem, pouch.MaxCount));
        PouchSummary = $"{pouch.Count} of {pouch.Items.Length} slots used · max {pouch.MaxCount} per item";

        // Legal items for this pouch, for the picker.
        var legal = _bag.Info.GetItems(pouch.Type);
        var choices = new List<ComboItem>(legal.Length + 1) { new("(None)", 0) };
        foreach (var id in legal)
        {
            if (id < _strings.itemlist.Length)
                choices.Add(new ComboItem(DescribeItem(id), id));
        }
        choices.Sort((a, b) => a.Value == 0 ? -1 : b.Value == 0 ? 1 : string.CompareOrdinal(a.Text, b.Text));
        ItemPickerChoices = choices;
    }

    partial void OnSelectedRowChanged(BagItemRowViewModel? value)
    {
        if (value is not null)
            PickerValue = value.Index;
    }

    [RelayCommand]
    public void SetSelectedItem()
    {
        if (SelectedRow is not { } row)
            return;
        row.SetItem(PickerValue);
    }

    [RelayCommand]
    public void ClearSelectedItem()
    {
        SelectedRow?.SetItem(0);
    }

    /// <summary>Fills the pouch with every item the game allows there.</summary>
    [RelayCommand]
    public void GiveAllItems()
    {
        if (CurrentPouch is not { } pouch)
            return;
        pouch.GiveAllItems(_bag, Math.Clamp(GiveAllCount, 1, pouch.MaxCount));
        ReloadPouch();
    }

    [RelayCommand]
    public void SortByName()
    {
        if (CurrentPouch is not { } pouch)
            return;
        pouch.SortByName(_strings.itemlist);
        ReloadPouch();
    }

    [RelayCommand]
    public void SortByCount()
    {
        if (CurrentPouch is not { } pouch)
            return;
        pouch.SortByCount(reverse: true);
        ReloadPouch();
    }

    [RelayCommand]
    public void ClearPouch()
    {
        if (CurrentPouch is not { } pouch)
            return;
        foreach (var item in pouch.Items)
            item.Clear();
        ReloadPouch();
    }

    /// <summary>Rebuilds the row list after a bulk pouch operation.</summary>
    private void ReloadPouch()
    {
        var index = SelectedPouchIndex;
        SelectedPouchIndex = -1;
        SelectedPouchIndex = index;
    }

    public void Apply() => _bag.CopyTo(_sav);
}

/// <summary>One inventory slot row.</summary>
public partial class BagItemRowViewModel : ObservableObject
{
    private readonly InventoryItem _item;
    private readonly Func<int, string> _describe;
    private readonly int _maxCount;
    private bool _loading;

    public BagItemRowViewModel(InventoryItem item, Func<int, string> describe, int maxCount)
    {
        _item = item;
        _describe = describe;
        _maxCount = maxCount;
        _loading = true;
        Count = item.Count;
        _loading = false;
        ItemName = NameOf(item.Index);
        Sprite = SpriteService.GetItemSprite(item.Index);
    }

    public int Index => _item.Index;

    [ObservableProperty] private string _itemName = string.Empty;
    [ObservableProperty] private int _count;
    [ObservableProperty] private Bitmap? _sprite;

    /// <summary>The pouch's per-item ceiling, so the input cannot promise more than the game stores.</summary>
    public int MaxCount => _maxCount;

    public void SetItem(int itemId)
    {
        _item.Index = itemId;
        ItemName = NameOf(itemId);
        Sprite = SpriteService.GetItemSprite(itemId);
        if (itemId == 0)
        {
            _item.Count = 0;
            Count = 0;
        }
        else if (_item.Count == 0)
        {
            _item.Count = 1;
            Count = 1;
        }
        OnPropertyChanged(nameof(Index));
    }

    partial void OnCountChanged(int value)
    {
        if (_loading)
            return;
        _item.Count = System.Math.Clamp(value, 0, _maxCount);
    }

    private string NameOf(int id) => _describe(id);
}
