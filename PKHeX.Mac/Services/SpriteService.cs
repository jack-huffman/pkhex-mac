using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Resolves and caches sprite bitmaps for Pokémon, balls, and items from the bundled PNG assets.
/// Follows the same resource naming convention as PKHeX's sprite resources.
/// </summary>
public static class SpriteService
{
    private const string AssetRoot = "avares://PKHeX.Mac/Assets/img";
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    // Species whose sprite ignores the current form.
    private static readonly HashSet<ushort> DefaultFormSprite =
    [
        (ushort)Species.Mothim, (ushort)Species.Scatterbug, (ushort)Species.Spewpa,
        (ushort)Species.Rockruff, (ushort)Species.Mimikyu, (ushort)Species.Sinistea,
        (ushort)Species.Polteageist, (ushort)Species.Urshifu, (ushort)Species.Dudunsparce,
        (ushort)Species.Poltchageist, (ushort)Species.Sinistcha,
    ];

    // Species with a gender-specific sprite (female variant suffixed with 'f').
    private static readonly HashSet<ushort> GenderedSprite =
    [
        (ushort)Species.Hippopotas, (ushort)Species.Hippowdon, (ushort)Species.Unfezant,
        (ushort)Species.Frillish, (ushort)Species.Jellicent, (ushort)Species.Pyroar,
    ];

    public static Bitmap? GetPokemonSprite(PKM pk) =>
        pk.Species == 0 ? null : GetSprite(pk.Species, pk.Form, pk.Gender, pk is IFormArgument fa ? fa.FormArgument : 0, pk.IsShiny, pk.Context);

    public static Bitmap? GetPokemonArtwork(PKM pk) =>
        pk.Species == 0 ? null : GetSprite(pk.Species, pk.Form, pk.Gender, pk is IFormArgument fa ? fa.FormArgument : 0, pk.IsShiny, pk.Context, artwork: true);

    public static Bitmap? GetSprite(ushort species, byte form, byte gender, uint formArg, bool shiny, EntityContext context, bool artwork = false)
    {
        var name = BuildName(species, form, gender, formArg, context);
        if (artwork)
        {
            return LoadSet("artwork", "artwork-shiny", "a", name, species, shiny)
                ?? LoadSet("big", "big-shiny", "b", name, species, shiny);
        }
        // The "big" pixel-sprite set only covers species <= 905; newer species
        // (Gen 9+) only exist as artwork, which we downscale to slot size.
        return LoadSet("big", "big-shiny", "b", name, species, shiny)
            ?? LoadArtworkScaledToSlot(name, species, shiny);
    }

    private static Bitmap? LoadSet(string folder, string shinyFolder, string prefix, string name, ushort species, bool shiny)
    {
        if (shiny)
        {
            var bmp = Load($"{shinyFolder}/{prefix}{name}s.png");
            if (bmp is not null)
                return bmp;
        }
        return Load($"{folder}/{prefix}{name}.png")
            ?? Load($"{folder}/{prefix}_{species}.png"); // fallback: base form
    }

    private static readonly Dictionary<string, Bitmap?> ScaledCache = new();

    private static Bitmap? LoadArtworkScaledToSlot(string name, ushort species, bool shiny)
    {
        var key = $"{name}:{shiny}";
        if (ScaledCache.TryGetValue(key, out var cached))
            return cached;

        var art = LoadSet("artwork", "artwork-shiny", "a", name, species, shiny);
        Bitmap? result = null;
        if (art is not null)
        {
            // Fit within 2x slot sprite size (136x112) preserving aspect ratio,
            // so it renders crisply on Retina displays at 68x56 logical.
            const double maxW = 136, maxH = 112;
            var size = art.PixelSize;
            var scale = Math.Min(maxW / size.Width, maxH / size.Height);
            var target = new Avalonia.PixelSize(
                Math.Max(1, (int)(size.Width * scale)),
                Math.Max(1, (int)(size.Height * scale)));
            result = art.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        }
        ScaledCache[key] = result;
        return result;
    }

    public static Bitmap? GetBallSprite(byte ball) =>
        ball == 0 ? null : Load($"ball/_ball{ball}.png");

    public static Bitmap? GetItemSprite(int item) =>
        item <= 0 ? null : Load($"items/bitem_{item}.png") ?? Load($"items-artwork/aitem_{item}.png");

    public static Bitmap? GetOverlay(string overlayName) => Load($"overlays/{overlayName}.png");

    public static Bitmap? GetMisc(string fileName) => Load($"{fileName}.png");

    private static string BuildName(ushort species, byte form, byte gender, uint formArg, EntityContext context)
    {
        if (DefaultFormSprite.Contains(species))
            form = 0;
        if (species == (ushort)Species.Xerneas && context == EntityContext.Gen9a)
            form = 1;

        var name = $"_{species}";
        if (form != 0)
        {
            name += $"-{form}";
            if (species == (ushort)Species.Pikachu)
            {
                if (context == EntityContext.Gen6)
                    name += "c";
                else if (form == 8)
                    name += "p";
            }
            else if (species == (ushort)Species.Eevee && form == 1)
            {
                name += "p";
            }
        }
        if (gender == 1 && GenderedSprite.Contains(species))
            name += "f";

        if (species == (ushort)Species.Alcremie)
        {
            if (form == 0)
                name += $"-{form}";
            name += $"-{formArg}";
        }
        return name;
    }

    private static Bitmap? Load(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var cached))
            return cached;

        var uri = new Uri($"{AssetRoot}/{relativePath}");
        Bitmap? bmp = null;
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            bmp = new Bitmap(stream);
        }
        Cache[relativePath] = bmp;
        return bmp;
    }
}
