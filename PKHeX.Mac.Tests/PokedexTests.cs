using PKHeX.Core;
using PKHeX.Mac.Services;
using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Every write the Pokédex editor makes has to reach the unsaved-changes counter, and a
/// single-flag edit must not re-register the whole entry on its way there.
/// </summary>
public class PokedexTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    [Fact]
    public void TickingCaughtRegistersTheEntryAndReportsOnce()
    {
        var sav = new SAV9SV();
        var changes = 0;
        var dex = new PokedexViewModel(sav, Strings, () => changes++);
        var row = dex.Rows.First(r => r.Number == (ushort)Species.Sprigatito);

        row.Caught = true;

        Assert.Equal(1, changes);
        Assert.True(DexAccessor.GetCaught(sav, (ushort)Species.Sprigatito));
        Assert.True(row.Seen);
    }

    [Fact]
    public void ClearingShinySeenDoesNotReRegisterTheEntry()
    {
        // The shiny flag used to be written and then routed through the whole-entry
        // registration "to reuse its notification", which set the flag straight back.
        var sav = new SAV9SV();
        var changes = 0;
        var dex = new PokedexViewModel(sav, Strings, () => changes++) { ShinyToo = true };
        var row = dex.Rows.First(r => r.Number == (ushort)Species.Sprigatito);
        row.Caught = true;
        row.ShinySeen = true;
        Assert.True(Dex9Detail.GetShinySeen(sav, (ushort)Species.Sprigatito));

        row.ShinySeen = false;

        Assert.Equal(3, changes);
        Assert.False(Dex9Detail.GetShinySeen(sav, (ushort)Species.Sprigatito));
        Assert.True(DexAccessor.GetCaught(sav, (ushort)Species.Sprigatito));
    }

    [Fact]
    public void PerFormAndGenderEditsReportChanges()
    {
        var sav = new SAV9SV();
        var changes = 0;
        var dex = new PokedexViewModel(sav, Strings, () => changes++);
        var row = dex.Rows.First(r => r.Number == (ushort)Species.Sprigatito);
        dex.SelectedRow = row;             // builds the detail pane
        changes = 0;

        row.Forms[0].Seen = true;
        row.Genders[0].Value = true;

        Assert.Equal(2, changes);
        Assert.True(Dex9Detail.GetFormSeen(sav, (ushort)Species.Sprigatito, 0));
        Assert.True(Dex9Detail.GetGenderSeen(sav, (ushort)Species.Sprigatito, 0));
    }
}
