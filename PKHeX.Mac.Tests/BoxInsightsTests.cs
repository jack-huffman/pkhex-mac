using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class BoxInsightsTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    private static SAV5B2W2 SaveWith(params (ushort Species, int Level, bool Shiny)[] entries)
    {
        var sav = new SAV5B2W2();
        for (int i = 0; i < entries.Length; i++)
        {
            var (species, level, shiny) = entries[i];
            var pk = sav.BlankPKM;
            pk.Species = species;
            pk.CurrentLevel = (byte)level;
            pk.Gender = pk.GetSaneGender();
            // A blank entity has PID and TID both zero, which makes it shiny by
            // default, so the non-shiny case has to be set explicitly too.
            if (shiny)
                CommonEdits.SetShiny(pk);
            else
                CommonEdits.SetUnshiny(pk);
            pk.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pk, 0, i);
        }
        return sav;
    }

    [Fact]
    public void EmptyBoxReportsNoContents()
    {
        var summary = BoxInsights.Analyze(new SAV5B2W2(), 0, Strings);
        Assert.False(summary.HasContents);
        Assert.Equal(0, summary.Filled);
        Assert.Empty(summary.Problems);
    }

    [Fact]
    public void CountsWhatTheBoxHolds()
    {
        var sav = SaveWith((1, 5, false), (4, 30, true), (7, 100, false));
        var summary = BoxInsights.Analyze(sav, 0, Strings);

        Assert.True(summary.HasContents);
        Assert.Equal(3, summary.Filled);
        Assert.Equal(30, summary.Capacity);
        Assert.Equal(1, summary.Shiny);
        Assert.Equal("3 of 30 filled", summary.FillText);
        Assert.Equal("Lv. 5–100", summary.LevelText);
    }

    [Fact]
    public void SingleLevelReadsNaturally()
    {
        var summary = BoxInsights.Analyze(SaveWith((25, 50, false), (26, 50, false)), 0, Strings);
        Assert.Equal("all Lv. 50", summary.LevelText);
    }

    [Fact]
    public void ProblemsCarryTheirSlotAndAReason()
    {
        // Blank-save entities have no encounter data, so they fail legality — which is
        // what the panel is for.
        var summary = BoxInsights.Analyze(SaveWith((1, 5, false), (4, 5, false)), 0, Strings);
        foreach (var problem in summary.Problems)
        {
            Assert.InRange(problem.Slot, 0, 29);
            Assert.False(string.IsNullOrWhiteSpace(problem.Issue));
            Assert.False(string.IsNullOrWhiteSpace(problem.Species));
            Assert.Equal($"slot {problem.Slot + 1}", problem.SlotText);
        }
    }

    [Fact]
    public void OutOfRangeBoxIsHandled()
    {
        var sav = SaveWith((1, 5, false));
        Assert.False(BoxInsights.Analyze(sav, 9999, Strings).HasContents);
        Assert.False(BoxInsights.Analyze(sav, -1, Strings).HasContents);
    }

    [Fact]
    public void CancellationStopsEarly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.False(BoxInsights.Analyze(SaveWith((1, 5, false)), 0, Strings, cts.Token).HasContents);
    }
}
