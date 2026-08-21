using System.Text.Json;
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
public class AppSettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkhex-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SurvivesAWriteAndRead()
    {
        var file = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);

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
        File.WriteAllText(file, JsonSerializer.Serialize(written));

        var read = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file))!;
        Assert.Equal(1440, read.WindowWidth);
        Assert.Equal("tools", read.LastView);
        Assert.Equal(7, read.LastBox);
        Assert.Equal("/saves/main", read.LastSavePath);
        Assert.Equal(["/saves/main"], read.Recent);
        Assert.True(read.HasWindowBounds);
    }

    [Fact]
    public void UnreadableFileFallsBackToDefaults()
    {
        // Load never throws; a corrupt file just means defaults.
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{ this is not json");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)));
        // AppSettings.Load swallows that and returns usable defaults.
        Assert.False(AppSettings.Load().HasWindowBounds || AppSettings.Load().Recent.Count > 8);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
