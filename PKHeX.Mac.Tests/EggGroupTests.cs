using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// PKHeX ships no names for egg groups, so the mapping here is ours. It was derived
/// from known species, and these tests keep it honest.
/// </summary>
public class EggGroupTests
{
    [Theory]
    [InlineData(1, "Monster")]
    [InlineData(2, "Water 1")]
    [InlineData(5, "Field")]
    [InlineData(6, "Fairy")]
    [InlineData(9, "Water 3")]
    [InlineData(13, "Ditto")]
    [InlineData(14, "Dragon")]
    [InlineData(15, "Undiscovered")]
    public void GroupsAreNamed(int group, string expected) =>
        Assert.Equal(expected, EggGroups.GetName(group));

    [Theory]
    [InlineData((ushort)1, "Monster · Grass")]     // Bulbasaur
    [InlineData((ushort)25, "Field · Fairy")]      // Pikachu
    [InlineData((ushort)133, "Field")]             // Eevee, both slots the same
    [InlineData((ushort)132, "Ditto")]
    [InlineData((ushort)150, "Undiscovered")]      // Mewtwo
    public void RealSpeciesDescribeCorrectly(ushort species, string expected)
    {
        var pi = PersonalTable.SV[species, 0];
        Assert.Equal(expected, EggGroups.Describe(pi));
    }

    [Theory]
    [InlineData((ushort)133, true)]    // Eevee breeds
    [InlineData((ushort)132, true)]    // Ditto breeds with almost anything
    [InlineData((ushort)150, false)]   // legendaries do not
    [InlineData((ushort)144, false)]   // Articuno
    public void UndiscoveredCannotBreed(ushort species, bool expected)
    {
        Assert.Equal(expected, EggGroups.CanBreed(PersonalTable.SV[species, 0]));
    }

    [Fact]
    public void UnknownGroupFallsBackReadably() =>
        Assert.Equal("Group 99", EggGroups.GetName(99));
}
