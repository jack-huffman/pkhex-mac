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

/// <summary>
/// The save's Mystery Gift album — the wondercards it is holding, and the
/// received-flags that stop a gift being claimed twice.
/// </summary>
/// <remarks>
/// This is the other half of the gift story: the Mystery Gift database converts a
/// gift into a Pokémon, while this writes the card itself into the save so the game
/// can hand it over in-game. Gen 4 through 7 and Let's Go keep an album; Gen 8 and 9
/// replaced it with server-delivered records, which is why Sword/Shield and
/// Scarlet/Violet cannot do this.
/// </remarks>
public partial class GiftAlbumViewModel : ObservableObject
{
    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly Action _onChanged;
    private readonly IMysteryGiftStorage? _album;
    private readonly IMysteryGiftFlags? _flags;
    private List<MysteryGift> _candidates = [];

    public GiftAlbumViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _sav = sav;
        _strings = strings;
        _onChanged = onChanged;

        if (sav is not IMysteryGiftStorageProvider provider)
            return;
        _album = provider.MysteryGiftStorage;
        _flags = sav as IMysteryGiftFlags;
        IsSupported = true;
        HasFlags = _flags is { MysteryGiftReceivedFlagMax: > 0 };

        LoadCards();
        BuildCandidates();
    }

    public bool IsSupported { get; }
    public bool HasFlags { get; }

    public ObservableCollection<GiftCardViewModel> Cards { get; } = [];

    /// <summary>Gifts from the archive that fit this save's card format.</summary>
    public ObservableCollection<GiftCandidate> Candidates { get; } = [];

    [ObservableProperty] private GiftCardViewModel? _selectedCard;
    [ObservableProperty] private GiftCandidate? _selectedCandidate;
    [ObservableProperty] private string _candidateFilter = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    partial void OnCandidateFilterChanged(string value) => ApplyCandidateFilter();

    private void LoadCards()
    {
        Cards.Clear();
        if (_album is null)
            return;
        for (int i = 0; i < _album.GiftCountMax; i++)
        {
            DataMysteryGift? gift = null;
            try
            {
                gift = _album.GetMysteryGift(i);
            }
            catch
            {
                // A malformed card slot reads as empty rather than taking down the view.
            }
            Cards.Add(new GiftCardViewModel(this, i, gift, _strings));
        }
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var used = Cards.Count(c => !c.IsEmpty);
        var received = HasFlags && _flags is not null
            ? Enumerable.Range(0, _flags.MysteryGiftReceivedFlagMax).Count(_flags.GetMysteryGiftReceivedFlag)
            : 0;
        Summary = $"{used} of {Cards.Count} card slots filled"
                  + (HasFlags ? $" · {received} received flags set" : string.Empty);
    }

    /// <summary>
    /// The archive holds every gift ever distributed; only those whose card type
    /// matches this save can be written into its album.
    /// </summary>
    private void BuildCandidates()
    {
        if (_album is null)
            return;
        // The album's own slot type tells us exactly what it will accept.
        Type? accepted = null;
        try
        {
            accepted = _album.GetMysteryGift(0).GetType();
        }
        catch
        {
            accepted = null;
        }

        _candidates = EncounterEvent.GetAllEvents()
            .Where(g => accepted is null || g.GetType() == accepted)
            .ToList();
        ApplyCandidateFilter();
    }

    private void ApplyCandidateFilter()
    {
        Candidates.Clear();
        var query = CandidateFilter.Trim();
        var shown = 0;
        foreach (var gift in _candidates)
        {
            if (query.Length != 0 && !Describe(gift).Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Candidates.Add(new GiftCandidate(gift, Describe(gift), SpriteFor(gift)));
            if (++shown >= 300) // the archive is thousands of entries; keep the list responsive
                break;
        }
    }

    private string Describe(MysteryGift gift)
    {
        var name = gift.CardTitle;
        if (gift.IsEntity && gift.Species != 0)
        {
            var species = (uint)gift.Species < _strings.specieslist.Length
                ? _strings.specieslist[gift.Species]
                : $"#{gift.Species}";
            return $"{name} — {species}";
        }
        return name;
    }

    private static Bitmap? SpriteFor(MysteryGift gift) =>
        gift is { IsEntity: true, Species: not 0 }
            ? SpriteService.GetSprite(gift.Species, gift.Form, gift.Gender, 0, gift.IsShiny, EntityContext.None)
            : null;

    internal void Notify(string message)
    {
        Status = message;
        RefreshSummary();
        _onChanged();
    }

    /// <summary>Writes the chosen archive gift into the selected card slot.</summary>
    [RelayCommand]
    public void InjectSelected()
    {
        if (_album is null)
            return;
        if (SelectedCard is not { } card)
        {
            Status = "Pick a card slot to write into.";
            return;
        }
        if (SelectedCandidate is not { } candidate)
        {
            Status = "Pick a gift to write.";
            return;
        }
        if (candidate.Gift is not DataMysteryGift data)
        {
            Status = "That gift has no card data to write.";
            return;
        }
        try
        {
            _album.SetMysteryGift(card.Index, data);
        }
        catch (Exception ex)
        {
            Status = $"This save would not accept that card: {ex.GetType().Name}.";
            return;
        }
        LoadCards();
        SelectedCard = Cards.FirstOrDefault(c => c.Index == card.Index);
        Notify($"Wrote \"{candidate.Description}\" into card slot {card.Index + 1}.");
    }

    /// <summary>Blanks the selected card slot.</summary>
    [RelayCommand]
    public void ClearSelected()
    {
        if (_album is null || SelectedCard is not { } card)
        {
            Status = "Pick a card slot first.";
            return;
        }
        try
        {
            var blank = _album.GetMysteryGift(card.Index);
            blank.Data.Clear();
            _album.SetMysteryGift(card.Index, blank);
        }
        catch (Exception ex)
        {
            Status = $"Could not clear that slot: {ex.GetType().Name}.";
            return;
        }
        LoadCards();
        Notify($"Cleared card slot {card.Index + 1}.");
    }

    /// <summary>
    /// Clears every received flag, so the game will hand out its cards again.
    /// </summary>
    [RelayCommand]
    public void ClearReceivedFlags()
    {
        if (_flags is null)
            return;
        _flags.ClearReceivedFlags();
        Notify("Cleared every gift received flag — the game will offer its cards again.");
    }
}

/// <summary>One card slot in the save's album.</summary>
public sealed class GiftCardViewModel
{
    public GiftCardViewModel(GiftAlbumViewModel parent, int index, DataMysteryGift? gift, GameStrings strings)
    {
        Index = index;
        Label = $"Card {index + 1}";

        if (gift is null)
        {
            Title = "(unreadable)";
            IsEmpty = true;
            return;
        }
        // An all-zero card is the empty state.
        if (gift.CardTitle.Length == 0 || gift is { IsEntity: true, Species: 0 })
        {
            Title = "(empty)";
            IsEmpty = true;
            return;
        }
        // A card region that never held a real gift decrypts to noise. Saying so beats
        // rendering mojibake as though it were a title.
        if (!LooksReadable(gift))
        {
            Title = "(not a valid card)";
            Detail = "This slot holds data the game would not read as a gift.";
            IsEmpty = true;
            return;
        }

        Title = gift.CardTitle;
        if (gift is { IsEntity: true, Species: not 0 })
        {
            Species = (uint)gift.Species < strings.specieslist.Length
                ? strings.specieslist[gift.Species]
                : $"#{gift.Species}";
            Sprite = SpriteService.GetSprite(gift.Species, gift.Form, gift.Gender, 0, gift.IsShiny, EntityContext.None);
            Detail = $"{Species} · Lv. {gift.Level}{(gift.IsShiny ? " ★" : string.Empty)}";
        }
        else if (gift.IsItem)
        {
            Detail = $"Item #{gift.ItemID} ×{gift.Quantity}";
        }
    }

    /// <summary>
    /// A real card has a printable title and, for entity gifts, a species in range.
    /// Anything else is leftover noise rather than a gift.
    /// </summary>
    private static bool LooksReadable(DataMysteryGift gift)
    {
        if (gift.IsEntity && gift.Species > (ushort)PKHeX.Core.Species.MAX_COUNT)
            return false;
        var title = gift.CardTitle;
        var printable = title.Count(c => !char.IsControl(c) && !char.IsSurrogate(c));
        return printable >= title.Length - 1;
    }

    public int Index { get; }
    public string Label { get; }
    public string Title { get; } = string.Empty;
    public string Species { get; } = string.Empty;
    public string Detail { get; } = string.Empty;
    public bool IsEmpty { get; }
    public Bitmap? Sprite { get; }
}

/// <summary>An archive gift that this save's album can hold.</summary>
public sealed record GiftCandidate(MysteryGift Gift, string Description, Bitmap? Sprite)
{
    public override string ToString() => Description;
}
