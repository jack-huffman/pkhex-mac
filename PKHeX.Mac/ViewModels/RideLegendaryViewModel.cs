using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The ride legendary as a Pokémon, read from the reserved box slot the game keeps it
/// in. See <see cref="RideLegendary"/> for why nothing else reaches it.
/// </summary>
public partial class RideLegendaryViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly Action<string> _onChanged;

    public RideLegendaryViewModel(SaveFile sav, GameStrings strings, Action<string> onChanged)
    {
        _sav = sav;
        _strings = strings;
        _onChanged = onChanged;
        IsSupported = RideLegendary.IsSupported(sav);
        Reload();
    }

    public bool IsSupported { get; }

    /// <summary>Reads the Pokémon currently in the selected box slot.</summary>
    public Func<PKM?>? ReadSelectedSlot { get; set; }

    /// <summary>Writes a Pokémon into the selected box slot.</summary>
    public Action<PKM>? WriteSelectedSlot { get; set; }

    [ObservableProperty] private Bitmap? _sprite;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private string _origin = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private bool _isLegal;
    [ObservableProperty] private string _status = string.Empty;

    public PKM? Pokemon { get; private set; }

    private void Reload()
    {
        var pk = RideLegendary.Read(_sav);
        Pokemon = pk;
        if (pk is null)
        {
            Sprite = null;
            SpeciesName = "Nothing stored";
            Summary = IsSupported
                ? "The reserved slot is empty — the ride has not been obtained yet."
                : "Only Scarlet and Violet keep a ride legendary.";
            Detail = string.Empty;
            Origin = string.Empty;
            IsEmpty = true;
            IsShiny = false;
            IsLegal = false;
            return;
        }

        IsEmpty = false;
        IsShiny = pk.IsShiny;
        SpeciesName = (uint)pk.Species < _strings.specieslist.Length
            ? _strings.specieslist[pk.Species]
            : $"#{pk.Species}";
        Summary = $"Lv. {pk.CurrentLevel} · "
                  + ((uint)pk.Nature < _strings.natures.Length ? _strings.natures[(int)pk.Nature] : pk.Nature.ToString())
                  + " · " + ((uint)pk.Ability < _strings.abilitylist.Length ? _strings.abilitylist[pk.Ability] : $"#{pk.Ability}");
        Detail = $"IV {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
        var ball = (uint)pk.Ball < _strings.balllist.Length ? _strings.balllist[pk.Ball] : $"ball {pk.Ball}";
        Origin = $"{ball} · met Lv. {pk.MetLevel} · OT {pk.OriginalTrainerName}";
        Sprite = SpriteService.GetPokemonSprite(pk);
        IsLegal = SafeLegality(pk);
    }

    private static bool SafeLegality(PKM pk)
    {
        try
        {
            return new LegalityAnalysis(pk).Valid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the ride into the selected box slot so the full inspector can edit it.
    /// The ride itself is left in place until it is put back.
    /// </summary>
    [RelayCommand]
    public void CopyToBox()
    {
        if (Pokemon is not { } pk)
        {
            Status = "There is nothing stored to copy.";
            return;
        }
        if (WriteSelectedSlot is null)
        {
            Status = "Select an empty box slot first.";
            return;
        }
        WriteSelectedSlot(pk.Clone());
        Status = $"Copied {SpeciesName} into the selected box slot. Edit it there, then put it back.";
        _onChanged($"Copied the ride {SpeciesName} into a box slot");
    }

    /// <summary>Writes the Pokémon in the selected box slot back over the ride.</summary>
    [RelayCommand]
    public void TakeFromBox()
    {
        if (ReadSelectedSlot?.Invoke() is not { Species: > 0 } pk)
        {
            Status = "Select a box slot holding the Pokémon you want to store as the ride.";
            return;
        }
        if (pk.Species is not ((ushort)Species.Koraidon or (ushort)Species.Miraidon))
        {
            // The game only ever renders its own legendary here; anything else would
            // leave the party screen showing something the engine cannot mount.
            Status = "Only Koraidon or Miraidon belongs in this slot.";
            return;
        }
        if (!RideLegendary.Write(_sav, pk.Clone()))
        {
            Status = "Could not write to the reserved slot.";
            return;
        }
        Reload();
        Status = $"Stored {SpeciesName} as the ride.";
        _onChanged($"Replaced the ride legendary with {SpeciesName}");
    }
}
