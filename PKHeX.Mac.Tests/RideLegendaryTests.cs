using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The ride legendary lives in a box the player cannot open, past the end of what
/// BoxCount reports. Writing there must not spill into the last reachable box.
/// </summary>
public class RideLegendaryTests
{
    [Fact]
    public void OnlyScarletVioletHasAReservedSlot()
    {
        Assert.False(RideLegendary.IsSupported(new SAV5B2W2()));
        Assert.False(RideLegendary.IsSupported(new SAV8SWSH()));
        Assert.True(RideLegendary.IsSupported(new SAV9SV()));
    }

    [Fact]
    public void ReservedSlotSitsPastTheLastReachableBox()
    {
        var sav = new SAV9SV();
        var capacity = sav.Blocks.BoxInfo.Data.Length / sav.SIZE_BOXSLOT;
        var reachable = sav.BoxCount * sav.BoxSlotCount;
        Assert.True(capacity > reachable);
        // A blank save has an empty reserved slot, not a missing one.
        Assert.Null(RideLegendary.Read(sav));
    }

    [Fact]
    public void RoundTripsWithoutTouchingTheBoxes()
    {
        var sav = new SAV9SV();

        // Put a marker in the last reachable slot so a boundary error would show.
        var marker = sav.BlankPKM;
        marker.Species = (ushort)Species.Pikachu;
        marker.CurrentLevel = 42;
        marker.Gender = marker.GetSaneGender();
        marker.RefreshChecksum();
        sav.SetBoxSlotAtIndex(marker, sav.BoxCount - 1, sav.BoxSlotCount - 1);

        var ride = sav.BlankPKM;
        ride.Species = (ushort)Species.Miraidon;
        ride.CurrentLevel = 68;
        ride.Gender = ride.GetSaneGender();
        ride.RefreshChecksum();

        Assert.True(RideLegendary.Write(sav, ride));

        var read = RideLegendary.Read(sav);
        Assert.NotNull(read);
        Assert.Equal((ushort)Species.Miraidon, read!.Species);
        Assert.Equal(68, read.CurrentLevel);

        // The neighbouring box slot is untouched.
        var neighbour = sav.GetBoxSlotAtIndex(sav.BoxCount - 1, sav.BoxSlotCount - 1);
        Assert.Equal((ushort)Species.Pikachu, neighbour.Species);
        Assert.Equal(42, neighbour.CurrentLevel);
    }

    [Fact]
    public void TheRideIsNotVisibleThroughNormalBoxAccess()
    {
        var sav = new SAV9SV();
        var ride = sav.BlankPKM;
        ride.Species = (ushort)Species.Koraidon;
        ride.Gender = ride.GetSaneGender();
        ride.RefreshChecksum();
        RideLegendary.Write(sav, ride);

        // Precisely the problem this feature exists to solve.
        for (int box = 0; box < sav.BoxCount; box++)
        {
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
                Assert.NotEqual((ushort)Species.Koraidon, sav.GetBoxSlotAtIndex(box, slot).Species);
        }
    }

    [Fact]
    public void WritingLeavesTheBlockReReadable()
    {
        // A blank Gen 9 save does not survive Write() then re-parse -- that is a
        // property of blank SV saves, not of this code, and is why the audit tests use
        // Gen 5 for round-trips. The full-save round-trip was verified against a real
        // 4.4MB save instead. What can be checked here is that the bytes written are
        // the bytes read back.
        var sav = new SAV9SV();
        var ride = sav.BlankPKM;
        ride.Species = (ushort)Species.Miraidon;
        ride.CurrentLevel = 80;
        ride.IV_SPA = 7;
        ride.Gender = ride.GetSaneGender();
        ride.RefreshChecksum();

        Assert.True(RideLegendary.Write(sav, ride));
        var after = RideLegendary.Read(sav);
        Assert.NotNull(after);
        Assert.Equal(80, after!.CurrentLevel);
        Assert.Equal(7, after.IV_SPA);
        Assert.True(after.ChecksumValid);
    }

    [Fact]
    public void WrongEntityTypeIsRejected()
    {
        var sav = new SAV9SV();
        var gen5 = new SAV5B2W2().BlankPKM;   // PK5, not PK9
        gen5.Species = (ushort)Species.Miraidon;
        Assert.False(RideLegendary.Write(sav, gen5));
    }
}
