using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Pokémon held in the fusion slots — the half that is set aside while a fusion is
/// active.
/// </summary>
/// <remarks>
/// Fusing Necrozma with Solgaleo, Kyurem with Reshiram or Calyrex with its steed parks
/// the donor in a dedicated block rather than a box, so it is invisible to every normal
/// view. It is a fully-formed entity with its own IVs, moves and history, and without
/// something like this the only way to get it back is to unfuse in-game.
/// </remarks>
public partial class FusionViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly Action<string> _onChanged;

    public FusionViewModel(SaveFile sav, GameStrings strings, Action<string> onChanged)
    {
        _sav = sav;
        _onChanged = onChanged;

        // The keys are public constants, unlike most of the block set.
        var slots = sav switch
        {
            SAV9SV => new (uint Key, string Label)[]
            {
                (SaveBlockAccessor9SV.KFusedNecrozmaS, "Necrozma · Solgaleo"),
                (SaveBlockAccessor9SV.KFusedNecrozmaM, "Necrozma · Lunala"),
                (SaveBlockAccessor9SV.KFusedKyurem, "Kyurem"),
                (SaveBlockAccessor9SV.KFusedCalyrex, "Calyrex"),
            },
            SAV8SWSH => [(SaveBlockAccessor8SWSH.KFusedCalyrex, "Calyrex")],
            _ => [],
        };

        if (sav is not ISCBlockArray array)
            return;
        foreach (var (key, label) in slots)
        {
            if (array.Accessor.TryGetBlock(key, out var block))
                Slots.Add(new FusionSlotViewModel(this, sav, strings, block, label));
        }
        IsSupported = Slots.Count > 0;
        RefreshSummary();
    }

    public bool IsSupported { get; }
    public ObservableCollection<FusionSlotViewModel> Slots { get; } = [];

    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Set by the window: writes a Pokémon into the selected box slot.</summary>
    public Action<PKM>? WriteSelectedSlot { get; set; }

    internal void RefreshSummary()
    {
        var used = 0;
        foreach (var slot in Slots)
        {
            if (!slot.IsEmpty)
                used++;
        }
        Summary = used == 0
            ? $"No fusion is active — all {Slots.Count} slots are empty"
            : $"{used} of {Slots.Count} slots hold a Pokémon";
    }

    internal void Notify(string message)
    {
        Status = message;
        RefreshSummary();
        _onChanged(message);
    }
}

/// <summary>One fusion slot and whatever is parked in it.</summary>
public partial class FusionSlotViewModel : ObservableObject
{
    private readonly FusionViewModel _parent;
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly SCBlock _block;

    public FusionSlotViewModel(FusionViewModel parent, SaveFile sav, GameStrings strings,
                              SCBlock block, string label)
    {
        _parent = parent;
        _sav = sav;
        _strings = strings;
        _block = block;
        Label = label;
        Reload();
    }

    public string Label { get; }

    [ObservableProperty] private Bitmap? _sprite;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isShiny;

    /// <summary>The parked Pokémon, or null when the slot is empty.</summary>
    public PKM? Pokemon { get; private set; }

    internal void Reload()
    {
        var pk = Read();
        Pokemon = pk is { Species: > 0 } ? pk : null;
        if (Pokemon is null)
        {
            Sprite = null;
            SpeciesName = "Empty";
            DetailText = "Nothing parked here";
            IsEmpty = true;
            IsShiny = false;
            return;
        }

        IsEmpty = false;
        IsShiny = Pokemon.IsShiny;
        SpeciesName = _strings.SpeciesName(Pokemon);
        DetailText = $"Lv. {Pokemon.CurrentLevel} · "
                     + $"IV {Pokemon.IV_HP}/{Pokemon.IV_ATK}/{Pokemon.IV_DEF}"
                     + $"/{Pokemon.IV_SPA}/{Pokemon.IV_SPD}/{Pokemon.IV_SPE}";
        Sprite = SpriteService.GetPokemonSprite(Pokemon);
    }

    /// <summary>Decodes the block, which holds one entity in party format.</summary>
    private PKM? Read()
    {
        try
        {
            var raw = _block.Data.ToArray();
            if (raw.Length < _sav.SIZE_PARTY)
                return null;
            return _sav.GetDecryptedPKM(raw.AsMemory(0, _sav.SIZE_PARTY));
        }
        catch (ArgumentException)
        {
            return null; // a block that is not entity-shaped holds nothing to show
        }
    }

    /// <summary>
    /// Copies the parked Pokémon into the selected box slot, leaving the fusion intact.
    /// Extracting rather than moving keeps the save consistent: clearing the slot while
    /// the fusion is still active would strand the fused form.
    /// </summary>
    [RelayCommand]
    public void CopyToBox()
    {
        if (Pokemon is not { } pk)
        {
            _parent.Status = "This fusion slot is empty.";
            return;
        }
        if (_parent.WriteSelectedSlot is null)
        {
            _parent.Status = "Select an empty box slot first.";
            return;
        }
        _parent.WriteSelectedSlot(pk.Clone());
        _parent.Notify($"Copied {SpeciesName} out of the {Label} slot into the selected box slot.");
    }
}
