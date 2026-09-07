using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PKHeX.Mac.Services;

/// <summary>
/// Invents a Pokémon HOME tracker.
/// </summary>
/// <remarks>
/// HOME issues each tracker exactly once, and the integrity audit treats several Pokémon
/// sharing one as conclusive evidence of editing — so anything this app hands out has to
/// be drawn from a source that will not repeat. A value of zero means "no tracker", so it
/// is never returned.
/// </remarks>
public static class HomeTracker
{
    public static ulong NewRandom()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        return value == 0 ? 1 : value;
    }
}
