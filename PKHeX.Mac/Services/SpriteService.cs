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

    public static Bitmap? GetPokemonSprite(PKM pk)
    {
        if (pk.Species == 0)
            return null;
        // An egg shows as an egg, whatever is inside it.
        if (pk.IsEgg)
            return GetEggSprite(slot: true);
        return GetSprite(pk.Species, pk.Form, pk.Gender, pk is IFormArgument fa ? fa.FormArgument : 0, pk.IsShiny, pk.Context);
    }

    /// <summary>
    /// The egg image. Prefers the 128px Pokémon HOME icon over PKHeX's 68x56 one;
    /// the slot variant is scaled down so it sits like every other box sprite.
    /// </summary>
    public static Bitmap? GetEggSprite(bool slot)
    {
        var home = Load("egg-home.png");
        if (home is null)
            return Load(slot ? "big/b_egg.png" : "artwork/a_egg.png");
        return slot ? ScaleToSlot(home, "egg:slot") : home;
    }

    public static Bitmap? GetPokemonArtwork(PKM pk)
    {
        if (pk.Species == 0)
            return null;
        if (pk.IsEgg)
            return GetEggSprite(slot: false);
        // Prefer the 512x512 HOME renders when present on disk. Alternate forms
        // resolve through the PokeAPI name->id map (forms.json); named base forms
        // (Maushold "Family of Three") also resolve there, so try the form lookup
        // first and fall back to the species-indexed render.
        var hires = LoadHiResForm(pk.Species, pk.Form, pk.Context, pk.IsShiny)
            ?? (PrefersFormArtwork(pk.Species, pk.Form) ? null : LoadHiRes(pk.Species, pk.IsShiny));
        if (hires is not null)
            return hires;
        return GetSprite(pk.Species, pk.Form, pk.Gender, pk is IFormArgument fa ? fa.FormArgument : 0, pk.IsShiny, pk.Context, artwork: true);
    }

    // ---- High-resolution HOME renders (on-disk, optional; see scripts/fetch-hires-sprites.sh) ----

    private static readonly Lazy<string?> HiResDir = new(ResolveHiResDir);

    private static string? ResolveHiResDir()
    {
        // Packaged app: hires/ sits next to the executable. Dev builds: walk up
        // from bin/Debug/netX.0/ to the project's Assets/hires folder.
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            System.IO.Path.Combine(baseDir, "hires"),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "Assets", "hires")),
        };
        foreach (var dir in candidates)
        {
            if (System.IO.Directory.Exists(dir))
                return dir;
        }
        return null;
    }

    private static Bitmap? LoadHiRes(ushort species, bool shiny)
    {
        if (HiResDir.Value is not { } dir)
            return null;
        var key = $"hires:{species}:{shiny}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        Bitmap? bmp = null;
        var path = shiny
            ? System.IO.Path.Combine(dir, "shiny", $"{species}.png")
            : System.IO.Path.Combine(dir, $"{species}.png");
        if (!System.IO.File.Exists(path) && shiny)
            path = System.IO.Path.Combine(dir, $"{species}.png"); // shiny render missing: use normal
        if (System.IO.File.Exists(path))
        {
            try
            {
                bmp = new Bitmap(path);
            }
            catch
            {
                bmp = null; // corrupt/partial download — fall back to bundled artwork
            }
        }
        Cache[key] = bmp;
        return bmp;
    }

    // ---- Alternate-form hi-res renders, resolved by PokeAPI name slug ----

    private static readonly Lazy<Dictionary<string, int>?> FormIdMap = new(LoadFormIdMap);
    private static readonly Lazy<GameStrings> EnglishStrings = new(() => GameInfo.GetStrings("en"));

    private static Dictionary<string, int>? LoadFormIdMap()
    {
        if (HiResDir.Value is not { } dir)
            return null;
        var path = System.IO.Path.Combine(dir, "forms.json");
        if (!System.IO.File.Exists(path))
            return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(System.IO.File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a form with no dedicated render should fall back to form-accurate
    /// low-resolution art rather than the crisp base-species render.
    /// </summary>
    /// <remarks>
    /// This used to prefer the base render for anything the appearance change was
    /// merely "cosmetic" — flower colours, Vivillon patterns, Alcremie creams, Furfrou
    /// trims. That reasoning was wrong for a save editor: the flower colour *is* how you
    /// tell one Flabébé from another, so five identical red renders are less useful than
    /// five correct smaller ones. Correct beats crisp.
    ///
    /// The exception is <see cref="DefaultFormSprite"/> — species whose forms genuinely
    /// share one appearance, where the base render is not a compromise but the right
    /// image.
    /// </remarks>
    public static bool PrefersFormArtwork(ushort species, byte form) =>
        form != 0 && !DefaultFormSprite.Contains(species);

    private static Bitmap? LoadHiResForm(ushort species, byte form, EntityContext context, bool shiny)
    {
        if (FormIdMap.Value is not { } map || HiResDir.Value is not { } dir)
            return null;
        var strings = EnglishStrings.Value;
        if (species >= strings.specieslist.Length)
            return null;
        var formNames = FormConverter.GetFormList(species, strings.types, strings.forms, GameInfo.GenderSymbolUnicode, context);
        if (form >= formNames.Length)
            return null;

        var sp = Slug(strings.specieslist[species]);
        var fo = SlugForm(formNames[form]);
        if (fo.Length == 0)
            return null;

        // PokeAPI slugs sometimes join form words ("bloodmoon"), sometimes hyphenate,
        // and some append a noun PKHeX omits: "-mask" (Ogerpon), "-breed" (Paldean
        // Tauros), "-plumage" (Squawkabilly). Explicit renames live in SlugOverrides.
        foreach (var raw in new[]
                 {
                     $"{sp}-{fo}",
                     $"{sp}-{fo.Replace("-", "")}",
                     $"{sp}-{fo}-mask",
                     $"{sp}-{fo}-breed",
                     $"{sp}-{fo}-plumage",
                     $"{sp}-{fo}-cap",
                 })
        {
            var candidate = SlugOverrides.GetValueOrDefault(raw, raw);
            if (!map.TryGetValue(candidate, out var id))
                continue;
            var key = $"hiresform:{id}:{shiny}";
            if (Cache.TryGetValue(key, out var cached))
            {
                if (cached is not null)
                    return cached;
                continue;
            }
            // Form entities have ids > 10000 and live in forms/; a few "forms"
            // are PokeAPI's base entity (Ogerpon-Teal, Maushold-Four, Deoxys-Normal)
            // whose render is the species file.
            var (folder, shinySub) = id > 10_000
                ? (System.IO.Path.Combine(dir, "forms"), System.IO.Path.Combine(dir, "forms", "shiny"))
                : (dir, System.IO.Path.Combine(dir, "shiny"));
            var path = shiny
                ? System.IO.Path.Combine(shinySub, $"{id}.png")
                : System.IO.Path.Combine(folder, $"{id}.png");
            if (!System.IO.File.Exists(path) && shiny)
                path = System.IO.Path.Combine(folder, $"{id}.png");
            Bitmap? bmp = null;
            if (System.IO.File.Exists(path))
            {
                try
                {
                    bmp = new Bitmap(path);
                }
                catch
                {
                    // partial download — ignore
                }
            }
            Cache[key] = bmp;
            if (bmp is not null)
                return bmp;
        }
        return null;
    }

    /// <summary>Lowercase, drop punctuation, gender symbols to -f/-m ("Mr. Mime" -> "mr-mime").</summary>
    private static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 2);
        foreach (var ch in name.ToLowerInvariant())
        {
            switch (ch)
            {
                case '♀': sb.Append("-f"); break;
                case '♂': sb.Append("-m"); break;
                case 'é': sb.Append('e'); break;
                case ' ' or '-' or '_':
                    if (sb.Length > 0 && sb[^1] != '-')
                        sb.Append('-');
                    break;
                default:
                    if (char.IsLetterOrDigit(ch))
                        sb.Append(ch);
                    break;
            }
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>PKHeX form names to PokeAPI region slugs ("Hisuian" -> "hisui", "♀" -> "female").</summary>
    private static string SlugForm(string formName)
    {
        var slug = Slug(formName);
        return slug switch
        {
            "alolan" => "alola",
            "galarian" => "galar",
            "hisuian" => "hisui",
            "paldean" => "paldea",
            "f" => "female",
            "m" => "male",
            _ => slug,
        };
    }

    /// <summary>Forms whose PokeAPI name differs beyond suffix conventions.</summary>
    private static readonly Dictionary<string, string> SlugOverrides = new()
    {
        ["basculin-blue"] = "basculin-blue-striped",
        ["basculin-white"] = "basculin-white-striped",
        ["darmanitan-galar"] = "darmanitan-galar-standard",
        ["zygarde-10-c"] = "zygarde-10-power-construct",
        ["zygarde-50-c"] = "zygarde-50-power-construct",
        ["rockruff-dusk"] = "rockruff-own-tempo",
        ["eiscue-noice-face"] = "eiscue-noice",
        ["pumpkaboo-jumbo"] = "pumpkaboo-super",
        ["gourgeist-jumbo"] = "gourgeist-super",
        ["ogerpon-teal"] = "ogerpon",
        ["minior-m-red"] = "minior-red-meteor",
        ["minior-m-orange"] = "minior-orange-meteor",
        ["minior-m-yellow"] = "minior-yellow-meteor",
        ["minior-m-green"] = "minior-green-meteor",
        ["minior-m-blue"] = "minior-blue-meteor",
        ["minior-m-indigo"] = "minior-indigo-meteor",
        ["minior-m-violet"] = "minior-violet-meteor",
        ["minior-c-red"] = "minior-red",
        ["minior-c-orange"] = "minior-orange",
        ["minior-c-yellow"] = "minior-yellow",
        ["minior-c-green"] = "minior-green",
        ["minior-c-blue"] = "minior-blue",
        ["minior-c-indigo"] = "minior-indigo",
        ["minior-c-violet"] = "minior-violet",
    };

    public static Bitmap? GetSprite(ushort species, byte form, byte gender, uint formArg, bool shiny, EntityContext context, bool artwork = false)
    {
        var name = BuildName(species, form, gender, formArg, context);
        if (artwork)
        {
            return LoadSet("artwork", "artwork-shiny", "a", name, species, shiny)
                ?? LoadSet("big", "big-shiny", "b", name, species, shiny);
        }

        // Box-slot sprites. For shiny requests, exhaust every shiny-colored source
        // (pixel set -> artwork set -> hi-res render, downscaled) before ever
        // settling for a base-color image.
        if (shiny)
        {
            var shinyBmp = Load($"big-shiny/b{name}s.png")
                ?? ScaleToSlot(Load($"artwork-shiny/a{name}s.png"), $"as{name}")
                ?? HiResScaledToSlot(species, form, context, shiny: true);
            if (shinyBmp is not null)
                return shinyBmp;
        }
        // The "big" pixel-sprite set only covers species <= 905; newer species
        // (Gen 9+) only exist as artwork / hi-res renders, which we downscale.
        return Load($"big/b{name}.png")
            ?? Load($"big/b_{species}.png")
            ?? ScaleToSlot(Load($"artwork/a{name}.png") ?? Load($"artwork/a_{species}.png"), $"an{name}")
            ?? HiResScaledToSlot(species, form, context, shiny: false);
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

    /// <summary>Fits a bitmap within 136x112 (2x slot sprite size, crisp on Retina).</summary>
    private static Bitmap? ScaleToSlot(Bitmap? src, string cacheKey)
    {
        if (src is null)
            return null;
        if (ScaledCache.TryGetValue(cacheKey, out var cached))
            return cached;

        const double maxW = 136, maxH = 112;
        var size = src.PixelSize;
        var scale = Math.Min(maxW / size.Width, maxH / size.Height);
        var target = new Avalonia.PixelSize(
            Math.Max(1, (int)(size.Width * scale)),
            Math.Max(1, (int)(size.Height * scale)));
        var result = src.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        ScaledCache[cacheKey] = result;
        return result;
    }

    private static Bitmap? HiResScaledToSlot(ushort species, byte form, EntityContext context, bool shiny)
    {
        // For alternate forms only a correctly-mapped form render is acceptable;
        // base-species art would show the wrong appearance in the box.
        var full = LoadHiResForm(species, form, context, shiny)
            ?? (PrefersFormArtwork(species, form) ? null : LoadHiRes(species, shiny));
        return ScaleToSlot(full, $"hr:{species}:{form}:{shiny}");
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
