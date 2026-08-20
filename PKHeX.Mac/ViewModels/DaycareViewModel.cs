using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The daycare: which Pokémon are boarded, how much experience they have banked,
/// whether an egg is waiting, and the RNG seed the egg will be generated from.
/// </summary>
/// <remarks>
/// Gen 1 through 7 plus BDSP keep a daycare; Sword/Shield and Scarlet/Violet do not
/// expose one. Omega Ruby and Alpha Sapphire have two, so the units are a list.
/// Parents are edited by moving them into a box and back, which keeps the full
/// inspector available without giving the box grid a third slot kind to reason about.
/// </remarks>
public partial class DaycareViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly Action _onChanged;

    public DaycareViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _sav = sav;
        _strings = strings;
        _onChanged = onChanged;

        if (sav is IDaycareMulti multi)
        {
            for (int i = 0; i < multi.DaycareCount; i++)
                Units.Add(new DaycareUnitViewModel(this, sav, strings, multi[i], $"Daycare {i + 1}"));
        }
        else if (sav is IDaycareStorage single)
        {
            Units.Add(new DaycareUnitViewModel(this, sav, strings, single, "Daycare"));
        }

        IsSupported = Units.Count > 0;
        if (!IsSupported)
            return;
        SelectedUnit = Units[0];

        // Egg readiness and the egg's RNG seed live on the save, not per unit.
        if (sav is IDaycareEggState egg)
        {
            HasEggState = true;
            _loading = true;
            EggAvailable = egg.IsEggAvailable;
            _loading = false;
        }

        SeedDigits = SeedWidth(sav);
        HasSeed = SeedDigits > 0;
        if (HasSeed)
        {
            _loading = true;
            SeedText = ReadSeed();
            _loading = false;
        }
    }

    private bool _loading;

    public bool IsSupported { get; }
    public bool HasEggState { get; }
    public bool HasSeed { get; }

    /// <summary>Hex digits in this game's daycare seed; 0 when it has none.</summary>
    public int SeedDigits { get; }

    public ObservableCollection<DaycareUnitViewModel> Units { get; } = [];

    [ObservableProperty] private DaycareUnitViewModel? _selectedUnit;
    [ObservableProperty] private bool _eggAvailable;
    [ObservableProperty] private string _seedText = string.Empty;
    [ObservableProperty] private string _seedError = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public bool HasMultipleUnits => Units.Count > 1;

    /// <summary>Reads the Pokémon in the box slot the user has selected.</summary>
    public Func<PKM?>? ReadSelectedSlot { get; set; }

    /// <summary>Writes a Pokémon into the box slot the user has selected.</summary>
    public Action<PKM>? WriteSelectedSlot { get; set; }

    internal void Notify(string message)
    {
        Status = message;
        _onChanged();
    }

    partial void OnEggAvailableChanged(bool value)
    {
        if (_loading || _sav is not IDaycareEggState egg)
            return;
        egg.IsEggAvailable = value;
        Notify(value ? "An egg is now waiting at the daycare." : "Cleared the waiting egg.");
    }

    partial void OnSeedTextChanged(string value)
    {
        if (_loading || !HasSeed)
            return;
        if (!TryWriteSeed(value.Trim()))
        {
            SeedError = $"{SeedDigits} hex digits";
            return;
        }
        SeedError = string.Empty;
        Notify("Daycare seed updated.");
    }

    // The seed is a differently sized integer per generation, and each save
    // implements the interface explicitly, so it has to be reached by cast.
    private static int SeedWidth(SaveFile sav) => sav switch
    {
        IDaycareRandomState<UInt128> => 32,
        IDaycareRandomState<ulong> => 16,
        IDaycareRandomState<uint> => 8,
        _ => 0,
    };

    private string ReadSeed() => _sav switch
    {
        IDaycareRandomState<UInt128> s => s.Seed.ToString("X32", CultureInfo.InvariantCulture),
        IDaycareRandomState<ulong> s => s.Seed.ToString("X16", CultureInfo.InvariantCulture),
        IDaycareRandomState<uint> s => s.Seed.ToString("X8", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private bool TryWriteSeed(string text)
    {
        var ci = CultureInfo.InvariantCulture;
        switch (_sav)
        {
            case IDaycareRandomState<UInt128> s when UInt128.TryParse(text, NumberStyles.HexNumber, ci, out var v):
                s.Seed = v;
                return true;
            case IDaycareRandomState<ulong> s when ulong.TryParse(text, NumberStyles.HexNumber, ci, out var v):
                s.Seed = v;
                return true;
            case IDaycareRandomState<uint> s when uint.TryParse(text, NumberStyles.HexNumber, ci, out var v):
                s.Seed = v;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>One daycare — ORAS has two, everything else has one.</summary>
public partial class DaycareUnitViewModel : ObservableObject
{
    public DaycareUnitViewModel(DaycareViewModel parent, SaveFile sav, GameStrings strings,
                               IDaycareStorage storage, string name)
    {
        Name = name;
        for (int i = 0; i < storage.DaycareSlotCount; i++)
            Parents.Add(new DaycareSlotViewModel(parent, sav, strings, storage, i));
    }

    public string Name { get; }
    public ObservableCollection<DaycareSlotViewModel> Parents { get; } = [];

    public override string ToString() => Name;
}

/// <summary>One boarded parent: its sprite, banked experience, and transfers.</summary>
public partial class DaycareSlotViewModel : ObservableObject
{
    private readonly DaycareViewModel _parent;
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly IDaycareStorage _storage;
    private readonly int _index;
    private bool _loading;

    public DaycareSlotViewModel(DaycareViewModel parent, SaveFile sav, GameStrings strings,
                               IDaycareStorage storage, int index)
    {
        _parent = parent;
        _sav = sav;
        _strings = strings;
        _storage = storage;
        _index = index;
        Label = $"Parent {index + 1}";
        HasExperience = sav is IDaycareExperience;
        Reload();
    }

    public string Label { get; }

    /// <summary>Only some generations bank experience while a Pokémon is boarded.</summary>
    public bool HasExperience { get; }

    [ObservableProperty] private Bitmap? _sprite;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _occupied;
    [ObservableProperty] private int _experience;

    /// <summary>The boarded Pokémon, decoded from the daycare's slot memory.</summary>
    public PKM? Pokemon { get; private set; }

    internal void Reload()
    {
        _loading = true;
        var slot = _storage.GetDaycareSlot(_index);
        var pk = _sav.GetStoredSlot(slot.Span);
        Pokemon = pk.Species == 0 ? null : pk;
        Occupied = _storage.IsDaycareOccupied(_index);

        if (Pokemon is null)
        {
            Sprite = null;
            SpeciesName = "Empty";
            DetailText = "No Pokémon boarded";
            IsEmpty = true;
            IsShiny = false;
        }
        else
        {
            IsEmpty = false;
            IsShiny = pk.IsShiny;
            Sprite = SpriteService.GetPokemonSprite(pk);
            SpeciesName = (uint)pk.Species < _strings.specieslist.Length
                ? _strings.specieslist[pk.Species]
                : $"#{pk.Species}";
            var nature = (uint)pk.Nature < _strings.natures.Length ? _strings.natures[(int)pk.Nature] : string.Empty;
            var gender = pk.Gender switch { 0 => "♂", 1 => "♀", _ => string.Empty };
            DetailText = $"Lv. {pk.CurrentLevel} · {nature} {gender}".TrimEnd();
        }

        if (HasExperience && _sav is IDaycareExperience exp)
            Experience = (int)Math.Min(int.MaxValue, exp.GetDaycareEXP(_index));

        _loading = false;
    }

    partial void OnOccupiedChanged(bool value)
    {
        if (_loading)
            return;
        _storage.SetDaycareOccupied(_index, value);
        _parent.Notify(value
            ? $"{Label} marked as boarded."
            : $"{Label} marked as not boarded. The Pokémon's data is still stored.");
    }

    partial void OnExperienceChanged(int value)
    {
        if (_loading || _sav is not IDaycareExperience exp)
            return;
        exp.SetDaycareEXP(_index, (uint)Math.Max(0, value));
        _parent.Notify($"{Label} banked experience set to {value:N0}.");
    }

    /// <summary>Copies the boarded Pokémon into the selected box slot, leaving it here.</summary>
    [RelayCommand]
    public void CopyToBox()
    {
        if (Pokemon is not { } pk)
        {
            _parent.Status = "This daycare slot is empty.";
            return;
        }
        if (_parent.WriteSelectedSlot is null)
        {
            _parent.Status = "Select an empty box slot first.";
            return;
        }
        _parent.WriteSelectedSlot(pk.Clone());
        _parent.Notify($"Copied {SpeciesName} into the selected box slot.");
    }

    /// <summary>Boards the Pokémon currently in the selected box slot.</summary>
    [RelayCommand]
    public void TakeFromBox()
    {
        if (_parent.ReadSelectedSlot?.Invoke() is not { } pk || pk.Species == 0)
        {
            _parent.Status = "Select a box slot holding a Pokémon first.";
            return;
        }
        var converted = pk;
        if (pk.GetType() != _sav.PKMType)
        {
            converted = EntityConverter.ConvertToType(pk, _sav.PKMType, out var result)!;
            if (converted is null)
            {
                _parent.Status = $"That Pokémon cannot be converted for this save ({result}).";
                return;
            }
        }
        converted.RefreshChecksum();
        var slot = _storage.GetDaycareSlot(_index);
        _sav.SetSlotFormatStored(converted, slot.Span, EntityImportSettings.None);
        _storage.SetDaycareOccupied(_index, true);
        Reload();
        _parent.Notify($"Boarded {SpeciesName} in {Label}.");
    }

    /// <summary>Empties the slot outright.</summary>
    [RelayCommand]
    public void ClearSlot()
    {
        var slot = _storage.GetDaycareSlot(_index);
        _sav.SetSlotFormatStored(_sav.BlankPKM, slot.Span, EntityImportSettings.None);
        _storage.SetDaycareOccupied(_index, false);
        Reload();
        _parent.Notify($"Cleared {Label}.");
    }
}
