using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Raw editor for the SCBlock save structure used by Sword/Shield, BDSP, Legends
/// and Scarlet/Violet. Replaces the flat event-flag array those games no longer have.
/// </summary>
/// <remarks>
/// Every block is keyed by a 32-bit hash of its internal name. PKHeX knows the names
/// for the blocks it has mapped (roughly a fifth of them in Scarlet/Violet); the rest
/// show as bare hex keys. There is no validation here — a block is whatever the game
/// says it is — so this is a power tool, not a guided editor.
/// </remarks>
public partial class SaveBlocksViewModel : ObservableObject
{
    private const int DisplayCap = 400;

    private readonly SaveFile _sav;
    private readonly Action _onChanged;
    private readonly List<ScBlockRowViewModel> _all = [];

    public SaveBlocksViewModel(SaveFile sav, Action onChanged)
    {
        _sav = sav;
        _onChanged = onChanged;
        IsSupported = sav is ISCBlockArray;
        if (sav is not ISCBlockArray array)
            return;

        // Friendly names come from the block accessor PKHeX ships per game.
        SCBlockMetadata? meta = null;
        try
        {
            if (sav is SAV9SV sv)
                meta = new SCBlockMetadata(sv.Blocks, [], []);
            else if (sav is SAV8SWSH swsh)
                meta = new SCBlockMetadata(swsh.Blocks, [], []);
            else if (sav is SAV8LA la)
                meta = new SCBlockMetadata(la.Blocks, [], []);
            else if (sav is SAV9ZA za)
                meta = new SCBlockMetadata(za.Blocks, [], []);
        }
        catch
        {
            meta = null; // names are a nicety; the editor still works without them
        }

        foreach (var block in array.AllBlocks)
        {
            string? name = null;
            try
            {
                name = meta?.GetBlockName(block, out _);
            }
            catch
            {
                // ignore naming failures for individual blocks
            }
            _all.Add(new ScBlockRowViewModel(this, block, name));
        }

        NamedCount = _all.Count(r => r.HasName);
        ApplyFilter();
    }

    public bool IsSupported { get; }
    public int NamedCount { get; }
    public ObservableCollection<ScBlockRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _namedOnly = true;
    [ObservableProperty] private bool _editableOnly = true;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _capNotice = string.Empty;
    [ObservableProperty] private ScBlockRowViewModel? _selectedRow;
    [ObservableProperty] private string _ioResult = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnNamedOnlyChanged(bool value) => ApplyFilter();
    partial void OnEditableOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var query = SearchText.Trim();
        var matches = _all.Where(r =>
            (!NamedOnly || r.HasName)
            && (!EditableOnly || r.IsEditable)
            && (query.Length == 0 || r.Matches(query))).ToList();

        foreach (var row in matches.Take(DisplayCap))
            Rows.Add(row);

        Summary = $"{matches.Count} of {_all.Count} blocks · {NamedCount} have known names";
        CapNotice = matches.Count > DisplayCap
            ? $"Showing the first {DisplayCap} — refine the search to see more."
            : string.Empty;
    }

    internal void NotifyChanged() => _onChanged();

    /// <summary>Raw bytes of the selected block, for writing to a file.</summary>
    public byte[]? GetSelectedBytes() => SelectedRow?.RawBytes();

    public string SelectedName => SelectedRow is { } r ? $"{r.Name} ({r.KeyText})" : string.Empty;

    /// <summary>
    /// Replaces the selected block's contents from a file. The length must match —
    /// a block's size is fixed by the game, so a different size means the wrong file.
    /// </summary>
    public bool ImportSelectedBytes(byte[] data, out string message)
    {
        if (SelectedRow is not { } row)
        {
            message = "Select a block first.";
            return false;
        }
        var expected = row.ByteLength;
        if (data.Length != expected)
        {
            message = $"That file is {data.Length} bytes but this block holds {expected}. " +
                      "Blocks are fixed size, so this is almost certainly the wrong file.";
            return false;
        }
        row.Overwrite(data);
        _onChanged();
        message = $"Restored {expected} bytes into {row.Name}.";
        IoResult = message;
        return true;
    }

    [RelayCommand]
    public void ClearFilters()
    {
        SearchText = string.Empty;
        NamedOnly = true;
        EditableOnly = true;
    }
}

/// <summary>One save block: its name (when known), type, and editable value.</summary>
public partial class ScBlockRowViewModel : ObservableObject
{
    private readonly SaveBlocksViewModel _parent;
    private readonly SCBlock _block;
    private bool _loading;

    public ScBlockRowViewModel(SaveBlocksViewModel parent, SCBlock block, string? name)
    {
        _parent = parent;
        _block = block;
        HasName = !string.IsNullOrWhiteSpace(name);
        Name = HasName ? name! : $"(unnamed)";
        KeyText = $"{block.Key:X8}";
        TypeName = block.Type.ToString();

        IsBoolean = block.Type.IsBoolean();
        IsScalar = block.HasValue();
        IsOpaque = !IsBoolean && !IsScalar;
        ByteLength = block.Data.Length;
        OpaqueText = IsOpaque ? $"{ByteLength} bytes" : string.Empty;

        _loading = true;
        if (IsBoolean)
            BoolValue = block.Type == SCTypeCode.Bool2;
        else if (IsScalar)
            TextValue = block.GetValue().ToString() ?? "0";
        _loading = false;
    }

    public bool HasName { get; }
    public string Name { get; }
    public string KeyText { get; }
    public string TypeName { get; }
    public bool IsBoolean { get; }
    public bool IsScalar { get; }
    public bool IsOpaque { get; }
    public int ByteLength { get; }
    public string OpaqueText { get; }

    /// <summary>Objects and arrays have no single value to type into.</summary>
    public bool IsEditable => IsBoolean || IsScalar;

    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private string _textValue = string.Empty;
    [ObservableProperty] private string _error = string.Empty;

    /// <summary>A copy of the block's raw bytes.</summary>
    internal byte[] RawBytes() => _block.Data.ToArray();

    /// <summary>Writes raw bytes back into the block and refreshes the displayed value.</summary>
    internal void Overwrite(byte[] data)
    {
        data.CopyTo(_block.Data);
        _loading = true;
        if (IsBoolean)
            BoolValue = _block.Type == SCTypeCode.Bool2;
        else if (IsScalar)
            TextValue = _block.GetValue().ToString() ?? "0";
        _loading = false;
    }

    public bool Matches(string query) =>
        Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || KeyText.Contains(query, StringComparison.OrdinalIgnoreCase);

    partial void OnBoolValueChanged(bool value)
    {
        if (_loading)
            return;
        // A boolean block carries its value in its *type*, not its data.
        _block.ChangeBooleanType(value ? SCTypeCode.Bool2 : SCTypeCode.Bool1);
        _parent.NotifyChanged();
    }

    partial void OnTextValueChanged(string value)
    {
        if (_loading || !IsScalar)
            return;
        if (!TryParse(value, out var parsed))
        {
            Error = $"Not a valid {TypeName}";
            return;
        }
        Error = string.Empty;
        _block.SetValue(parsed);
        _parent.NotifyChanged();
    }

    /// <summary>Parses text into the block's exact stored type.</summary>
    private bool TryParse(string text, out object result)
    {
        text = text.Trim();
        var ci = CultureInfo.InvariantCulture;
        switch (_block.Type)
        {
            case SCTypeCode.Byte when byte.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.UInt16 when ushort.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.UInt32 when uint.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.UInt64 when ulong.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.SByte when sbyte.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.Int16 when short.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.Int32 when int.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.Int64 when long.TryParse(text, out var v): result = v; return true;
            case SCTypeCode.Single when float.TryParse(text, NumberStyles.Float, ci, out var v): result = v; return true;
            case SCTypeCode.Double when double.TryParse(text, NumberStyles.Float, ci, out var v): result = v; return true;
            default: result = 0; return false;
        }
    }
}
