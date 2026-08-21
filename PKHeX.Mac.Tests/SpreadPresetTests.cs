using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class SpreadPresetTests
{
    private static PKM Subject()
    {
        var sav = new SAV9SV();
        var pk = sav.BlankPKM;
        pk.Species = (ushort)Species.Garchomp;
        pk.CurrentLevel = 50;
        pk.Gender = pk.GetSaneGender();
        pk.Nature = Nature.Bashful;
        pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 5;
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void NullFieldsAreLeftAlone()
    {
        // "Perfect IVs" must not also impose a nature or wipe EVs.
        var pk = Subject();
        pk.EV_HP = 100;
        var before = pk.Nature;

        var changed = new SpreadPreset { Ivs = [31, 31, 31, 31, 31, 31] }.ApplyTo(pk);

        Assert.Equal(31, pk.IV_HP);
        Assert.Equal(before, pk.Nature);
        Assert.Equal(100, pk.EV_HP);
        Assert.Equal(["IVs"], changed);
    }

    [Fact]
    public void PartialSpreadsOnlyTouchTheStatsTheyName()
    {
        var pk = Subject();
        new SpreadPreset { Ivs = [null, 31, null, null, null, 31] }.ApplyTo(pk);
        Assert.Equal(31, pk.IV_ATK);
        Assert.Equal(31, pk.IV_SPE);
        Assert.Equal(5, pk.IV_HP);      // untouched
        Assert.Equal(5, pk.IV_SPA);
    }

    [Fact]
    public void NatureAlsoSetsTheBattleNatureFromGenEight()
    {
        var pk = Subject();
        new SpreadPreset { Nature = (int)Nature.Adamant }.ApplyTo(pk);
        Assert.Equal(Nature.Adamant, pk.Nature);
        // A mint would otherwise leave the preset cosmetic.
        Assert.Equal(Nature.Adamant, pk.StatAlignment);
    }

    [Fact]
    public void ValuesAreClampedToLegalRanges()
    {
        var pk = Subject();
        new SpreadPreset { Ivs = [99, 99, 99, 99, 99, 99], Evs = [999, 0, 0, 0, 0, 0] }.ApplyTo(pk);
        Assert.Equal(31, pk.IV_HP);
        Assert.Equal(252, pk.EV_HP);
    }

    [Fact]
    public void ApplyingTwiceReportsNoSecondChange()
    {
        var pk = Subject();
        var preset = new SpreadPreset { Nature = (int)Nature.Modest, Evs = [4, 0, 0, 252, 0, 252] };
        Assert.NotEmpty(preset.ApplyTo(pk));
        Assert.Empty(preset.ApplyTo(pk));
    }

    [Fact]
    public void CapturingRoundTrips()
    {
        var pk = Subject();
        pk.EV_SPE = 252;
        var preset = SpreadPreset.From(pk, "Captured");

        var other = Subject();
        preset.ApplyTo(other);
        Assert.Equal(pk.IV_ATK, other.IV_ATK);
        Assert.Equal(252, other.EV_SPE);
        Assert.Equal(pk.Nature, other.Nature);
    }

    [Fact]
    public void SummaryDescribesWhatWillChange()
    {
        GameInfo.Strings = GameInfo.GetStrings("en");
        Assert.Equal("all IVs 31", new SpreadPreset { Ivs = [31, 31, 31, 31, 31, 31] }.Summary);
        Assert.Contains("252 Atk", new SpreadPreset { Evs = [0, 252, null, null, null, null] }.Summary);
        Assert.Equal("changes nothing", new SpreadPreset().Summary);
    }

    [Fact]
    public void DefaultsAreUsableAndDistinct()
    {
        var defaults = SpreadPreset.Defaults();
        Assert.NotEmpty(defaults);
        Assert.Equal(defaults.Count, defaults.Select(d => d.Name).Distinct().Count());
        var pk = Subject();
        foreach (var preset in defaults)
            preset.ApplyTo(pk);       // none may throw
    }
}
