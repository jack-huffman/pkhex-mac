using PKHeX.Core;
using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The Crown Tundra editor looks blocks up by their internal names rather than pasting key
/// constants, because most of those blocks are unnamed in PKHeX. These tests pin each name
/// against the keys PKHeX <em>does</em> document, so a typo in a name shows up here instead of
/// silently writing to the wrong block in someone's save.
/// </summary>
public class CrownTundraTests
{
    /// <summary>The engine's own lookup: the low 32 bits of the FNV-1a-64 hash of the name.</summary>
    private static uint KeyOf(string internalName) => (uint)FnvHash.HashFnv1a_64(internalName);

    [Theory]
    // The Max Lair legendary family, FSYS_CHIKA_LEGEND_NN. The game's order is not PKHeX's
    // listing order, which is exactly why these need pinning.
    [InlineData("FSYS_CHIKA_LEGEND_01", SaveBlockAccessor8SWSH.KCapturedZapdos)]
    [InlineData("FSYS_CHIKA_LEGEND_02", SaveBlockAccessor8SWSH.KCapturedMoltres)]
    [InlineData("FSYS_CHIKA_LEGEND_03", SaveBlockAccessor8SWSH.KCapturedArticuno)]
    [InlineData("FSYS_CHIKA_LEGEND_04", SaveBlockAccessor8SWSH.KCapturedMewtwo)]
    [InlineData("FSYS_CHIKA_LEGEND_05", SaveBlockAccessor8SWSH.KCapturedSuicune)]
    [InlineData("FSYS_CHIKA_LEGEND_06", SaveBlockAccessor8SWSH.KCapturedEntei)]
    [InlineData("FSYS_CHIKA_LEGEND_07", SaveBlockAccessor8SWSH.KCapturedRaikou)]
    [InlineData("FSYS_CHIKA_LEGEND_08", SaveBlockAccessor8SWSH.KCapturedLugia)]
    [InlineData("FSYS_CHIKA_LEGEND_09", SaveBlockAccessor8SWSH.KCapturedHoOh)]
    [InlineData("FSYS_CHIKA_LEGEND_10", SaveBlockAccessor8SWSH.KCapturedLatias)]
    [InlineData("FSYS_CHIKA_LEGEND_20", SaveBlockAccessor8SWSH.KCapturedGiratina)]
    [InlineData("FSYS_CHIKA_LEGEND_30", SaveBlockAccessor8SWSH.KCapturedYveltal)]
    [InlineData("FSYS_CHIKA_LEGEND_38", SaveBlockAccessor8SWSH.KCapturedNecrozma)]
    [InlineData("FSYS_CHIKA_LEGEND_45", SaveBlockAccessor8SWSH.KCapturedGuzzlord)]
    [InlineData("FSYS_CHIKA_LEGEND_47", SaveBlockAccessor8SWSH.KCapturedBlacephalon)]
    [InlineData("FSYS_CHIKA_LEGEND_48", SaveBlockAccessor8SWSH.KCapturedStakataka)]
    // A flag outside that family, to prove the hash is not fitted to one pattern.
    [InlineData("FSYS_CHIKA_UB_OPEN", SaveBlockAccessor8SWSH.KUnlockedUBsInMaxLair)]
    public void NameHashesToTheKeyPKHeXDocuments(string internalName, uint expected)
        => Assert.Equal(expected, KeyOf(internalName));

    [Theory]
    // Names PKHeX has no constant for. These come from the same brute force and are pinned
    // by value so a typo cannot quietly retarget them.
    [InlineData("FSYS_CHIKA_LEGEND_46", 0xF769AE2Cu)] // the 48th slot, unmapped to a species
    [InlineData("FSYS_GST_OPEN", 0xF27FD130u)]
    [InlineData("FSYS_GST_DANDE", 0xBE14CF20u)]   // Leon
    [InlineData("FSYS_GST_KIBANA", 0xF33517E0u)]  // Raihan
    [InlineData("FSYS_GST_SAITO", 0x64E5B186u)]   // Bea
    [InlineData("FSYS_GST_SIRUDHI", 0x8C42D0E4u)] // Shielbert
    [InlineData("FSYS_CHIKA_FIRST", 0xEB611D76u)]
    [InlineData("FE_R2_CHIKA_INTRO", 0x98C91379u)]
    public void NameHashesToTheRecoveredValue(string internalName, uint expected)
        => Assert.Equal(expected, KeyOf(internalName));

    [Theory]
    [InlineData("0x405EF69F74046C91", 0x405EF69F74046C91ul)]
    [InlineData("405EF69F74046C91", 0x405EF69F74046C91ul)]
    [InlineData("  0X405ef69f74046c91  ", 0x405EF69F74046C91ul)]
    public void SeedTextAcceptsTheFormsAUserWouldType(string text, ulong expected)
    {
        Assert.True(CrownTundraViewModel.TryParseSeed(text, out var seed));
        Assert.Equal(expected, seed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("0x")]
    [InlineData("12345678901234567")]   // 17 digits overflow 64 bits
    public void SeedTextRejectsWhatItCannotParse(string text)
        => Assert.False(CrownTundraViewModel.TryParseSeed(text, out _));

    [Fact]
    public void AnUnsupportedSaveReportsItselfUnsupported()
    {
        // A Gen 4 save has none of these blocks; the tab must hide rather than throw.
        var sav = new SAV4HGSS();
        var vm = new CrownTundraViewModel(sav, GameInfo.GetStrings("en"), () => { });
        Assert.False(vm.IsSupported);
        Assert.Empty(vm.Legendaries);
    }
}
