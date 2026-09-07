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
/// Stored under <c>~/Library/Application Support</c>, where macOS expects preferences, and
/// written atomically so a crash mid-write cannot leave an unreadable file. Nothing here
/// is important enough to interrupt anyone over, so every failure is swallowed and the
/// defaults apply.
/// </remarks>
public sealed class AppSettings
{
    private const int MaxRecent = 8;

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    /// <summary>Last window position; null until one has been recorded.</summary>
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
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

    [JsonIgnore]
    public bool HasWindowPosition => WindowX is not null && WindowY is not null;

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

    /// <summary>Where this user's settings live.</summary>
    public static string Path { get; } = System.IO.Path.Combine(SettingsRoot(), "PKHeX.Mac", "settings.json");

    /// <summary>
    /// Where earlier builds wrote the file. .NET maps <see cref="Environment.SpecialFolder.ApplicationData"/>
    /// to <c>~/.config</c> on every Unix, macOS included, which is not where a Mac app's
    /// preferences belong. A file found there is adopted once and then left alone.
    /// </summary>
    private static readonly string LegacyPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKHeX.Mac", "settings.json");

    private static string SettingsRoot()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(home, "Library", "Application Support");
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <summary>Loads this user's settings, or defaults when there are none worth reading.</summary>
    public static AppSettings Load()
    {
        AdoptLegacyFile();
        return Load(Path);
    }

    /// <summary>Reads settings from <paramref name="path"/>. A missing, unreadable or corrupt file yields defaults.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable file is not worth reporting; defaults are fine.
        }
        return new AppSettings();
    }

    /// <summary>Writes this user's settings. Never throws.</summary>
    public void Save() => Save(Path);

    /// <summary>Writes settings to <paramref name="path"/> atomically. Never throws.</summary>
    public void Save(string path)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            // Write beside the target and move into place, so an interrupted write
            // cannot truncate the previous settings.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing window position is not worth surfacing.
        }
    }

    private static void AdoptLegacyFile()
    {
        try
        {
            if (LegacyPath == Path || File.Exists(Path) || !File.Exists(LegacyPath))
                return;
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Move(LegacyPath, Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Then the defaults apply, as with any other unreadable file.
        }
    }
}
