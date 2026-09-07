using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class HomeTrackerTests
{
    [Fact]
    public void NeverReturnsZeroAndDoesNotRepeat()
    {
        // Zero means "no tracker", and the integrity audit calls a repeated tracker
        // conclusive evidence of editing, so both properties matter.
        var seen = new HashSet<ulong>();
        for (int i = 0; i < 10_000; i++)
        {
            var tracker = HomeTracker.NewRandom();
            Assert.NotEqual(0UL, tracker);
            Assert.True(seen.Add(tracker));
        }
    }
}
