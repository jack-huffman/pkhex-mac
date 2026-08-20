using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Technical Record / TM editor: which machine-taught moves this Pokémon has
/// learned. Only the records the species is actually permitted are listed.
/// </summary>
public partial class TechRecordViewModel : ObservableObject
{
    private readonly GameStrings _strings;
    private readonly Action _markDirty;
    private PKM? _pk;
    private readonly List<TechRecordRowViewModel> _all = [];
    private bool _loading;

    public TechRecordViewModel(GameStrings strings, Action markDirty)
    {
        _strings = strings;
        _markDirty = markDirty;
    }

    public ObservableCollection<TechRecordRowViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _isSupported;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _learnedOnly;
    [ObservableProperty] private string _summary = string.Empty;

    public void Load(PKM? pk)
    {
        _loading = true;
        try
        {
            _pk = pk;
            _all.Clear();
            Rows.Clear();
            IsSupported = pk is { Species: > 0 } and ITechRecord;
            if (pk is not ITechRecord record)
                return;

            var permit = record.Permit;
            var moves = permit.RecordPermitIndexes;
            for (int i = 0; i < permit.RecordCountUsed && i < moves.Length; i++)
            {
                // Skip records this species can never learn — they only add noise.
                if (!permit.IsRecordPermitted(i))
                    continue;
                var moveId = moves[i];
                var name = (uint)moveId < _strings.movelist.Length ? _strings.movelist[moveId] : $"Move #{moveId}";
                _all.Add(new TechRecordRowViewModel(this, i, name, record.GetMoveRecordFlag(i)));
            }
            _all.Sort((a, b) => string.CompareOrdinal(a.MoveName, b.MoveName));
        }
        finally
        {
            _loading = false;
        }
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnLearnedOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var query = SearchText.Trim();
        foreach (var row in _all)
        {
            if (LearnedOnly && !row.Learned)
                continue;
            if (query.Length != 0 && !row.MoveName.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Rows.Add(row);
        }
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var learned = _all.Count(r => r.Learned);
        Summary = _all.Count == 0
            ? string.Empty
            : $"{learned} of {_all.Count} records learned";
    }

    internal void Write(int index, bool value)
    {
        if (_loading || _pk is not ITechRecord record)
            return;
        record.SetMoveRecordFlag(index, value);
        RefreshSummary();
        _markDirty();
    }

    /// <summary>Marks every record this Pokémon could legally have learned.</summary>
    [RelayCommand]
    public void SetAllLegal()
    {
        if (_pk is not ITechRecord record || _pk is null)
            return;
        record.SetRecordFlags(_pk, TechnicalRecordApplicatorOption.LegalCurrent);
        Load(_pk);
        _markDirty();
    }

    [RelayCommand]
    public void SetAll()
    {
        if (_pk is not ITechRecord record)
            return;
        record.SetRecordFlagsAll();
        Load(_pk);
        _markDirty();
    }

    [RelayCommand]
    public void ClearAll()
    {
        if (_pk is not ITechRecord record)
            return;
        record.ClearRecordFlags();
        Load(_pk);
        _markDirty();
    }
}

/// <summary>One technical record: the move it teaches and whether it was used.</summary>
public partial class TechRecordRowViewModel : ObservableObject
{
    private readonly TechRecordViewModel _parent;
    private readonly int _index;
    private bool _loading;

    public TechRecordRowViewModel(TechRecordViewModel parent, int index, string moveName, bool learned)
    {
        _parent = parent;
        _index = index;
        MoveName = moveName;
        _loading = true;
        Learned = learned;
        _loading = false;
    }

    public string MoveName { get; }

    [ObservableProperty] private bool _learned;

    partial void OnLearnedChanged(bool value)
    {
        if (_loading)
            return;
        _parent.Write(_index, value);
    }
}
