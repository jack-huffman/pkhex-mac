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
/// The player's look and flourishes in Scarlet/Violet and Legends Z-A: ball-throwing
/// style, worn outfit, facial appearance, and the club perks that unlock them.
/// </summary>
/// <remarks>
/// Outfit slots hold opaque 64-bit item identifiers with no name table anywhere in
/// PKHeX, so they are edited as hex — the same as PKHeX's own property grid. Facial
/// appearance is different: those are small option indexes (skin colour 3, eye shape 6),
/// so they read as plain numbers. The valuable operations are the unlock commands,
/// which use PKHeX's item tables.
/// The field lists are discovered by reflection so an upstream addition shows up here
/// without a code change.
/// </remarks>
public partial class TrainerStyleViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly Action _onChanged;
    private readonly BlueberrySupportBoard9? _board;
    private bool _loading;

    public TrainerStyleViewModel(SaveFile sav, Action onChanged)
    {
        _sav = sav;
        _onChanged = onChanged;

        object? fashion = null;
        object? appearance = null;
        switch (sav)
        {
            case SAV9SV sv:
                IsSupported = true;
                HasThrowStyle = true;
                fashion = sv.PlayerFashion;
                appearance = sv.PlayerAppearance;
                _loading = true;
                ThrowStyleIndex = Math.Clamp((int)sv.ThrowStyle - 1, 0, ThrowStyleChoices.Count - 1);
                _loading = false;
                _board = sv.BlueberryClubRoom.SupportBoard;
                break;
            case SAV9ZA za:
                IsSupported = true;
                fashion = za.PlayerFashion;
                appearance = za.PlayerAppearance;
                break;
        }

        if (!IsSupported)
            return;

        // Outfit ids are hashes; appearance traits are small option indexes.
        AddFields(Outfit, fashion, hex: true);
        AddFields(Appearance, appearance, hex: false);
        if (_board is not null)
            AddPurchases(_board);

        // Club perks have their own tab, so they stay out of this summary.
        HasSupportBoard = ClubPurchases.Count > 0;
        Summary = $"{Outfit.Count} outfit slots · {Appearance.Count} appearance traits";
    }

    public bool IsSupported { get; }
    public bool HasThrowStyle { get; }
    public bool HasSupportBoard { get; }

    public ObservableCollection<StyleFieldViewModel> Outfit { get; } = [];
    public ObservableCollection<StyleFieldViewModel> Appearance { get; } = [];
    public ObservableCollection<StyleToggleViewModel> ClubPurchases { get; } = [];

    /// <summary>Names for <see cref="ThrowStyle9"/>, in its numeric order.</summary>
    public IReadOnlyList<string> ThrowStyleChoices { get; } =
    [
        "Original", "Left-handed", "Elegant", "Reverent", "Ninja",
        "Dainty", "Twirling", "Smug", "Galarian Star",
    ];

    [ObservableProperty] private int _throwStyleIndex;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    partial void OnThrowStyleIndexChanged(int value)
    {
        if (_loading || _sav is not SAV9SV sv || (uint)value >= (uint)ThrowStyleChoices.Count)
            return;
        sv.ThrowStyle = (ThrowStyle9)(value + 1);
        _onChanged();
    }

    /// <summary>
    /// Unlocks the three club-room throw styles and marks the support board entries
    /// bought, which is what makes the styles selectable in-game.
    /// </summary>
    [RelayCommand]
    public void UnlockThrowStyles()
    {
        if (_sav is not SAV9SV sv)
            return;
        sv.UnlockAllThrowStyles();
        RefreshPurchases();
        Status = "All nine throw styles unlocked.";
        _onChanged();
    }

    /// <summary>Adds every fashion item for the player's gender to the unlock lists.</summary>
    [RelayCommand]
    public void UnlockFashion()
    {
        if (_sav is not SAV9SV sv)
        {
            Status = "Fashion unlock tables only exist for Scarlet and Violet.";
            return;
        }
        var added = PlayerFashionUnlock9.UnlockBase(sv.Accessor, sv.Gender);
        Status = added == 0
            ? "Every fashion item was already unlocked."
            : $"Unlocked {added} fashion item{(added == 1 ? string.Empty : "s")} "
              + $"for the {(sv.Gender == 0 ? "male" : "female")} wardrobe.";
        _onChanged();
    }

    /// <summary>Marks every Blueberry Academy club perk as bought and already seen.</summary>
    [RelayCommand]
    public void UnlockClubPerks()
    {
        if (_board is null)
            return;
        foreach (var row in ClubPurchases)
        {
            // "Unread" is the new-item badge; buying without clearing it looks wrong in-game.
            row.Value = !row.IsUnreadFlag;
        }
        Status = "All club perks marked as purchased.";
        _onChanged();
    }

    private void RefreshPurchases()
    {
        foreach (var row in ClubPurchases)
            row.Reload();
    }

    /// <summary>Discovers the numeric slots on a fashion or appearance block.</summary>
    private void AddFields(ObservableCollection<StyleFieldViewModel> target, object? source, bool hex)
    {
        if (source is null)
            return;
        foreach (var prop in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite)
                continue;
            if (prop.PropertyType != typeof(ulong) && prop.PropertyType != typeof(uint))
                continue;
            target.Add(new StyleFieldViewModel(source, prop, hex, _onChanged));
        }
    }

    private void AddPurchases(BlueberrySupportBoard9 board)
    {
        foreach (var prop in typeof(BlueberrySupportBoard9).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(bool) || !prop.CanRead || !prop.CanWrite)
                continue;
            ClubPurchases.Add(new StyleToggleViewModel(board, prop, _onChanged));
        }
    }

    /// <summary>"BaseballClub1SmugElegantPurchased" → "Baseball club 1 smug elegant".</summary>
    internal static string Prettify(string name)
    {
        foreach (var suffix in new[] { "Purchased", "Unread" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                name = name[..^suffix.Length];
        }

        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var boundary = i > 0
                && (char.IsUpper(c) || char.IsDigit(c) != char.IsDigit(name[i - 1]))
                && !(char.IsUpper(c) && char.IsUpper(name[i - 1]));
            if (boundary)
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>One opaque outfit or appearance slot, edited as hex.</summary>
public partial class StyleFieldViewModel : ObservableObject
{
    private readonly object _owner;
    private readonly PropertyInfo _prop;
    private readonly Action _onChanged;
    private readonly bool _is64;
    private readonly bool _hex;
    private bool _loading;

    public StyleFieldViewModel(object owner, PropertyInfo prop, bool hex, Action onChanged)
    {
        _owner = owner;
        _prop = prop;
        _hex = hex;
        _onChanged = onChanged;
        _is64 = prop.PropertyType == typeof(ulong);
        Label = TrainerStyleViewModel.Prettify(prop.Name);
        _loading = true;
        HexText = Read();
        _loading = false;
    }

    public string Label { get; }

    /// <summary>Hex ids want a monospace field; small indexes read better proportional.</summary>
    public Avalonia.Media.FontFamily FieldFont =>
        _hex ? new Avalonia.Media.FontFamily("Menlo, monospace") : Avalonia.Media.FontFamily.Default;

    [ObservableProperty] private string _hexText = string.Empty;
    [ObservableProperty] private string _error = string.Empty;

    private ulong RawValue() => _is64 ? (ulong)_prop.GetValue(_owner)! : (uint)_prop.GetValue(_owner)!;

    private string Read()
    {
        var value = RawValue();
        if (!_hex)
            return value.ToString(CultureInfo.InvariantCulture);
        return _is64 ? $"{value:X16}" : $"{value:X8}";
    }

    partial void OnHexTextChanged(string value)
    {
        if (_loading)
            return;
        var text = value.Trim();
        var ci = CultureInfo.InvariantCulture;
        var style = _hex ? NumberStyles.HexNumber : NumberStyles.Integer;
        if (!ulong.TryParse(text, style, ci, out var parsed))
        {
            Error = _hex ? (_is64 ? "16 hex digits" : "8 hex digits") : "whole number";
            return;
        }
        if (!_is64 && parsed > uint.MaxValue)
        {
            Error = "too large";
            return;
        }
        _prop.SetValue(_owner, _is64 ? parsed : (uint)parsed);
        Error = string.Empty;
        _onChanged();
    }
}

/// <summary>One club-room purchase or "new" badge flag.</summary>
public partial class StyleToggleViewModel : ObservableObject
{
    private readonly object _owner;
    private readonly PropertyInfo _prop;
    private readonly Action _onChanged;
    private bool _loading;

    public StyleToggleViewModel(object owner, PropertyInfo prop, Action onChanged)
    {
        _owner = owner;
        _prop = prop;
        _onChanged = onChanged;
        IsUnreadFlag = prop.Name.EndsWith("Unread", StringComparison.Ordinal);
        Label = TrainerStyleViewModel.Prettify(prop.Name) + (IsUnreadFlag ? " (new badge)" : string.Empty);
        Reload();
    }

    public string Label { get; }

    /// <summary>"Unread" flags drive the in-game new-item badge, not ownership.</summary>
    public bool IsUnreadFlag { get; }

    [ObservableProperty] private bool _value;

    internal void Reload()
    {
        _loading = true;
        Value = (bool)_prop.GetValue(_owner)!;
        _loading = false;
    }

    partial void OnValueChanged(bool value)
    {
        if (_loading)
            return;
        _prop.SetValue(_owner, value);
        _onChanged();
    }
}
