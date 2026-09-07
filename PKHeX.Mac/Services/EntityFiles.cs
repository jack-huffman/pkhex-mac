using System;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Reading and writing individual Pokémon files — <c>.pk9</c>, <c>.pb8</c>, <c>.ek*</c> and the
/// rest — against an open save.
/// </summary>
/// <remarks>
/// PKHeX.Core knows every format and every conversion route; this only adds the file
/// system and the save's point of view: a file is useful here only once it is in this
/// save's own format. Nothing in here touches a slot; callers decide where a result lands.
/// </remarks>
public static class EntityFiles
{
    /// <summary>
    /// Upper bound on a plausible entity file. Saves start in the tens of kilobytes, so
    /// anything larger is skipped without being read.
    /// </summary>
    private const long MaxEntityFileLength = 0x400;

    /// <summary>
    /// Whether the path's extension names an entity format PKHeX understands, including the
    /// encrypted <c>.ek*</c> twins of the <c>.pk*</c> formats.
    /// </summary>
    public static bool HasEntityExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        if (ext.Length == 0)
            return false;
        if (ext.Length is 3 or 4 && ext.StartsWith("ek", StringComparison.OrdinalIgnoreCase))
            return true;
        return EntityFileExtension.GetExtensions().Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads an entity file and converts it to the save's format. Returns null with a reason
    /// when the file is unreadable, not a Pokémon, or has no route into this game.
    /// </summary>
    public static PKM? Read(string path, SaveFile sav, out string error)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length is 0 or > MaxEntityFileLength)
            {
                error = "That file is not the size of a Pokémon.";
                return null;
            }
            var data = File.ReadAllBytes(path);
            var prefer = EntityFileExtension.GetContextFromExtension(path, sav.Context);
            var pk = EntityFormat.GetFromBytes(data, prefer);
            if (pk is null)
            {
                error = "Not a recognizable Pokémon entity file.";
                return null;
            }
            return ConvertFor(pk, sav, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>The entity in the save's own format, converting when the formats differ.</summary>
    public static PKM? ConvertFor(PKM pk, SaveFile sav, out string error)
    {
        error = string.Empty;
        if (pk.GetType() == sav.PKMType)
            return pk;
        var converted = EntityConverter.ConvertToType(pk, sav.PKMType, out var result);
        if (converted is null)
            error = $"Cannot convert to this save's format: {result}";
        return converted;
    }

    /// <summary>Writes one Pokémon as a decrypted party-format file, the layout PKHeX exports.</summary>
    public static bool Write(PKM pk, string path, out string error)
    {
        error = string.Empty;
        try
        {
            var data = new byte[pk.SIZE_PARTY];
            pk.WriteDecryptedDataParty(data);
            File.WriteAllBytes(path, data);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Writes every Pokémon in a box to a folder, one file each, never overwriting: the
    /// same species and nickname can repeat within a box.
    /// </summary>
    /// <returns>How many files were written.</returns>
    public static int DumpBox(SaveFile sav, int box, string folder)
    {
        if (!sav.HasBox || (uint)box >= sav.BoxCount)
            return 0;
        var written = 0;
        for (int slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            var pk = sav.GetBoxSlotAtIndex(box, slot);
            if (pk.Species == 0)
                continue;
            var path = UniquePath(folder, PathUtil.CleanFileName(pk.FileName));
            if (Write(pk, path, out _))
                written++;
        }
        return written;
    }

    /// <summary>
    /// Loads every readable entity file in a folder into a box's free slots, in file-name
    /// order, stopping when the box is full.
    /// </summary>
    public static FolderImportResult LoadFolder(SaveFile sav, int box, string folder)
    {
        if (!sav.HasBox || (uint)box >= sav.BoxCount || !Directory.Exists(folder))
            return default;

        int loaded = 0, skipped = 0;
        var next = 0;
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal))
        {
            var pk = Read(file, sav, out _);
            if (pk is null || pk.Species == 0)
            {
                skipped++;
                continue;
            }
            while (next < sav.BoxSlotCount && sav.GetBoxSlotAtIndex(box, next).Species != 0)
                next++;
            if (next >= sav.BoxSlotCount)
                break;
            pk.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pk, box, next);
            loaded++;
            next++;
        }
        return new FolderImportResult(loaded, skipped);
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; File.Exists(path); suffix++)
            path = Path.Combine(folder, $"{stem} ({suffix}){extension}");
        return path;
    }
}

/// <summary>How a folder import went: files that became Pokémon, and files that did not.</summary>
public readonly record struct FolderImportResult(int Loaded, int Skipped);
