using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class CommandSearchTests
{
    private static int? Score(string title, string query) => CommandSearch.Score(title, "", query);

    [Fact]
    public void EmptyQueryKeepsEverything() =>
        Assert.Equal(0, Score("Anything", ""));

    [Fact]
    public void BetterMatchesScoreHigher()
    {
        var exact = Score("Gyms", "Gyms")!.Value;
        var prefix = Score("Gyms and Titans", "Gyms")!.Value;
        var word = Score("Clear Gyms", "Gyms")!.Value;
        var sub = Score("Regymnasium", "gym")!.Value;
        Assert.True(exact > prefix);
        Assert.True(prefix > word);
        Assert.True(word > sub);
    }

    [Fact]
    public void ShorterTitlesWinTies()
    {
        Assert.True(Score("Bag", "ba")!.Value > Score("Bag and Items", "ba")!.Value);
    }

    [Fact]
    public void InitialsMatchAsASubsequence()
    {
        Assert.NotNull(Score("Blueberry Perks", "bbp"));
        Assert.NotNull(Score("Team Analysis", "ta"));
        Assert.Null(Score("Team Analysis", "zz"));
    }

    [Fact]
    public void GroupIsSearchable()
    {
        Assert.NotNull(CommandSearch.Score("Gyms", "Trainer & Bag", "trainer"));
        Assert.Null(CommandSearch.Score("Gyms", "Trainer & Bag", "raids"));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        Assert.NotNull(Score("Integrity Audit", "INTEGRITY"));
        Assert.NotNull(Score("Integrity Audit", "integrity"));
    }

    [Fact]
    public void RankOrdersAndLimits()
    {
        string[] entries = ["Box 1", "Box 12", "Box 2", "Breeding", "Bag"];
        var ranked = CommandSearch.Rank(entries, "box", e => e, _ => "", limit: 2);
        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, e => Assert.StartsWith("Box", e));
        // Shortest title first among equal-quality prefix matches.
        Assert.Equal("Box 1", ranked[0]);
    }

    [Fact]
    public void NonMatchesAreDropped()
    {
        string[] entries = ["Daycare", "Mail", "Hall of Fame"];
        Assert.Empty(CommandSearch.Rank(entries, "qqq", e => e, _ => ""));
    }
}
