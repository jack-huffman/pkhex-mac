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
/// The mailbox, and the mail each party member is carrying. Gen 2 through 5 store
/// mail; later generations dropped it.
/// </summary>
/// <remarks>
/// Two kinds of mail live here and they are written back differently. Mailbox mail
/// knows its offset in the save and writes straight there. Mail held by a party
/// Pokémon lives inside that Pokémon's own data, so it is written into the entity
/// and the party slot is then re-saved — calling the save-file write path on held
/// mail would target offset -1 and corrupt the file, so the two are kept distinct.
///
/// Held mail is addressed by <em>party position</em>, which the box grid can change
/// underneath this view: deleting a party member shifts everyone after it up. The
/// rows are therefore rebuilt by <see cref="Reload"/> whenever the view is opened,
/// rather than trusting positions captured when the editor was first built.
///
/// Gen 4 and 5 messages are word codes rather than text, so only the games that store
/// real strings expose an editable message.
/// </remarks>
public partial class MailViewModel : ObservableObject
{
    /// <summary>Gen 2 and 3 keep one held mail per party slot ahead of the mailbox.</summary>
    private const int HeldMailSlots = 6;
    private const int MailboxSlots23 = 10;
    private const int MailboxSlots45 = 20;

    private readonly SaveFile _sav;
    private readonly GameStrings _strings;
    private readonly Action _onChanged;
    private bool _bulk;

    public MailViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _sav = sav;
        _strings = strings;
        _onChanged = onChanged;
        Build();
        IsSupported = Rows.Count > 0;
        RefreshSummary();
    }

    /// <summary>
    /// Re-reads every slot from the save. Called when the view opens, because held mail
    /// is addressed by party position and the party can be reordered elsewhere.
    /// </summary>
    public void Reload()
    {
        Rows.Clear();
        Build();
        RefreshSummary();
        OnPropertyChanged(nameof(Visible));
    }

    private void Build()
    {
        var sav = _sav;
        var strings = _strings;
        switch (sav)
        {
            case SAV2 sav2:
                // Gen 2 mail all lives in the save; the first six belong to the party.
                for (int i = 0; i < HeldMailSlots + MailboxSlots23; i++)
                    Add(new Mail2(sav2, i), i < HeldMailSlots ? $"Party {i + 1} held" : $"Mailbox {i - HeldMailSlots + 1}", -1, strings);
                break;

            case SAV2Stadium stadium:
                for (int i = 0; i < SAV2Stadium.MailboxHeldMailCount + SAV2Stadium.MailboxMailCount; i++)
                {
                    var held = i < SAV2Stadium.MailboxHeldMailCount;
                    Add(new Mail2(stadium, i),
                        held ? $"Held {i + 1}" : $"Mailbox {i - SAV2Stadium.MailboxHeldMailCount + 1}", -1, strings);
                }
                break;

            case SAV3 sav3:
                for (int i = 0; i < HeldMailSlots + MailboxSlots23; i++)
                    Add(sav3.LargeBlock.GetMail(i), i < HeldMailSlots ? $"Party {i + 1} held" : $"Mailbox {i - HeldMailSlots + 1}", -1, strings);
                break;

            case SAV4 sav4:
                for (int i = 0; i < sav4.PartyCount; i++)
                {
                    if (sav4.GetPartySlotAtIndex(i) is PK4 pk4)
                        Add(new Mail4(pk4.HeldMail.ToArray()), $"Party {i + 1} held", i, strings);
                }
                for (int j = 0; j < MailboxSlots45; j++)
                    Add(sav4.GetMail(j), $"Mailbox {j + 1}", -1, strings);
                break;

            case SAV5 sav5:
                for (int i = 0; i < sav5.PartyCount; i++)
                {
                    if (sav5.GetPartySlotAtIndex(i) is PK5 pk5)
                        Add(new Mail5(pk5.HeldMail.ToArray()), $"Party {i + 1} held", i, strings);
                }
                for (int j = 0; j < MailboxSlots45; j++)
                    Add(sav5.GetMail(j), $"Mailbox {j + 1}", -1, strings);
                break;
        }
    }

    private void Add(MailDetail mail, string label, int partyIndex, GameStrings strings) =>
        Rows.Add(new MailRowViewModel(this, _sav, mail, label, partyIndex, strings));

    public bool IsSupported { get; }
    public ObservableCollection<MailRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _usedOnly = true;

    public IEnumerable<MailRowViewModel> Visible =>
        UsedOnly ? Rows.Where(r => !r.IsBlank) : Rows;

    partial void OnUsedOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(Visible));
        RefreshSummary();
    }

    internal void RefreshSummary()
    {
        var used = Rows.Count(r => !r.IsBlank);
        Summary = $"{used} of {Rows.Count} mail slots in use";
    }

    internal void Notify(string message)
    {
        if (_bulk)
            return; // the bulk command reports once when it finishes
        Status = message;
        RefreshSummary();
        OnPropertyChanged(nameof(Visible));
        _onChanged();
    }

    /// <summary>Empties every mail slot, reported as one change.</summary>
    [RelayCommand]
    public void ClearAll()
    {
        _bulk = true;
        foreach (var row in Rows.Where(r => !r.IsBlank).ToList())
            row.Clear();
        _bulk = false;
        Notify("Cleared every mail slot.");
    }
}

/// <summary>One piece of mail, either in the mailbox or held by a party member.</summary>
public partial class MailRowViewModel : ObservableObject
{
    private readonly MailViewModel _parent;
    private readonly SaveFile _sav;
    private readonly MailDetail _mail;
    private readonly GameStrings _strings;

    /// <summary>Party slot this mail is carried by, or -1 for mailbox mail.</summary>
    private readonly int _partyIndex;

    /// <summary>
    /// The species that occupied the party slot when this row was read, so a write can
    /// tell that the party has been reordered underneath it.
    /// </summary>
    private readonly ushort _carrierSpecies;

    private bool _loading;

    public MailRowViewModel(MailViewModel parent, SaveFile sav, MailDetail mail, string label,
                            int partyIndex, GameStrings strings)
    {
        _parent = parent;
        _sav = sav;
        _mail = mail;
        _strings = strings;
        _partyIndex = partyIndex;
        _carrierSpecies = partyIndex >= 0 && partyIndex < sav.PartyCount
            ? sav.GetPartySlotAtIndex(partyIndex).Species
            : (ushort)0;
        Label = label;
        IsHeldMail = partyIndex >= 0;

        // Only formats that store the message as text can round-trip an edit. Gen 4 and 5
        // hold word codes, which come back empty.
        SupportsText = mail.GetMessage(false).Length > 0 || sav.Generation == 3;

        Reload();
    }

    public string Label { get; }
    public bool IsHeldMail { get; }
    public bool SupportsText { get; }

    [ObservableProperty] private string _authorName = string.Empty;
    [ObservableProperty] private int _authorTid;
    [ObservableProperty] private int _authorSid;
    [ObservableProperty] private int _appearSpecies;
    [ObservableProperty] private int _mailType;
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private string _speciesName = string.Empty;
    [ObservableProperty] private Bitmap? _sprite;
    [ObservableProperty] private bool _isBlank;
    [ObservableProperty] private string _stateText = string.Empty;

    private void Reload()
    {
        _loading = true;
        AuthorName = _mail.AuthorName;
        AuthorTid = _mail.AuthorTID;
        AuthorSid = _mail.AuthorSID;
        AppearSpecies = _mail.AppearPKM;
        MailType = _mail.MailType;
        if (SupportsText)
            MessageText = _mail.GetMessage(false);

        // IsEmpty is tri-state: true empty, false valid, null malformed.
        var empty = _mail.IsEmpty;
        IsBlank = empty != false;
        StateText = empty switch
        {
            true => "empty",
            false => "in use",
            _ => "unrecognised data",
        };

        var species = (ushort)Math.Clamp(AppearSpecies, 0, ushort.MaxValue);
        SpeciesName = species == 0 ? string.Empty : _strings.SpeciesName(species);
        Sprite = species != 0
            ? SpriteService.GetSprite(species, 0, 0, 0, shiny: false, EntityContext.None)
            : null;
        _loading = false;
    }

    partial void OnAuthorNameChanged(string value)
    {
        if (_loading)
            return;
        _mail.AuthorName = value;
        Write($"Mail author set to \"{value}\".");
    }

    partial void OnAuthorTidChanged(int value)
    {
        if (_loading)
            return;
        _mail.AuthorTID = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        Write("Mail author ID updated.");
    }

    partial void OnAuthorSidChanged(int value)
    {
        if (_loading)
            return;
        _mail.AuthorSID = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        Write("Mail author secret ID updated.");
    }

    partial void OnAppearSpeciesChanged(int value)
    {
        if (_loading)
            return;
        _mail.AppearPKM = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        Write("Mail Pokémon updated.");
    }

    partial void OnMailTypeChanged(int value)
    {
        if (_loading)
            return;
        _mail.MailType = value;
        Write("Mail stationery updated.");
    }

    partial void OnMessageTextChanged(string value)
    {
        if (_loading || !SupportsText)
            return;
        _mail.SetMessage(value, string.Empty, userEntered: true);
        Write("Mail message updated.");
    }

    /// <summary>Blanks this mail.</summary>
    [RelayCommand]
    public void Clear()
    {
        _mail.SetBlank();
        Write($"Cleared {Label}.");
    }

    /// <summary>
    /// Persists the mail. Held mail goes into its Pokémon and the party slot is
    /// re-saved; mailbox mail writes to its own offset in the save.
    /// </summary>
    private void Write(string message)
    {
        if (IsHeldMail)
        {
            if (_partyIndex >= _sav.PartyCount)
            {
                _parent.Status = "That party member is gone; reopen Mail to see the current party.";
                return;
            }
            var pk = _sav.GetPartySlotAtIndex(_partyIndex);
            // Writing mail onto whoever now occupies the slot would corrupt a bystander.
            if (pk.Species != _carrierSpecies)
            {
                _parent.Status = "The party changed since this was read; reopen Mail to edit the right Pokémon.";
                return;
            }
            switch (pk)
            {
                case PK4 pk4:
                    _mail.CopyTo(pk4);
                    break;
                case PK5 pk5:
                    _mail.CopyTo(pk5);
                    break;
                default:
                    _parent.Status = "This save's held mail cannot be written.";
                    return;
            }
            pk.RefreshChecksum();
            _sav.SetPartySlotAtIndex(pk, _partyIndex);
        }
        else
        {
            _mail.CopyTo(_sav);
        }
        Reload();
        _parent.Notify(message);
    }
}
