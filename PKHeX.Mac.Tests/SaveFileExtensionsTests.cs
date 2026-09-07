using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class SaveFileExtensionsTests
{
    private static PKM Make(SaveFile sav, ushort species)
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.Gender = pk.GetSaneGender();
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void OccupiedSlotsComeBoxesFirstThenParty()
    {
        var sav = new SAV5B2W2();
        sav.SetBoxSlotAtIndex(Make(sav, 4), 2, 7);
        sav.SetBoxSlotAtIndex(Make(sav, 1), 0, 3);
        sav.SetPartySlotAtIndex(Make(sav, 7), 0);

        var slots = sav.EnumerateOccupiedSlots().ToList();
        Assert.Equal(3, slots.Count);
        Assert.Equal((1, 0, 3), (slots[0].Entity.Species, slots[0].Box, slots[0].Slot));
        Assert.Equal((4, 2, 7), (slots[1].Entity.Species, slots[1].Box, slots[1].Slot));
        Assert.True(slots[2].IsParty);
        Assert.Equal("Box 3, slot 8", slots[1].Location);
        Assert.Equal("Party, slot 1", slots[2].Location);
    }

    [Fact]
    public void EntitiesAreCopiesNotViews()
    {
        var sav = new SAV5B2W2();
        sav.SetBoxSlotAtIndex(Make(sav, 25), 0, 0);
        var snapshot = sav.EnumerateOccupiedSlots().Single().Entity;
        sav.SetBoxSlotAtIndex(sav.BlankPKM, 0, 0);
        Assert.Equal(25, snapshot.Species);
    }

    [Fact]
    public void FirstEmptySlotSkipsOccupants()
    {
        var sav = new SAV5B2W2();
        Assert.Equal(0, sav.FindFirstEmptySlot(0));
        sav.SetBoxSlotAtIndex(Make(sav, 1), 0, 0);
        sav.SetBoxSlotAtIndex(Make(sav, 1), 0, 1);
        Assert.Equal(2, sav.FindFirstEmptySlot(0));
        for (int i = 0; i < sav.BoxSlotCount; i++)
            sav.SetBoxSlotAtIndex(Make(sav, 1), 1, i);
        Assert.Equal(-1, sav.FindFirstEmptySlot(1));
        Assert.Equal(-1, sav.FindFirstEmptySlot(999));
    }
}
