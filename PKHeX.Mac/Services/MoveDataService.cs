using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
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

    public readonly record struct MoveFacts(int? Power, int? Accuracy, Category Category, string Description)
    {
        public string PowerText => Power?.ToString() ?? "—";
        public string AccuracyText => Accuracy?.ToString() ?? "—";

        public string CategoryLabel => Category switch
        {
            Category.Physical => "PHY",
            Category.Special => "SPE",
            Category.Status => "STA",
            _ => "",
        };
    }

    private static readonly Lazy<Dictionary<int, MoveFacts>> Table = new(Load);

    public static readonly IBrush PhysicalBrush = new SolidColorBrush(Color.Parse("#E0733D"));
    public static readonly IBrush SpecialBrush = new SolidColorBrush(Color.Parse("#5C8FD6"));
    public static readonly IBrush StatusBrush = new SolidColorBrush(Color.Parse("#8E8E93"));

    public static IBrush BrushFor(Category category) => category switch
    {
        Category.Physical => PhysicalBrush,
        Category.Special => SpecialBrush,
        _ => StatusBrush,
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
                result[id] = new MoveFacts(
                    ReadNullableInt(e, "p"),
                    ReadNullableInt(e, "a"),
                    ParseCategory(e.TryGetProperty("c", out var c) ? c.GetString() : null),
                    (e.TryGetProperty("d", out var d) ? d.GetString() : null) ?? string.Empty);
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
