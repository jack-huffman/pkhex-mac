using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>Editor for the trainer's inventory pouches. Writes to the save on Apply.</summary>
public partial class BagViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly PlayerBag _bag;

    public BagViewModel(SaveFile sav, GameStrings strings)
    {
        _sav = sav;
        _strings = strings;
        _bag = sav.Inventory;
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

    private InventoryPouch? CurrentPouch =>
        (uint)SelectedPouchIndex < _bag.Pouches.Count ? _bag.Pouches[SelectedPouchIndex] : null;

    partial void OnSelectedPouchIndexChanged(int value)
    {
        Rows.Clear();
        if (CurrentPouch is not { } pouch)
            return;
        MaxCount = pouch.MaxCount;
        foreach (var item in pouch.Items)
            Rows.Add(new BagItemRowViewModel(item, _strings, pouch.MaxCount));

        // Legal items for this pouch, for the picker.
        var legal = _bag.Info.GetItems(pouch.Type);
        var choices = new List<ComboItem>(legal.Length + 1) { new("(None)", 0) };
        foreach (var id in legal)
        {
            if (id < _strings.itemlist.Length)
                choices.Add(new ComboItem(_strings.itemlist[id], id));
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

    public void Apply() => _bag.CopyTo(_sav);
}

/// <summary>One inventory slot row.</summary>
public partial class BagItemRowViewModel : ObservableObject
{
    private readonly InventoryItem _item;
    private readonly GameStrings _strings;
    private readonly int _maxCount;
    private bool _loading;

    public BagItemRowViewModel(InventoryItem item, GameStrings strings, int maxCount)
    {
        _item = item;
        _strings = strings;
        _maxCount = maxCount;
        _loading = true;
        Count = item.Count;
        _loading = false;
        ItemName = NameOf(item.Index);
    }

    public int Index => _item.Index;

    [ObservableProperty] private string _itemName = string.Empty;
    [ObservableProperty] private int _count;

    public void SetItem(int itemId)
    {
        _item.Index = itemId;
        ItemName = NameOf(itemId);
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

    private string NameOf(int id) =>
        id == 0 ? "—" : (uint)id < _strings.itemlist.Length ? _strings.itemlist[id] : $"#{id}";
}
