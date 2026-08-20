using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The type chart is hand-entered data, so a typo here would produce confidently wrong
/// battle advice. These are the checks that were run ad hoc while writing it.
/// </summary>
public class TypeChartTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    private static int T(string name) => Array.FindIndex(Strings.types, t => t == name);

    [Theory]
    // Straightforward super-effective and resisted pairs.
    [InlineData("Fire", "Grass", 2)]
    [InlineData("Water", "Fire", 2)]
    [InlineData("Bug", "Grass", 2)]
    [InlineData("Ice", "Dragon", 2)]
    [InlineData("Normal", "Rock", 0.5)]
    [InlineData("Grass", "Steel", 0.5)]
    [InlineData("Fighting", "Fairy", 0.5)]
    [InlineData("Bug", "Fairy", 0.5)]
    [InlineData("Dark", "Fairy", 0.5)]
    // Immunities: the cases most likely to be entered wrong.
    [InlineData("Electric", "Ground", 0)]
    [InlineData("Ghost", "Normal", 0)]
    [InlineData("Fighting", "Ghost", 0)]
    [InlineData("Dragon", "Fairy", 0)]
    [InlineData("Psychic", "Dark", 0)]
    [InlineData("Ground", "Flying", 0)]
    [InlineData("Poison", "Steel", 0)]
    // Fairy's arrival in Gen 6 rewired several rows.
    [InlineData("Fairy", "Dragon", 2)]
    [InlineData("Steel", "Fairy", 2)]
    // Steel stopped resisting Dark and Ghost in Gen 6.
    [InlineData("Dark", "Steel", 1)]
    [InlineData("Ghost", "Steel", 1)]
    public void ModernChartMatchesKnownInteractions(string attacker, string defender, double expected)
    {
        Assert.Equal(expected, TypeChart.Get(T(attacker), T(defender), ChartEra.Modern));
    }

    [Theory]
    [InlineData("Dark", "Steel", 0.5)]
    [InlineData("Ghost", "Steel", 0.5)]
    public void SteelKeptItsDarkAndGhostResistancesBeforeGenSix(string attacker, string defender, double expected)
    {
        Assert.Equal(expected, TypeChart.Get(T(attacker), T(defender), ChartEra.Gen2To5));
        // ...and lost them afterwards.
        Assert.Equal(1, TypeChart.Get(T(attacker), T(defender), ChartEra.Modern));
    }

    [Fact]
    public void GenOneQuirksAreModelled()
    {
        // The famous bug: Ghost did nothing to Psychic despite the type advantage.
        Assert.Equal(0, TypeChart.Get(T("Ghost"), T("Psychic"), ChartEra.Gen1));
        Assert.Equal(2, TypeChart.Get(T("Ghost"), T("Psychic"), ChartEra.Modern));

        // Bug and Poison hit each other hard in Gen 1 only.
        Assert.Equal(2, TypeChart.Get(T("Bug"), T("Poison"), ChartEra.Gen1));
        Assert.Equal(2, TypeChart.Get(T("Poison"), T("Bug"), ChartEra.Gen1));
        Assert.Equal(0.5, TypeChart.Get(T("Bug"), T("Poison"), ChartEra.Modern));
    }

    [Fact]
    public void DualTypesMultiply()
    {
        // Ice against Dragon/Flying is the canonical quadruple hit.
        Assert.Equal(4, TypeChart.GetAgainst(T("Ice"), T("Dragon"), T("Flying"), ChartEra.Modern));
        // Rock/Poison takes quadruple damage from Ground.
        Assert.Equal(4, TypeChart.GetAgainst(T("Ground"), T("Rock"), T("Poison"), ChartEra.Modern));
        // Stacked resistances quarter it.
        Assert.Equal(0.25, TypeChart.GetAgainst(T("Grass"), T("Steel"), T("Dragon"), ChartEra.Modern));
    }

    [Fact]
    public void SingleTypeIsNotSquared()
    {
        // Passing the same type twice must not double-apply; Fire on a pure Grass
        // defender is 2x, not 4x.
        Assert.Equal(2, TypeChart.GetAgainst(T("Fire"), T("Grass"), T("Grass"), ChartEra.Modern));
    }

    [Theory]
    [InlineData(26, "Ground")]   // Levitate
    [InlineData(297, "Ground")]  // Earth Eater
    [InlineData(18, "Fire")]     // Flash Fire
    [InlineData(11, "Water")]    // Water Absorb
    [InlineData(10, "Electric")] // Volt Absorb
    [InlineData(157, "Grass")]   // Sap Sipper
    public void ImmunityAbilitiesZeroTheirType(int ability, string attacker)
    {
        Assert.Equal(0, TypeChart.ApplyAbility(1.0, T(attacker), ability));
        // Other types are untouched by that ability.
        var other = attacker == "Ground" ? T("Fire") : T("Ground");
        Assert.Equal(1.0, TypeChart.ApplyAbility(1.0, other, ability));
    }

    [Fact]
    public void ThickFatHalvesFireAndIceOnly()
    {
        Assert.Equal(0.5, TypeChart.ApplyAbility(1.0, T("Fire"), 47));
        Assert.Equal(0.5, TypeChart.ApplyAbility(1.0, T("Ice"), 47));
        Assert.Equal(1.0, TypeChart.ApplyAbility(1.0, T("Water"), 47));
    }

    [Fact]
    public void WonderGuardBlocksAnythingNotSuperEffective()
    {
        Assert.Equal(0, TypeChart.ApplyAbility(1.0, T("Normal"), 25));
        Assert.Equal(0, TypeChart.ApplyAbility(0.5, T("Normal"), 25));
        Assert.Equal(2, TypeChart.ApplyAbility(2.0, T("Fire"), 25));
    }

    [Fact]
    public void EveryTypeHasABalancedProfile()
    {
        // Sanity net over the whole table: each type must both resist something and be
        // weak to something, and no multiplier may fall outside the legal set.
        var legal = new[] { 0, 0.5, 1, 2 };
        foreach (var attacker in TypeChart.GetTypes(ChartEra.Modern))
        {
            foreach (var defender in TypeChart.GetTypes(ChartEra.Modern))
                Assert.Contains(TypeChart.Get(attacker, defender, ChartEra.Modern), legal);
        }

        foreach (var defender in TypeChart.GetTypes(ChartEra.Modern))
        {
            var incoming = TypeChart.GetTypes(ChartEra.Modern)
                .Select(a => TypeChart.Get(a, defender, ChartEra.Modern)).ToList();
            Assert.Contains(2, incoming);                 // something hits it hard
            Assert.Contains(incoming, m => m < 1);        // it resists something
        }
    }

    [Theory]
    [InlineData(ChartEra.Modern, 18)]
    [InlineData(ChartEra.Gen2To5, 17)]  // no Fairy
    [InlineData(ChartEra.Gen1, 15)]     // no Fairy, Dark or Steel
    public void EachEraExposesItsOwnTypes(ChartEra era, int expected)
    {
        Assert.Equal(expected, TypeChart.GetTypes(era).Count);
    }

    [Fact]
    public void StellarIsTreatedAsNeutral()
    {
        // Stellar is a Tera type with no defensive chart entry; it must not throw.
        Assert.Equal(1.0, TypeChart.Get(18, T("Fire"), ChartEra.Modern));
        Assert.Equal(1.0, TypeChart.Get(T("Fire"), 18, ChartEra.Modern));
    }
}
