using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class SaveDiffTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    private static (SAV5B2W2 Live, SAV5B2W2 Pristine) Pair()
    {
        var pristine = new SAV5B2W2();
        var pk = pristine.BlankPKM;
        pk.Species = (ushort)Species.Pikachu;
        pk.CurrentLevel = 30;
        pk.Gender = pk.GetSaneGender();
        CommonEdits.SetUnshiny(pk);
        pk.RefreshChecksum();
        pristine.SetBoxSlotAtIndex(pk, 0, 0);

        var live = (SAV5B2W2)SaveUtil.GetSaveFile(pristine.Write().ToArray())!;
        return (live, pristine);
    }

    [Fact]
    public void IdenticalSavesReportNothing()
    {
        var (live, pristine) = Pair();
        Assert.Empty(SaveDiff.Compare(live, pristine, Strings));
    }

    [Fact]
    public void AddingAPokemonIsReported()
    {
        var (live, pristine) = Pair();
        var pk = live.BlankPKM;
        pk.Species = (ushort)Species.Eevee;
        pk.Gender = pk.GetSaneGender();
        pk.RefreshChecksum();
        live.SetBoxSlotAtIndex(pk, 0, 5);

        var change = Assert.Single(SaveDiff.Compare(live, pristine, Strings),
                                   c => c.Kind == ChangeKind.Entity);
        Assert.Contains("added Eevee", change.Description);
        Assert.Contains("slot 6", change.Where);
    }

    [Fact]
    public void RemovingAPokemonIsReported()
    {
        var (live, pristine) = Pair();
        live.SetBoxSlotAtIndex(live.BlankPKM, 0, 0);

        var change = Assert.Single(SaveDiff.Compare(live, pristine, Strings),
                                   c => c.Kind == ChangeKind.Entity);
        Assert.Contains("removed Pikachu", change.Description);
    }

    [Fact]
    public void EditsNameTheFieldsThatMoved()
    {
        var (live, pristine) = Pair();
        var pk = live.GetBoxSlotAtIndex(0, 0);
        pk.CurrentLevel = 44;
        pk.IV_ATK = 31;
        pk.RefreshChecksum();
        live.SetBoxSlotAtIndex(pk, 0, 0);

        var change = Assert.Single(SaveDiff.Compare(live, pristine, Strings),
                                   c => c.Kind == ChangeKind.Entity);
        Assert.Contains("Lv 30 → 44", change.Description);
        Assert.Contains("IVs", change.Description);
        // Fields that did not move must not be listed.
        Assert.DoesNotContain("nickname", change.Description);
        Assert.DoesNotContain("EVs", change.Description);
    }

    [Fact]
    public void SwappingSpeciesReadsAsAReplacement()
    {
        var (live, pristine) = Pair();
        var pk = live.BlankPKM;
        pk.Species = (ushort)Species.Bulbasaur;
        pk.Gender = pk.GetSaneGender();
        pk.RefreshChecksum();
        live.SetBoxSlotAtIndex(pk, 0, 0);

        var change = Assert.Single(SaveDiff.Compare(live, pristine, Strings),
                                   c => c.Kind == ChangeKind.Entity);
        Assert.Contains("Pikachu → Bulbasaur", change.Description);
    }

    [Fact]
    public void UnrelatedSlotsAreNotReported()
    {
        var (live, pristine) = Pair();
        var pk = live.GetBoxSlotAtIndex(0, 0);
        pk.CurrentLevel = 31;
        pk.RefreshChecksum();
        live.SetBoxSlotAtIndex(pk, 0, 0);

        // Exactly one slot changed out of the whole save.
        Assert.Single(SaveDiff.Compare(live, pristine, Strings), c => c.Kind == ChangeKind.Entity);
    }
}
