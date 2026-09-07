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

/// <summary>Round-trips settings through a real file, which the in-memory tests cannot.</summary>
public sealed class AppSettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkhex-settings-" + Guid.NewGuid().ToString("N"));
    private string File => Path.Combine(_dir, "settings.json");

    [Fact]
    public void SurvivesAWriteAndRead()
    {
        var written = new AppSettings
        {
            WindowWidth = 1440,
            WindowHeight = 900,
            WindowX = 120,
            WindowY = 60,
            LastView = "tools",
            LastBox = 7,
            LastSavePath = "/saves/main",
        };
        written.NoteOpened("/saves/main");
        written.Save(File);

        var read = AppSettings.Load(File);
        Assert.Equal(1440, read.WindowWidth);
        Assert.Equal(120, read.WindowX);
        Assert.True(read.HasWindowPosition);
        Assert.Equal("tools", read.LastView);
        Assert.Equal(7, read.LastBox);
        Assert.Equal("/saves/main", read.LastSavePath);
        Assert.Equal(["/saves/main"], read.Recent);
        Assert.True(read.HasWindowBounds);
    }

    [Fact]
    public void SaveCreatesTheDirectoryAndLeavesNoTemporaryFile()
    {
        new AppSettings().Save(File);
        Assert.True(System.IO.File.Exists(File));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void FreshSettingsSerialiseBeforeAnyWindowPositionIsKnown()
    {
        // The position used to default to NaN, which JSON cannot represent, so the very
        // first Save() threw and the recent-files list was never written.
        var s = new AppSettings();
        Assert.False(s.HasWindowPosition);
        s.NoteOpened("/saves/main");
        s.Save(File);
        Assert.Equal(["/saves/main"], AppSettings.Load(File).Recent);
    }

    [Fact]
    public void CorruptFileYieldsDefaultsRatherThanThrowing()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{ not json");
        var s = AppSettings.Load(File);
        Assert.False(s.HasWindowBounds);
        Assert.Empty(s.Recent);
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var s = AppSettings.Load(File);
        Assert.False(s.HasWindowBounds);
        Assert.Null(s.LastView);
        Assert.Equal(0, s.LastBox);
    }

    [Fact]
    public void LivesUnderApplicationSupportOnMac()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        Assert.Contains("/Library/Application Support/PKHeX.Mac/", AppSettings.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("/.config/", AppSettings.Path, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
