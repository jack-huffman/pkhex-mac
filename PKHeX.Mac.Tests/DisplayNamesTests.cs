using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class DisplayNamesTests
{
    [Theory]
    [InlineData("BattleSubwayPlay", "Battle subway play")]
    [InlineData("Club1SmugElegant", "Club 1 smug elegant")]
    [InlineData("PIDIsShiny", "PID is shiny")]
    [InlineData("GetPID", "Get PID")]
    [InlineData("UnlockedRaidDifficulty6", "Unlocked raid difficulty 6")]
    [InlineData("Money", "Money")]
    [InlineData("", "")]
    public void PascalCaseBecomesASentence(string input, string expected)
        => Assert.Equal(expected, DisplayNames.FromPascalCase(input));

    [Fact]
    public void CaseCanBeKeptForProperNouns()
        => Assert.Equal("Champion Kalos", DisplayNames.FromPascalCase("ChampionKalos", keepCase: true));

    [Theory]
    [InlineData("total_capture", "Total capture")]
    [InlineData("eggs_hatched", "Eggs hatched")]
    [InlineData("single", "Single")]
    public void SnakeCaseBecomesASentence(string input, string expected)
        => Assert.Equal(expected, DisplayNames.FromSnakeCase(input));

    [Theory]
    [InlineData("KUnlockedUpgradeFly", "Unlocked upgrade fly")]
    [InlineData("KMoney", "Money")]
    [InlineData("Box 3, slot 1", "Box 3, slot 1")]   // not a block label; untouched
    [InlineData("Kitakami", "Kitakami")]             // K followed by lowercase is a word, not a prefix
    public void BlockLabelsDropTheirPrefix(string input, string expected)
        => Assert.Equal(expected, DisplayNames.FromBlockLabel(input));

    [Fact]
    public void SuffixesAreStrippedOnlyWhenSomethingRemains()
    {
        Assert.Equal("Smug", DisplayNames.WithoutSuffix("SmugPurchased", "Purchased"));
        Assert.Equal("Purchased", DisplayNames.WithoutSuffix("Purchased", "Purchased"));
        Assert.Equal("Smug", DisplayNames.WithoutSuffix("Smug", "Purchased"));
    }
}
