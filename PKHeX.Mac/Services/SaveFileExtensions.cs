using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>Walks a save's storage without every caller re-typing the two nested loops.</summary>
public static class SaveFileExtensions
{
    /// <summary>
    /// Every occupied slot, boxes first and then the party. Entities are copies, so the
    /// result can be handed to another thread while the save keeps being edited.
    /// </summary>
    public static IEnumerable<StoredSlot> EnumerateOccupiedSlots(this SaveFile sav)
    {
        if (sav.HasBox)
        {
            for (int box = 0; box < sav.BoxCount; box++)
            {
                for (int slot = 0; slot < sav.BoxSlotCount; slot++)
                {
                    var pk = sav.GetBoxSlotAtIndex(box, slot);
                    if (pk.Species != 0)
                        yield return new StoredSlot(pk, box, slot);
                }
            }
        }
        if (sav.HasParty)
        {
            for (int i = 0; i < sav.PartyCount; i++)
            {
                var pk = sav.GetPartySlotAtIndex(i);
                if (pk.Species != 0)
                    yield return new StoredSlot(pk, StoredSlot.PartyBox, i);
            }
        }
    }

    /// <summary>The first empty slot in a box, or -1 when it is full.</summary>
    public static int FindFirstEmptySlot(this SaveFile sav, int box)
    {
        if (!sav.HasBox || (uint)box >= sav.BoxCount)
            return -1;
        for (int slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            if (sav.GetBoxSlotAtIndex(box, slot).Species == 0)
                return slot;
        }
        return -1;
    }
}

/// <summary>A copy of one stored Pokémon and where it came from.</summary>
public readonly record struct StoredSlot(PKM Entity, int Box, int Slot)
{
    /// <summary>The value of <see cref="Box"/> for party members.</summary>
    public const int PartyBox = -1;

    public bool IsParty => Box == PartyBox;

    /// <summary>"Box 3, slot 12" or "Party, slot 2".</summary>
    public string Location => IsParty ? $"Party, slot {Slot + 1}" : $"Box {Box + 1}, slot {Slot + 1}";
}
