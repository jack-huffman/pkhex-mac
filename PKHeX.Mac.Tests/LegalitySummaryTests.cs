using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class LegalitySummaryTests
{
    private static PKM LegalPikachu() => Fixtures.Legal(new SAV5B2W2(), Species.Pikachu);

    [Fact]
    public void ALegalEntityHasNoIssue()
    {
        var la = new LegalityAnalysis(LegalPikachu());
        Assert.True(la.Valid, la.Report());
        Assert.Equal(string.Empty, LegalitySummary.FirstIssue(la));
    }

    [Fact]
    public void AnIllegalEntityReportsItsFirstComplaintNotTheVerdictLine()
    {
        var pk = LegalPikachu();
        pk.Move1 = (ushort)Move.SacredFire; // Ho-Oh's signature move; no Pikachu has ever learned it
        pk.RefreshChecksum();
        var la = new LegalityAnalysis(pk);
        Assert.False(la.Valid);

        // PKHeX lists each complaint as "Invalid <check>: <reason>"; the summary is the
        // first of those, not a bare verdict line.
        var issue = LegalitySummary.FirstIssue(la);
        Assert.Contains("Move 1", issue, StringComparison.Ordinal);
        Assert.Equal(issue, la.Report().Split('\n')[0].Trim());
    }
}
