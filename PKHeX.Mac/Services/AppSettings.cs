using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PKHeX.Mac.Services;

/// <summary>
/// The small amount of state worth surviving a relaunch: where the window was, what
/// you were looking at, and which saves you have opened.
/// </summary>
/// <remarks>
/// Stored under Application Support, the conventional place on macOS, and written
/// atomically so a crash mid-write cannot leave an unreadable file. Nothing here is
/// important enough to interrupt anyone over, so every failure is swallowed and the
/// defaults apply.
/// </remarks>
public sealed class AppSettings
{
    private const int MaxRecent = 8;

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public double WindowX { get; set; } = double.NaN;
    public double WindowY { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }

    /// <summary>The sidebar destination that was showing.</summary>
    public string? LastView { get; set; }

    /// <summary>Box that was open, restored only for the same save.</summary>
    public int LastBox { get; set; }

    /// <summary>Path of the save the box index belongs to.</summary>
    public string? LastSavePath { get; set; }

    /// <summary>Most recently opened saves, newest first.</summary>
    public List<string> Recent { get; set; } = [];

    [JsonIgnore]
    public bool HasWindowBounds => WindowWidth > 200 && WindowHeight > 200;

    /// <summary>Records a save as opened, moving it to the front.</summary>
    public void NoteOpened(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        while (Recent.Count > MaxRecent)
            Recent.RemoveAt(Recent.Count - 1);
    }

    /// <summary>Drops entries whose file no longer exists.</summary>
    public void PruneMissing() => Recent.RemoveAll(p => !File.Exists(p));

    // ---- Persistence ----

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PKHeX.Mac", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch
        {
            // A corrupt or unreadable file is not worth reporting; defaults are fine.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            // Write beside the target and move into place, so an interrupted write
            // cannot truncate the previous settings.
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, Path, overwrite: true);
        }
        catch
        {
            // Losing window position is not worth surfacing.
        }
    }
}
