using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Platform;

namespace PKHeX.Mac.Services;

/// <summary>
/// Battle metadata for moves: power, accuracy, damage category and a short
/// description.
/// </summary>
/// <remarks>
/// PKHeX.Core deliberately carries only each move's type and PP — it is a legality
/// engine and has no use for battle numbers. This service reads the supplementary
/// table produced by <c>scripts/fetch-move-data.sh</c> (bundled as an asset).
/// </remarks>
public static class MoveDataService
{
    public enum Category { Unknown, Physical, Special, Status }

    public readonly record struct MoveFacts(int? Power, int? Accuracy, Category Category, string Description,
                                           int MinHits = 1, int MaxHits = 1, int CritRate = 0)
    {
        /// <summary>True when the move strikes more than once per use.</summary>
        public bool IsMultiHit => MaxHits > 1;

        /// <summary>"3 hits" or "2–5 hits", for display beside the base power.</summary>
        public string HitsText => !IsMultiHit
            ? string.Empty
            : MinHits == MaxHits ? $"{MinHits} hits" : $"{MinHits}–{MaxHits} hits";

        /// <summary>
        /// Expected number of hits. A fixed count is exact; the 2–5 spread uses the
        /// modern distribution (35/35/15/15), which averages 3.1 rather than 3.5.
        /// </summary>
        public double ExpectedHits => MinHits == MaxHits
            ? MinHits
            : MinHits == 2 && MaxHits == 5 ? 3.1 : (MinHits + MaxHits) / 2.0;

        /// <summary>
        /// Average damage multiplier from critical hits. PokeAPI grades the rate in
        /// stages; anything at stage 3 or above always crits.
        /// </summary>
        public double CritMultiplier => CritRate switch
        {
            <= 0 => 1.0,        // baseline 1/24 chance, not worth modelling
            1 => 1.0625,        // ~12.5% chance of a 1.5x hit
            2 => 1.25,          // ~50%
            _ => 1.5,           // always crits
        };

        /// <summary>
        /// Power actually delivered per use, folding in hit count and crit rate. A
        /// 25-power three-hit move that always crits lands like 112, and ranking it
        /// as 25 badly understates it.
        /// </summary>
        public double EffectivePower => (Power ?? 0) * ExpectedHits * CritMultiplier;

        public string PowerText => Power?.ToString(CultureInfo.InvariantCulture) ?? "—";
        public string AccuracyText => Accuracy?.ToString(CultureInfo.InvariantCulture) ?? "—";

        public string CategoryLabel => Category switch
        {
            Category.Physical => "PHY",
            Category.Special => "SPE",
            Category.Status => "STA",
            _ => "",
        };
    }

    private static readonly Lazy<Dictionary<int, MoveFacts>> Table = new(Load);

    public static IBrush BrushFor(Category category) => category switch
    {
        Category.Physical => Palette.Physical,
        Category.Special => Palette.Special,
        _ => Palette.Status,
    };

    /// <summary>Facts for a move id; empty values when the table has no entry.</summary>
    public static MoveFacts Get(ushort move) =>
        Table.Value.TryGetValue(move, out var facts)
            ? facts
            : new MoveFacts(null, null, Category.Unknown, string.Empty);

    public static bool HasData => Table.Value.Count > 0;

    private static Dictionary<int, MoveFacts> Load()
    {
        var result = new Dictionary<int, MoveFacts>();
        try
        {
            var uri = new Uri("avares://PKHeX.Mac/Assets/movedata.json");
            if (!AssetLoader.Exists(uri))
                return result;
            using var stream = AssetLoader.Open(uri);
            using var doc = JsonDocument.Parse(stream);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out var id))
                    continue;
                var e = property.Value;
                var minHits = ReadNullableInt(e, "hl") ?? 1;
                var maxHits = ReadNullableInt(e, "hh") ?? minHits;
                result[id] = new MoveFacts(
                    ReadNullableInt(e, "p"),
                    ReadNullableInt(e, "a"),
                    ParseCategory(e.TryGetProperty("c", out var c) ? c.GetString() : null),
                    (e.TryGetProperty("d", out var d) ? d.GetString() : null) ?? string.Empty,
                    minHits,
                    maxHits,
                    ReadNullableInt(e, "cr") ?? 0);
            }
        }
        catch
        {
            // Missing or malformed table: the editor simply shows no battle numbers.
        }
        return result;
    }

    private static int? ReadNullableInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static Category ParseCategory(string? value) => value switch
    {
        "physical" => Category.Physical,
        "special" => Category.Special,
        "status" => Category.Status,
        _ => Category.Unknown,
    };
}
