using PKHeX.Core;
using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// From Gen 8 the nature the stats follow is a separate byte, so a Mint can hold it apart
/// from the Pokémon's own nature. The stat arrows follow that byte, so it has to move with
/// the nature unless a Mint is deliberately keeping it back.
/// </summary>
public class NatureTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    /// <summary>Rows a Modest nature leaves alone: HP, Def, SpD, Spe.</summary>
    private static readonly int[] UnaffectedByModest = [0, 2, 4, 5];

    private static (PokemonDetailViewModel Vm, PKM Pk) Editor(SaveFile sav, Species species)
    {
        var vm = new PokemonDetailViewModel(Strings);
        vm.SetContext(sav, new FilteredGameDataSource(sav, GameInfo.Sources));
        var pk = Fixtures.Legal(sav, species);
        pk.Nature = pk.StatAlignment = Nature.Gentle;
        vm.Load(pk);
        return (vm, vm.Pokemon!);
    }

    [Fact]
    public void ChangingTheNatureOfAnUnmintedPokemonMovesTheStatNatureWithIt()
    {
        var (vm, pk) = Editor(new SAV9SV(), Species.Sprigatito);
        Assert.False(vm.HasMintedNature);

        vm.NatureValue = (int)Nature.Modest;

        Assert.Equal(Nature.Modest, pk.Nature);
        Assert.Equal(Nature.Modest, pk.StatAlignment);   // the arrows follow this
        Assert.Equal((int)Nature.Modest, vm.StatNatureValue);
        Assert.False(vm.HasMintedNature);
    }

    [Fact]
    public void TheArrowsLandOnTheStatsTheChosenNatureActuallyMoves()
    {
        var (vm, _) = Editor(new SAV9SV(), Species.Sprigatito);

        vm.NatureValue = (int)Nature.Modest;   // raises Sp. Atk, lowers Attack

        // Rows are HP, Atk, Def, SpA, SpD, Spe.
        Assert.Equal("▲", vm.Stats[3].NatureBadge);
        Assert.Equal("▼", vm.Stats[1].NatureBadge);
        Assert.All(UnaffectedByModest, i => Assert.False(vm.Stats[i].HasNatureBadge));
    }

    [Fact]
    public void AMintKeepsItsOwnStatNatureWhenTheNatureChanges()
    {
        var (vm, pk) = Editor(new SAV9SV(), Species.Sprigatito);
        vm.StatNatureValue = (int)Nature.Adamant;   // a Mint is applied
        Assert.True(vm.HasMintedNature);

        vm.NatureValue = (int)Nature.Modest;

        Assert.Equal(Nature.Modest, pk.Nature);
        Assert.Equal(Nature.Adamant, pk.StatAlignment);   // the Mint survives
        Assert.True(vm.HasMintedNature);
        Assert.Contains("Mint", vm.NatureEffectText, StringComparison.Ordinal);
    }

    [Fact]
    public void OlderFormatsHaveOnlyOneNature()
    {
        var (vm, pk) = Editor(new SAV5B2W2(), Species.Pikachu);

        vm.NatureValue = (int)Nature.Modest;

        Assert.Equal(Nature.Modest, pk.Nature);
        Assert.Equal(Nature.Modest, pk.StatAlignment);
        Assert.False(vm.HasMintedNature);   // no Mint exists before Gen 8
    }
}
