using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Finds save files on this Mac without the user going hunting through a file dialog:
/// inserted SD cards, the usual macOS emulator save folders, and the obvious download
/// and document locations.
/// </summary>
/// <remarks>
/// The console SD-card layouts come from PKHeX's own <see cref="SaveFinder"/>, so the
/// Checkpoint / JKSV / gm9 / FBI / TWLSaveTool paths stay correct as upstream learns
/// about new ones. The macOS side is ours: PKHeX's defaults look for Windows drive
/// letters, which find nothing here.
/// </remarks>
public static class SaveDiscovery
{
    /// <summary>Folders are searched to this depth. Deep enough for emulator layouts,
    /// shallow enough that a large Documents folder does not stall the scan.</summary>
    private const int MaxDepth = 4;

    /// <summary>Emulator save locations under ~/Library/Application Support.</summary>
    private static readonly string[] EmulatorFolders =
    [
        "mGBA", "DeSmuME", "melonDS", "OpenEmu",
        "Citra", "Lime3DS", "Azahar", "PabloMK7/Citra", // 3DS, across its forks
        "Ryujinx/bis/user/save", "Dolphin/GC",
        "Cemu", "Sudachi", "Citron",
    ];

    /// <summary>Everywhere worth looking, in the order results should be grouped.</summary>
    public static List<SearchRoot> GetSearchRoots()
    {
        var roots = new List<SearchRoot>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Inserted SD cards and external drives: check the known console layouts, and
        // the volume root itself in case the save was copied loose.
        if (Directory.Exists("/Volumes"))
        {
            foreach (var volume in SafeDirectories("/Volumes"))
            {
                // The startup disk is the whole machine; scanning it would never finish.
                if (IsSystemVolume(volume))
                    continue;
                var name = Path.GetFileName(volume);
                foreach (var path in SaveFinder.Get3DSBackupPaths(volume).Concat(SaveFinder.GetSwitchBackupPaths(volume)))
                    roots.Add(new SearchRoot(path, $"{name} (console backup)"));
                roots.Add(new SearchRoot(volume, name));
            }
        }

        var support = Path.Combine(home, "Library", "Application Support");
        foreach (var emulator in EmulatorFolders)
        {
            var path = Path.Combine(support, emulator.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
                roots.Add(new SearchRoot(path, emulator.Split('/')[0]));
        }

        foreach (var folder in new[] { "Downloads", "Documents", "Desktop" })
        {
            var path = Path.Combine(home, folder);
            if (Directory.Exists(path))
                roots.Add(new SearchRoot(path, folder));
        }

        return roots;
    }

    /// <summary>
    /// Walks the search roots and returns every file that parses as a save.
    /// </summary>
    public static List<DiscoveredSave> Scan(CancellationToken token, Action<string>? progress = null)
    {
        var found = new List<DiscoveredSave>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSearchRoots())
        {
            if (token.IsCancellationRequested)
                break;
            progress?.Invoke($"Searching {root.Label}…");

            foreach (var file in EnumerateFiles(root.Path, MaxDepth, token))
            {
                if (token.IsCancellationRequested)
                    break;
                if (!seen.Add(file))
                    continue;
                if (!CouldBeSave(file))
                    continue;

                SaveFile? sav = null;
                try
                {
                    SaveUtil.TryGetSaveFile(file, out sav);
                }
                catch
                {
                    sav = null; // unreadable or not a save; keep going
                }
                if (sav is null)
                    continue;

                found.Add(new DiscoveredSave(file, sav, root.Label));
                progress?.Invoke($"Found {found.Count} save{(found.Count == 1 ? string.Empty : "s")}…");
            }
        }

        // Most recently written first: that is almost always the one wanted.
        return found.OrderByDescending(f => f.Modified).ToList();
    }

    /// <summary>
    /// Cheap pre-filter. Save files sit in a known size band, so this skips the bulk of
    /// a Documents folder without opening anything.
    /// </summary>
    private static bool CouldBeSave(string path)
    {
        try
        {
            var info = new FileInfo(path);
            // Smallest supported save is a 32KB Gen 1; largest is a ~7MB Switch title.
            return info.Length is >= 0x8000 and <= 0x800000;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSystemVolume(string volume)
    {
        try
        {
            // The startup disk appears in /Volumes as a symlink to /.
            return Directory.ResolveLinkTarget(volume, returnFinalTarget: true)?.FullName == "/"
                   || Path.GetFileName(volume) == "Macintosh HD";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Depth-limited enumeration that steps over folders it cannot read.</summary>
    private static IEnumerable<string> EnumerateFiles(string root, int depth, CancellationToken token)
    {
        if (depth < 0 || token.IsCancellationRequested || !Directory.Exists(root))
            yield break;

        string[] files;
        try
        {
            files = Directory.GetFiles(root);
        }
        catch
        {
            yield break; // permission denied, or it vanished mid-scan
        }
        foreach (var file in files)
            yield return file;

        foreach (var dir in SafeDirectories(root))
        {
            if (token.IsCancellationRequested)
                yield break;
            // App bundles and package folders never hold game saves.
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') || name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var file in EnumerateFiles(dir, depth - 1, token))
                yield return file;
        }
    }

    private static string[] SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return [];
        }
    }
}

/// <summary>A folder to search, with the label its results are grouped under.</summary>
public sealed record SearchRoot(string Path, string Label);

/// <summary>A save file found on disk, described well enough to choose between several.</summary>
public sealed class DiscoveredSave
{
    public DiscoveredSave(string path, SaveFile save, string source)
    {
        Path = path;
        Save = save;
        Source = source;
        FileName = System.IO.Path.GetFileName(path);
        Modified = File.GetLastWriteTime(path);

        Game = save.Version.ToString();
        Trainer = string.IsNullOrWhiteSpace(save.OT) ? "(no trainer name)" : save.OT;
        Detail = $"{Trainer} · ID {save.DisplayTID}";

        var played = save.PlayTimeString;
        if (!string.IsNullOrWhiteSpace(played))
            Detail += $" · {played}";

        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string Path { get; }
    public SaveFile Save { get; }
    public string Source { get; }
    public string FileName { get; }
    public string Folder { get; }
    public string Game { get; }
    public string Trainer { get; }
    public string Detail { get; }
    public DateTime Modified { get; }

    public string ModifiedText => Modified.ToString("d MMM yyyy, HH:mm");
}
