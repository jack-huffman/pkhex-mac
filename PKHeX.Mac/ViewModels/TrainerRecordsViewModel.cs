using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Lifetime trainer stat records — eggs hatched, battles won, steps taken and the
/// rest — for the saves that keep them (Gen 6, Gen 7, BDSP and Sword/Shield).
/// </summary>
/// <remarks>
/// PKHeX stores the record names as internal snake_case keys rather than display
/// strings, so they are prettified here. Each record has its own ceiling, which the
/// save clamps to on write; the ceiling is shown so a value that refuses to grow
/// makes sense.
/// </remarks>
public partial class TrainerRecordsViewModel : ObservableObject
{
    private readonly ITrainerStatRecord? _record;
    private readonly Action _onChanged;
    private readonly List<RecordRowViewModel> _all = [];

    public TrainerRecordsViewModel(SaveFile sav, Action onChanged)
    {
        _onChanged = onChanged;
        if (sav is not ITrainerStatRecord record)
            return;

        _record = record;
        IsSupported = true;
        var names = GetNames(sav);

        for (int i = 0; i < record.RecordCount; i++)
        {
            var label = names.TryGetValue(i, out var key) ? Prettify(key) : $"Record {i}";
            _all.Add(new RecordRowViewModel(record, i, label, onChanged));
        }
        ApplyFilter();
    }

    /// <summary>The record name table matching this save's game.</summary>
    private static Dictionary<int, string> GetNames(SaveFile sav) => sav switch
    {
        SAV8SWSH => RecordLists.RecordList_8,
        SAV8BS => Record8b.RecordList_8b,
        { Generation: 7 } => RecordLists.RecordList_7,
        { Generation: 6 } => RecordLists.RecordList_6,
        { Generation: 5 } => RecordLists.RecordList_5,
        _ => [],
    };

    public bool IsSupported { get; }
    public ObservableCollection<RecordRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _nonZeroOnly;
    [ObservableProperty] private string _summary = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnNonZeroOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var query = SearchText.Trim();
        foreach (var row in _all)
        {
            if (NonZeroOnly && row.Value == 0)
                continue;
            if (query.Length != 0 && !row.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Rows.Add(row);
        }
        var set = _all.Count(r => r.Value != 0);
        Summary = $"{_all.Count} records · {set} with a value";
    }

    /// <summary>Zeroes every record — the clean-slate option PKHeX offers.</summary>
    [RelayCommand]
    public void ClearAll()
    {
        foreach (var row in _all.Where(r => r.Value != 0))
            row.Value = 0;
        ApplyFilter();
        _onChanged();
    }

    /// <summary>"total_capture" → "Total capture".</summary>
    internal static string Prettify(string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(key.Length + 2);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i != 0)
                sb.Append(' ');
            var part = parts[i];
            sb.Append(i == 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part);
        }
        return sb.ToString();
    }
}

/// <summary>One lifetime record, with the ceiling the save will clamp it to.</summary>
public partial class RecordRowViewModel : ObservableObject
{
    private readonly ITrainerStatRecord _record;
    private readonly int _id;
    private readonly Action _onChanged;
    private bool _loading;

    public RecordRowViewModel(ITrainerStatRecord record, int id, string label, Action onChanged)
    {
        _record = record;
        _id = id;
        _onChanged = onChanged;
        Label = label;
        Max = record.GetRecordMax(id);
        MaxText = Max > 0 ? $"max {Max:N0}" : string.Empty;
        _loading = true;
        Value = record.GetRecord(id);
        _loading = false;
    }

    public string Label { get; }
    public int Max { get; }
    public string MaxText { get; }
    public string IdText => $"#{_id}";

    [ObservableProperty] private int _value;

    partial void OnValueChanged(int value)
    {
        if (_loading)
            return;
        _record.SetRecord(_id, value);
        // The save clamps to the record's own ceiling; show what it actually stored.
        var stored = _record.GetRecord(_id);
        if (stored != value)
        {
            _loading = true;
            Value = stored;
            _loading = false;
        }
        _onChanged();
    }
}
