using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// The names PKHeX knows for a save's SCBlocks, keyed by block key.
/// </summary>
/// <remarks>
/// Roughly a fifth of Scarlet/Violet's blocks are named; the rest are bare hashes. The
/// metadata is built by reflection over the game's accessor and can fail on an unusual
/// save, in which case an empty map is returned and callers show keys instead.
/// </remarks>
public static class SCBlockNames
{
    public static IReadOnlyDictionary<uint, string> For(ISCBlockArray array)
    {
        var map = new Dictionary<uint, string>();
        try
        {
            var meta = new SCBlockMetadata(array.Accessor, [], []);
            foreach (var block in array.AllBlocks)
            {
                if (meta.GetBlockName(block, out _) is { } name)
                    map[block.Key] = name;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Names are a nicety; the callers work on keys alone.
        }
        return map;
    }
}
