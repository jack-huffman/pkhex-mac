using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Every species name shown in the UI goes through one lookup, so its out-of-range
/// behaviour is what stands between a corrupt save and a crash.
/// </summary>
public class GameStringsExtensionsTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    [Fact]
    public void KnownSpeciesResolveToTheirName()
    {
        Assert.Equal("Pikachu", Strings.SpeciesName((int)Species.Pikachu));
        var pk = new SAV9SV().BlankPKM;
        pk.Species = (ushort)Species.Miraidon;
        Assert.Equal("Miraidon", Strings.SpeciesName(pk));
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(ushort.MaxValue)]
    [InlineData(-1)]
    public void UnknownIdsDegradeToTheNumber(int species)
        => Assert.Equal($"#{species}", Strings.SpeciesName(species));

    [Fact]
    public void AnyTableWorksTheSameWay()
    {
        string[] table = ["zero", "one"];
        Assert.Equal("one", table.NameOrId(1));
        Assert.Equal("#2", table.NameOrId(2));
        Assert.Equal("#-1", table.NameOrId(-1));
    }
}
