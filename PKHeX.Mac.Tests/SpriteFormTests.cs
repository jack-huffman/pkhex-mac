using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Which image a form gets is a correctness question, not a cosmetic one: the flower
/// colour is how you tell one Flabébé from another.
/// </summary>
public class SpriteFormTests
{
    [Theory]
    [InlineData(Species.Flabébé)]   // flower colours
    [InlineData(Species.Floette)]
    [InlineData(Species.Florges)]
    [InlineData(Species.Vivillon)]  // wing patterns
    [InlineData(Species.Furfrou)]   // trims
    [InlineData(Species.Alcremie)]  // creams
    [InlineData(Species.Minior)]    // core colours
    [InlineData(Species.Unown)]
    [InlineData(Species.Arceus)]
    public void AlternateFormsNeverBorrowTheBaseRender(Species species)
    {
        // Form 0 is the base and may use the base render.
        Assert.False(SpriteFormPrefers(species, 0));
        // Every other form must not, or they would all look identical.
        for (byte form = 1; form < 5; form++)
            Assert.True(SpriteFormPrefers(species, form));
    }

    [Theory]
    [InlineData(Species.Sinistea)]      // Phony and Antique look the same
    [InlineData(Species.Polteageist)]
    [InlineData(Species.Scatterbug)]
    [InlineData(Species.Spewpa)]
    [InlineData(Species.Mimikyu)]
    public void FormsThatShareAnAppearanceStillUseTheBaseRender(Species species)
    {
        for (byte form = 0; form < 3; form++)
            Assert.False(SpriteFormPrefers(species, form));
    }

    [Fact]
    public void BaseFormAlwaysUsesTheCrispRender()
    {
        // Nothing about form 0 should ever prefer the low-resolution art.
        foreach (var species in new[] { Species.Pikachu, Species.Eevee, Species.Flabébé })
            Assert.False(SpriteFormPrefers(species, 0));
    }

    private static bool SpriteFormPrefers(Species species, byte form) =>
        SpriteService.PrefersFormArtwork((ushort)species, form);
}
