using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

using Category = MoveDataService.Category;

/// <summary>
/// Effective power decides which move the coverage view recommends. Getting it wrong
/// once already hid Surging Strikes behind a weaker move, so the arithmetic is pinned.
/// </summary>
public class MoveFactsTests
{
    private static MoveDataService.MoveFacts Move(int power, Category category = Category.Physical,
                                                  int minHits = 1, int maxHits = 1, int crit = 0) =>
        new(power, 100, category, string.Empty, minHits, maxHits, crit);

    [Fact]
    public void SingleHitPowerIsUnchanged()
    {
        Assert.Equal(80, Move(80).EffectivePower);
        Assert.False(Move(80).IsMultiHit);
        Assert.Equal(string.Empty, Move(80).HitsText);
    }

    [Fact]
    public void SurgingStrikesIsWorthFarMoreThanItsListedPower()
    {
        // 25 power, three hits, always crits: the case that exposed the bug.
        var move = Move(25, minHits: 3, maxHits: 3, crit: 6);
        Assert.Equal(112.5, move.EffectivePower);
        Assert.Equal("3 hits", move.HitsText);
        Assert.True(move.EffectivePower > Move(90, Category.Special).EffectivePower);
    }

    [Fact]
    public void VariableHitsUseTheModernDistribution()
    {
        // Two-to-five hit moves average 3.1 under the 35/35/15/15 spread, not 3.5.
        var rockBlast = Move(25, minHits: 2, maxHits: 5);
        Assert.Equal(3.1, rockBlast.ExpectedHits);
        Assert.Equal(77.5, rockBlast.EffectivePower);
        Assert.Equal("2–5 hits", rockBlast.HitsText);
    }

    [Theory]
    [InlineData(0, 1.0)]      // ordinary
    [InlineData(1, 1.0625)]   // raised, e.g. Slash
    [InlineData(2, 1.25)]
    [InlineData(6, 1.5)]      // always crits
    public void CritRateScalesOutput(int rate, double expected)
    {
        Assert.Equal(expected, Move(100, crit: rate).CritMultiplier);
    }

    [Fact]
    public void StatusMovesHaveNoPower()
    {
        var status = new MoveDataService.MoveFacts(null, null, Category.Status, string.Empty);
        Assert.Equal(0, status.EffectivePower);
        Assert.Equal("—", status.PowerText);
        Assert.Equal("—", status.AccuracyText);
    }

    [Fact]
    public void FixedMultiHitBeatsAnEqualPowerSingleHit()
    {
        Assert.True(Move(30, minHits: 2, maxHits: 2).EffectivePower > Move(30).EffectivePower);
    }
}
