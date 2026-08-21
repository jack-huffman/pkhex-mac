using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Transfers rely on PKHeX for the conversion itself; what matters here is that the
/// verdict is reported honestly rather than a failure being written anyway.
/// </summary>
public class SaveTransferTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    private static PKM Make(SaveFile sav, ushort species, int level = 30)
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.CurrentLevel = (byte)level;
        pk.Gender = pk.GetSaneGender();
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void EmptySlotIsRefused()
    {
        var dest = new SAV9SV();
        var plan = SaveTransfer.Plan(new SAV5B2W2().BlankPKM, dest, Strings);
        Assert.False(plan.CanTransfer);
        Assert.Contains("empty", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForwardTransferIsAllowed()
    {
        var source = new SAV5B2W2();
        var dest = new SAV9SV();
        var plan = SaveTransfer.Plan(Make(source, (ushort)Species.Pikachu), dest, Strings);

        Assert.True(plan.CanTransfer);
        Assert.Equal(typeof(PK9), plan.Entity!.GetType());
        Assert.Equal((ushort)Species.Pikachu, plan.Entity.Species);
    }

    [Fact]
    public void BackwardTransferIsRefusedWithAReason()
    {
        // Pokémon only travel forward through the generations.
        var source = new SAV9SV();
        var dest = new SAV5B2W2();
        var plan = SaveTransfer.Plan(Make(source, (ushort)Species.Pikachu), dest, Strings);

        Assert.False(plan.CanTransfer);
        Assert.False(string.IsNullOrWhiteSpace(plan.Problem));
    }

    [Fact]
    public void SpeciesTheDestinationHasNeverHeardOfIsRefused()
    {
        var source = new SAV9SV();
        var dest = new SAV5B2W2();
        // Miraidon postdates Black 2 by four generations.
        var plan = SaveTransfer.Plan(Make(source, (ushort)Species.Miraidon), dest, Strings);
        Assert.False(plan.CanTransfer);
        Assert.Contains("Miraidon", plan.Problem!);
    }

    [Fact]
    public void CommittingFindsTheFirstFreeSlot()
    {
        var source = new SAV5B2W2();
        var dest = new SAV9SV();
        dest.SetBoxSlotAtIndex(Make(dest, (ushort)Species.Eevee), 0, 0);

        var plan = SaveTransfer.Plan(Make(source, (ushort)Species.Pikachu), dest, Strings);
        Assert.True(SaveTransfer.Commit(plan, dest, 0, out var message));
        Assert.Contains("slot 2", message);
        Assert.Equal((ushort)Species.Pikachu, dest.GetBoxSlotAtIndex(0, 1).Species);
        // The occupant is untouched.
        Assert.Equal((ushort)Species.Eevee, dest.GetBoxSlotAtIndex(0, 0).Species);
    }

    [Fact]
    public void AFullBoxIsReportedRatherThanOverwritten()
    {
        var source = new SAV5B2W2();
        var dest = new SAV9SV();
        for (int i = 0; i < dest.BoxSlotCount; i++)
            dest.SetBoxSlotAtIndex(Make(dest, (ushort)Species.Eevee), 0, i);

        var plan = SaveTransfer.Plan(Make(source, (ushort)Species.Pikachu), dest, Strings);
        Assert.False(SaveTransfer.Commit(plan, dest, 0, out var message));
        Assert.Contains("full", message);
    }

    [Fact]
    public void ABlockedPlanCannotBeCommitted()
    {
        var dest = new SAV5B2W2();
        var plan = TransferPlan.Blocked("Miraidon", "no route");
        Assert.False(SaveTransfer.Commit(plan, dest, 0, out var message));
        Assert.Equal("no route", message);
    }

    [Fact]
    public void LegalityIsCheckedOnTheFarSide()
    {
        // A conversion can succeed mechanically and still land somewhere illegal, so the
        // plan carries the destination's verdict rather than assuming success is fine.
        var plan = SaveTransfer.Plan(Make(new SAV5B2W2(), (ushort)Species.Pikachu), new SAV9SV(), Strings);
        Assert.True(plan.CanTransfer);
        if (!plan.IsLegal)
            Assert.False(string.IsNullOrWhiteSpace(plan.Issue));
    }
}

/// <summary>
/// A transferred Pokémon is illegal without a HOME tracker, but the tracker is invented
/// rather than issued, so it must be opt-in and must never repeat.
/// </summary>
public class SaveTransferTrackerTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    private static PKM Source()
    {
        var sav = new SAV8SWSH();
        var pk = sav.BlankPKM;
        pk.Species = (ushort)Species.Falinks;
        pk.CurrentLevel = 40;
        pk.Gender = pk.GetSaneGender();
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void MissingTrackerIsReportedRatherThanFixedQuietly()
    {
        var plan = SaveTransfer.Plan(Source(), new SAV9SV(), Strings);
        Assert.True(plan.CanTransfer);
        Assert.True(plan.NeedsHomeTracker);
        Assert.False(plan.HomeTrackerAssigned);
        Assert.Equal(0UL, ((IHomeTrack)plan.Entity!).Tracker);
    }

    [Fact]
    public void OptingInAssignsOne()
    {
        var plan = SaveTransfer.Plan(Source(), new SAV9SV(), Strings, assignHomeTracker: true);
        Assert.True(plan.HomeTrackerAssigned);
        Assert.False(plan.NeedsHomeTracker);
        Assert.NotEqual(0UL, ((IHomeTrack)plan.Entity!).Tracker);
    }

    [Fact]
    public void TrackersDoNotRepeat()
    {
        // Several Pokémon sharing a tracker is exactly what the integrity audit calls
        // impossible, so this must not hand out the same value twice.
        var seen = new HashSet<ulong>();
        for (int i = 0; i < 200; i++)
        {
            var plan = SaveTransfer.Plan(Source(), new SAV9SV(), Strings, assignHomeTracker: true);
            Assert.True(seen.Add(((IHomeTrack)plan.Entity!).Tracker));
        }
    }

    [Fact]
    public void HandlerIsUpdatedToTheReceivingTrainer()
    {
        // Without this every transfer arrives illegal on "Current handler cannot be the OT".
        var destination = new SAV9SV { OT = "Receiver" };
        var plan = SaveTransfer.Plan(Source(), destination, Strings, assignHomeTracker: true);
        Assert.True(plan.CanTransfer);
        Assert.DoesNotContain("handler", plan.Issue, StringComparison.OrdinalIgnoreCase);
    }
}
