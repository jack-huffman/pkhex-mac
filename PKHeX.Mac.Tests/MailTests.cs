using PKHeX.Core;
using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Held mail lives inside a party Pokémon and is addressed by party position, but the box
/// grid can reorder the party while the mail editor is cached. Writing then landed on
/// whoever had moved into that slot.
/// </summary>
public class MailTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    private static SAV5B2W2 SaveWithParty(params Species[] party)
    {
        var sav = new SAV5B2W2();
        for (int i = 0; i < party.Length; i++)
            sav.SetPartySlotAtIndex(Fixtures.Legal(sav, party[i]), i);
        return sav;
    }

    [Fact]
    public void EveryPartyMemberGetsAHeldMailRow()
    {
        var sav = SaveWithParty(Species.Pikachu, Species.Eevee);
        var vm = new MailViewModel(sav, Strings, () => { });

        Assert.True(vm.IsSupported);
        Assert.Equal(2, vm.Rows.Count(r => r.IsHeldMail));
    }

    [Fact]
    public void WritingRefusesWhenThePartyMovedUnderneathIt()
    {
        var sav = SaveWithParty(Species.Pikachu, Species.Eevee);
        var vm = new MailViewModel(sav, Strings, () => { });
        var held = vm.Rows.First(r => r.IsHeldMail);

        // What deleting the first party member does: everyone after it shifts up.
        sav.SetPartySlotAtIndex(Fixtures.Legal(sav, Species.Eevee), 0);

        held.AuthorName = "Ghost";

        Assert.Contains("party changed", vm.Status, StringComparison.OrdinalIgnoreCase);
        // The bystander that moved into the slot keeps its own mail.
        Assert.NotEqual("Ghost", new Mail5(((PK5)sav.GetPartySlotAtIndex(0)).HeldMail.ToArray()).AuthorName);
    }

    [Fact]
    public void ReloadingReadsTheCurrentParty()
    {
        var sav = SaveWithParty(Species.Pikachu, Species.Eevee);
        var vm = new MailViewModel(sav, Strings, () => { });
        sav.SetPartySlotAtIndex(Fixtures.Legal(sav, Species.Eevee), 0);

        vm.Reload();
        var held = vm.Rows.First(r => r.IsHeldMail);
        held.AuthorName = "Owner";

        // Addressed correctly now, so the write lands.
        Assert.DoesNotContain("party changed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Owner", new Mail5(((PK5)sav.GetPartySlotAtIndex(0)).HeldMail.ToArray()).AuthorName);
    }

    [Fact]
    public void MailboxSlotsAreUnaffectedByTheParty()
    {
        var sav = SaveWithParty(Species.Pikachu);
        var vm = new MailViewModel(sav, Strings, () => { });
        var mailbox = vm.Rows.First(r => !r.IsHeldMail);

        sav.SetPartySlotAtIndex(Fixtures.Legal(sav, Species.Eevee), 0);
        mailbox.AuthorName = "Postman";

        Assert.DoesNotContain("party changed", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Postman", mailbox.AuthorName);
    }
}
