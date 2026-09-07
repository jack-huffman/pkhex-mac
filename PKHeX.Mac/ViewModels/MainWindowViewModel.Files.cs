using System;
using System.Globalization;
using System.IO;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>Loading and exporting the save, and moving Pokémon files in and out of it.</summary>
public sealed partial class MainWindowViewModel
{
    public bool LoadSave(string path, out string error)
    {
        error = string.Empty;
        try
        {
            if (!SaveUtil.TryGetSaveFile(path, out var sav))
            {
                error = "This file is not a recognized Pokémon save file.\nMake sure it is decrypted (exported with Checkpoint, JKSM, or a save manager).";
                return false;
            }

            _sav = sav;
            _savPath = path;
            sav.Metadata.SetExtraInfo(path);
            // The engine's own deep copy, taken before anything can touch the save, is
            // what every revert restores from.
            _pristine = sav.Clone();
            SaveState.Reset();          // a different save: previous edits are moot
            _sources = new FilteredGameDataSource(sav, GameInfo.Sources);
            // Still set the global, because parts of PKHeX.Core consult it.
            GameInfo.FilteredSources = _sources;
            Detail.SetContext(sav, _sources);
            HasSave = true;
            HasParty = sav.HasParty;

            TrainerName = sav.OT;
            GameName = GameInfo.GetVersionName(sav.Version);
            GenerationText = $"Generation {sav.Generation}";
            RefreshTrainerCard();
            WindowTitle = $"PKHeX for Mac — {Path.GetFileName(path)} ({GameName})";
            StatusText = $"Loaded {Path.GetFileName(path)} · {GameName} · OT: {sav.OT}";

            BoxNames.Clear();
            if (sav.HasBox)
            {
                foreach (var n in BoxUtil.GetBoxNames(sav))
                    BoxNames.Add(n);
            }

            RebuildBoxSlots();
            CurrentBox = 0;
            CurrentBoxName = BoxNames.Count > 0 ? BoxNames[0] : string.Empty;
            LoadBox(0);
            LoadParty();
            SelectSlot(null);
            Preview.Load(null);
            LoadRideSlot();
            BuildEditors(sav);

            if (Settings is { } settings)
            {
                settings.NoteOpened(path);
                // Only restore the box if this is the same save it was recorded against.
                if (string.Equals(settings.LastSavePath, path, StringComparison.OrdinalIgnoreCase)
                    && (uint)settings.LastBox < sav.BoxCount)
                    CurrentBox = settings.LastBox;
                settings.LastSavePath = path;
                SettingsChanged?.Invoke();
            }
            BuildPaletteEntries();
            CurrentView = "boxes";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or IndexOutOfRangeException)
        {
            error = $"Failed to load save file:\n{ex.Message}";
            return false;
        }
    }

    public bool ExportSave(string path, out string error)
    {
        error = string.Empty;
        if (_sav is null)
        {
            error = "No save file loaded.";
            return false;
        }
        try
        {
            // Back up whatever is already there before overwriting it. Writing a
            // corrupt save over the only copy would cost real playtime.
            var backup = TryBackup(path);
            var data = _sav.Write();
            File.WriteAllBytes(path, data.ToArray());
            _savPath = path;
            StatusText = backup is null
                ? $"Saved to {Path.GetFileName(path)}"
                : $"Saved to {Path.GetFileName(path)} (previous version kept as {Path.GetFileName(backup)})";
            SaveState.MarkSaved(Path.GetFileName(path));
            // The file now matches memory, so that becomes the state a revert returns to.
            _pristine = _sav.Clone();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Failed to write save file:\n{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Copies an existing save aside before it is overwritten. Returns the backup
    /// path, or null when there was nothing to back up. Never throws — a failed
    /// backup must not block the export.
    /// </summary>
    private static string? TryBackup(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var dir = Path.GetDirectoryName(path) ?? ".";
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var backup = Path.Combine(dir, $"{Path.GetFileName(path)}.{stamp}.bak");
            File.Copy(path, backup, overwrite: false);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- Individual Pokémon files ----

    /// <summary>Imports a .pk*/.pb*/etc entity file into a slot, converting format if needed.</summary>
    public bool ImportEntityFile(SlotViewModel slot, string path, out string message)
    {
        if (_sav is null)
        {
            message = "No save loaded.";
            return false;
        }
        var pk = EntityFiles.Read(path, _sav, out message);
        if (pk is null)
            return false;
        WriteSlot(slot, pk);
        RefreshSlotViews();
        SelectSlot(slot.IsParty ? PartySlots[slot.Slot] : BoxSlots[slot.Slot]);
        message = $"Imported {_strings.SpeciesName(pk)}.";
        NoteChange(message);
        return true;
    }

    /// <summary>Writes the Pokémon in a slot to a file in PKHeX's decrypted party layout.</summary>
    public bool ExportEntityFile(SlotViewModel slot, string path, out string message)
    {
        var pk = ReadSlot(slot);
        if (pk is null || pk.Species == 0)
        {
            message = "That slot is empty.";
            return false;
        }
        if (!EntityFiles.Write(pk, path, out message))
            return false;
        StatusText = $"Exported {_strings.SpeciesName(pk)} to {Path.GetFileName(path)}.";
        return true;
    }

    /// <summary>Writes every Pokémon in the current box to a folder.</summary>
    public int DumpToFolder(string folder)
    {
        if (_sav is null)
            return 0;
        var written = EntityFiles.DumpBox(_sav, CurrentBox, folder);
        StatusText = $"Exported {written} Pokémon to {Path.GetFileName(folder)}.";
        return written;
    }

    /// <summary>Loads every readable entity file in a folder into the current box's free slots.</summary>
    public FolderImportResult LoadFromFolder(string folder)
    {
        if (_sav is null)
            return default;
        var result = EntityFiles.LoadFolder(_sav, CurrentBox, folder);
        RefreshSlotViews();
        var summary = $"Imported {result.Loaded} Pokémon into {CurrentBoxName}"
                      + (result.Skipped > 0 ? $" ({result.Skipped} file(s) skipped)" : string.Empty) + ".";
        if (result.Loaded > 0)
            NoteChange(summary);
        else
            StatusText = summary;
        return result;
    }

    public string? GetSlotShowdownText(SlotViewModel slot)
    {
        var pk = ReadSlot(slot);
        return pk is null || pk.Species == 0 ? null : new ShowdownSet(pk).Text;
    }
}
