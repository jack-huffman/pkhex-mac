using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

public class AppSettingsTests
{
    [Fact]
    public void RecentPutsTheNewestFirst()
    {
        var s = new AppSettings();
        s.NoteOpened("/a");
        s.NoteOpened("/b");
        Assert.Equal(["/b", "/a"], s.Recent);
    }

    [Fact]
    public void ReopeningMovesToTheFrontWithoutDuplicating()
    {
        var s = new AppSettings();
        s.NoteOpened("/a");
        s.NoteOpened("/b");
        s.NoteOpened("/a");
        Assert.Equal(["/a", "/b"], s.Recent);
    }

    [Fact]
    public void PathComparisonIgnoresCase()
    {
        var s = new AppSettings();
        s.NoteOpened("/Saves/Main");
        s.NoteOpened("/saves/main");
        Assert.Single(s.Recent);
    }

    [Fact]
    public void ListIsCapped()
    {
        var s = new AppSettings();
        for (int i = 0; i < 30; i++)
            s.NoteOpened($"/save{i}");
        Assert.Equal(8, s.Recent.Count);
        Assert.Equal("/save29", s.Recent[0]);
    }

    [Fact]
    public void MissingFilesArePruned()
    {
        var s = new AppSettings();
        s.NoteOpened("/definitely/not/here");
        s.PruneMissing();
        Assert.Empty(s.Recent);
    }

    [Fact]
    public void BoundsAreOnlyUsedWhenSensible()
    {
        Assert.False(new AppSettings().HasWindowBounds);
        Assert.False(new AppSettings { WindowWidth = 50, WindowHeight = 50 }.HasWindowBounds);
        Assert.True(new AppSettings { WindowWidth = 1360, WindowHeight = 860 }.HasWindowBounds);
    }
}
