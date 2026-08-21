using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Generic editor for the generation-specific save structures PKHeX exposes but that
/// have no dedicated screen here — Unity Tower, Global Link, Entralink, Musical,
/// Battle Subway, Pokéathlon, honey trees, Poffins, Zygarde cells and the rest.
/// </summary>
/// <remarks>
/// This is the Gen 1–7 counterpart to the Save Blocks editor: rather than a screen
/// per feature, it walks the substructures the save class publishes and edits their
/// scalar fields directly. That means an upstream addition shows up here for free,
/// which is the point — there are roughly forty of these little editors in PKHeX and
/// hand-porting each one would fall behind upstream immediately.
///
/// It is a power tool. Values are written straight through with no validation beyond
/// the type, exactly like the Save Blocks editor.
/// </remarks>
public partial class GameExtrasViewModel : ObservableObject
{
    /// <summary>Guards against a runaway walk on an unusual save layout.</summary>
    private const int MaxFields = 2000;

    /// <summary>Internals that would be noise or are edited properly elsewhere.</summary>
    private static readonly HashSet<string> SkipNames = new(StringComparer.Ordinal)
    {
        "Data", "Raw", "SAV", "Extra", "Offset", "BAK", "Zukan", "Items", "BoxLayout",
        "Records", "EventWork", "EventFlags", "Mystery", "Daycare",
        // Editor bookkeeping, not save contents.
        "Metadata", "State",
    };

    private readonly Action _onChanged;
    private readonly List<ExtrasGroupViewModel> _all = [];

    /// <summary>
    /// A save usually forwards its substructures from a block accessor, so the same
    /// object is reachable as both "PlayerData" and "Blocks.PlayerData". Identity
    /// tracking keeps the first (shorter) path and drops the alias.
    /// </summary>
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);

    public GameExtrasViewModel(SaveFile sav, Action onChanged)
    {
        _onChanged = onChanged;

        // The save itself, plus its block accessor when it has one.
        Collect(sav, string.Empty);
        if (GetProperty(sav, "Blocks") is { } blocks)
            Collect(blocks, "Blocks");

        IsSupported = _all.Count > 0;
        ApplyFilter();
    }

    public bool IsSupported { get; }
    public ObservableCollection<ExtrasGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _nonZeroOnly;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnNonZeroOnlyChanged(bool value) => ApplyFilter();

    private static object? GetProperty(object owner, string name)
    {
        try
        {
            return owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Turns each substructure the container publishes into a group of fields.</summary>
    private void Collect(object container, string prefix)
    {
        var fieldCount = _all.Sum(g => g.Fields.Count);
        foreach (var prop in container.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (fieldCount >= MaxFields)
                return;
            if (prop.GetIndexParameters().Length != 0 || !prop.CanRead)
                continue;
            if (SkipNames.Contains(prop.Name))
                continue;
            if (!IsSubstructure(prop.PropertyType))
                continue;

            object? value;
            try
            {
                value = prop.GetValue(container);
            }
            catch
            {
                continue; // a block this save does not have
            }
            if (value is null || !_seen.Add(value))
                continue;

            var name = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
            var group = new ExtrasGroupViewModel(Prettify(prop.Name), name, value, _onChanged);
            if (group.Fields.Count == 0)
                continue;
            _all.Add(group);
            fieldCount += group.Fields.Count;
        }
    }

    /// <summary>
    /// A substructure is a PKHeX.Core class that wraps part of the save. Value types,
    /// strings, collections and the entity types are not what we are after.
    /// </summary>
    private static bool IsSubstructure(Type type)
    {
        if (!type.IsClass || type == typeof(string) || type.IsArray)
            return false;
        if (type.Namespace is null || !type.Namespace.StartsWith("PKHeX.Core", StringComparison.Ordinal))
            return false;
        if (typeof(PKM).IsAssignableFrom(type) || typeof(SaveFile).IsAssignableFrom(type))
            return false;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return false;
        return true;
    }

    private void ApplyFilter()
    {
        Groups.Clear();
        var query = SearchText.Trim();
        foreach (var group in _all)
        {
            group.Filter(query, NonZeroOnly);
            if (group.Visible.Count != 0)
                Groups.Add(group);
        }
        var fields = _all.Sum(g => g.Fields.Count);
        var shown = Groups.Sum(g => g.Visible.Count);
        Summary = query.Length == 0 && !NonZeroOnly
            ? $"{_all.Count} structures · {fields} fields"
            : $"{shown} of {fields} fields in {Groups.Count} structures";
    }

    [RelayCommand]
    public void ClearFilters()
    {
        SearchText = string.Empty;
        NonZeroOnly = false;
    }

    /// <summary>"BattleSubwayPlay" → "Battle subway play".</summary>
    internal static string Prettify(string name)
    {
        var sb = new StringBuilder(name.Length + 6);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var boundary = i > 0
                && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(name[i - 1])))
                && !(char.IsUpper(c) && char.IsUpper(name[i - 1]));
            if (boundary)
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>One save substructure and its editable scalar fields.</summary>
public sealed class ExtrasGroupViewModel
{
    public ExtrasGroupViewModel(string label, string path, object target, Action onChanged)
    {
        Label = label;
        Path = path;
        TypeName = target.GetType().Name;

        foreach (var prop in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length != 0 || !prop.CanRead || !prop.CanWrite)
                continue;
            var kind = ExtrasFieldViewModel.Classify(prop.PropertyType);
            if (kind == ExtrasFieldKind.Unsupported)
                continue;
            var field = ExtrasFieldViewModel.TryCreate(target, prop, kind, onChanged);
            if (field is not null)
                Fields.Add(field);
        }
    }

    public string Label { get; }
    public string Path { get; }
    public string TypeName { get; }

    public List<ExtrasFieldViewModel> Fields { get; } = [];

    /// <summary>The subset matching the current filter.</summary>
    public ObservableCollection<ExtrasFieldViewModel> Visible { get; } = [];

    internal void Filter(string query, bool nonZeroOnly)
    {
        Visible.Clear();
        var groupMatches = query.Length == 0
                           || Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || TypeName.Contains(query, StringComparison.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            if (nonZeroOnly && field.IsDefaultValue)
                continue;
            if (query.Length != 0 && !groupMatches
                && !field.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Visible.Add(field);
        }
    }
}

public enum ExtrasFieldKind
{
    Unsupported,
    Boolean,
    Integer,
    Enumeration,
    Text,
}

/// <summary>One scalar field inside a save substructure.</summary>
public partial class ExtrasFieldViewModel : ObservableObject
{
    private readonly object _owner;
    private readonly PropertyInfo _prop;
    private readonly Action _onChanged;
    private bool _loading;

    private ExtrasFieldViewModel(object owner, PropertyInfo prop, ExtrasFieldKind kind, Action onChanged)
    {
        _owner = owner;
        _prop = prop;
        _onChanged = onChanged;
        Kind = kind;
        Label = GameExtrasViewModel.Prettify(prop.Name);
        TypeName = prop.PropertyType.Name;

        if (kind == ExtrasFieldKind.Enumeration)
            EnumNames = Enum.GetNames(prop.PropertyType);
    }

    /// <summary>Creates the field, or nothing if its getter refuses to run.</summary>
    internal static ExtrasFieldViewModel? TryCreate(object owner, PropertyInfo prop, ExtrasFieldKind kind, Action onChanged)
    {
        var field = new ExtrasFieldViewModel(owner, prop, kind, onChanged);
        return field.TryLoad() ? field : null;
    }

    public ExtrasFieldKind Kind { get; }
    public string Label { get; }
    public string TypeName { get; }
    public IReadOnlyList<string> EnumNames { get; } = [];

    public bool IsBoolean => Kind == ExtrasFieldKind.Boolean;
    public bool IsInteger => Kind == ExtrasFieldKind.Integer;
    public bool IsEnumeration => Kind == ExtrasFieldKind.Enumeration;
    public bool IsText => Kind == ExtrasFieldKind.Text;

    /// <summary>Used by the "only fields with a value" filter.</summary>
    public bool IsDefaultValue { get; private set; } = true;

    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private string _textValue = string.Empty;
    [ObservableProperty] private int _enumIndex;
    [ObservableProperty] private string _error = string.Empty;

    /// <summary>Drives the field's error styling; the text itself goes in a tooltip.</summary>
    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    internal static ExtrasFieldKind Classify(Type type)
    {
        if (type.IsEnum)
            return ExtrasFieldKind.Enumeration;
        if (type == typeof(bool))
            return ExtrasFieldKind.Boolean;
        if (type == typeof(string))
            return ExtrasFieldKind.Text;
        return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
               || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
               || type == typeof(long) || type == typeof(ulong)
            ? ExtrasFieldKind.Integer
            : ExtrasFieldKind.Unsupported;
    }

    private bool TryLoad()
    {
        try
        {
            _loading = true;
            var value = _prop.GetValue(_owner);
            switch (Kind)
            {
                case ExtrasFieldKind.Boolean:
                    BoolValue = (bool)value!;
                    IsDefaultValue = !BoolValue;
                    break;
                case ExtrasFieldKind.Enumeration:
                    var name = value?.ToString() ?? string.Empty;
                    EnumIndex = Math.Max(0, EnumNames.ToList().IndexOf(name));
                    IsDefaultValue = EnumIndex == 0;
                    break;
                case ExtrasFieldKind.Text:
                    TextValue = value as string ?? string.Empty;
                    IsDefaultValue = TextValue.Length == 0;
                    break;
                default:
                    TextValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                    IsDefaultValue = TextValue is "0";
                    break;
            }
            return true;
        }
        catch
        {
            return false; // a getter that throws on this save is simply not shown
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (_loading)
            return;
        Write(value);
    }

    partial void OnEnumIndexChanged(int value)
    {
        if (_loading || (uint)value >= (uint)EnumNames.Count)
            return;
        Write(Enum.Parse(_prop.PropertyType, EnumNames[value]));
    }

    partial void OnTextValueChanged(string value)
    {
        if (_loading)
            return;
        if (Kind == ExtrasFieldKind.Text)
        {
            Write(value);
            return;
        }
        if (!TryParseInteger(value.Trim(), out var parsed))
        {
            Error = $"not a valid {TypeName}";
            return;
        }
        Write(parsed);
    }

    private bool TryParseInteger(string text, out object result)
    {
        var ci = CultureInfo.InvariantCulture;
        var t = _prop.PropertyType;
        result = 0;
        if (t == typeof(byte) && byte.TryParse(text, ci, out var b)) { result = b; return true; }
        if (t == typeof(sbyte) && sbyte.TryParse(text, ci, out var sb)) { result = sb; return true; }
        if (t == typeof(short) && short.TryParse(text, ci, out var s)) { result = s; return true; }
        if (t == typeof(ushort) && ushort.TryParse(text, ci, out var us)) { result = us; return true; }
        if (t == typeof(int) && int.TryParse(text, ci, out var i)) { result = i; return true; }
        if (t == typeof(uint) && uint.TryParse(text, ci, out var ui)) { result = ui; return true; }
        if (t == typeof(long) && long.TryParse(text, ci, out var l)) { result = l; return true; }
        if (t == typeof(ulong) && ulong.TryParse(text, ci, out var ul)) { result = ul; return true; }
        return false;
    }

    private void Write(object value)
    {
        try
        {
            _prop.SetValue(_owner, value);
            Error = string.Empty;
            _onChanged();
        }
        catch (Exception ex)
        {
            // Some setters clamp or reject; say so rather than pretending it stuck.
            Error = ex.InnerException?.Message ?? ex.Message;
        }
    }
}
