using System;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// Crown Tundra content for Sword/Shield: the Max Lair (Dynamax Adventures) and the
/// Galarian Star Tournament.
/// </summary>
/// <remarks>
/// Most of these blocks have no name in PKHeX. They were recovered by brute-forcing the
/// FNV-1a-64 hash the game uses for block keys, so this file looks every block up by its
/// internal name — through the same hashing lookup the engine itself uses — rather than
/// pasting a magic constant. <c>CrownTundraTests</c> pins each name against the keys
/// PKHeX does document.
/// </remarks>
public partial class CrownTundraViewModel : ObservableObject
{
    private readonly SAV8SWSH? _sav;
    private readonly Action _onChanged;
    private bool _loading;

    /// <summary>The game shows streaks as three digits; the endless record grows further.</summary>
    private const int MaxStreak = 999;
    private const int MaxEndlessStreak = 999_999;

    /// <summary>Seed the game uses to pick rentals and the encounters along an adventure.</summary>
    private const uint KSeed = SaveBlockAccessor8SWSH.KMaxLairRentalChoiceSeed;
    private const uint KDisconnect = SaveBlockAccessor8SWSH.KMaxLairDisconnectStreak;
    private const uint KEndless = SaveBlockAccessor8SWSH.KMaxLairEndlessStreak;
    private const uint KNoted1 = SaveBlockAccessor8SWSH.KMaxLairSpeciesID1Noted;
    private const uint KNoted2 = SaveBlockAccessor8SWSH.KMaxLairSpeciesID2Noted;
    private const uint KNoted3 = SaveBlockAccessor8SWSH.KMaxLairSpeciesID3Noted;
    private const uint KPeoniaHint = SaveBlockAccessor8SWSH.KMaxLairPeoniaSpeciesHint;

    /// <summary>
    /// FSYS_CHIKA_LEGEND_NN, in the game's own order — which is not PKHeX's listing order.
    /// Index 46 has no species mapped to it and no PKHeX constant; it is shown as unknown
    /// rather than hidden, because the block genuinely exists in retail saves.
    /// </summary>
    private static readonly ushort[] LegendSpecies =
    [
        0,   // 1-based; index 0 unused
        145, 146, 144, 150, 245, 244, 243, 249, 250, 380, // 01-10
        381, 383, 382, 384, 480, 482, 481, 483, 484, 487, // 11-20
        485, 488, 641, 642, 645, 643, 644, 646, 716, 717, // 21-30
        718, 785, 786, 787, 788, 791, 792, 800, 793, 794, // 31-40
        795, 796, 798, 797, 799, 0, 806, 805,             // 41-48 (46 unmapped)
    ];

    /// <summary>Galarian Star Tournament partners, by the game's romanised Japanese names.</summary>
    private static readonly (string Internal, string Display)[] TournamentPartners =
    [
        ("DANDE", "Leon"), ("HOP", "Hop"), ("KABU", "Kabu"), ("KIBANA", "Raihan"),
        ("KURARA", "Klara"), ("MAKUWA", "Gordie"), ("MERON", "Melony"), ("NEZU", "Piers"),
        ("ONION", "Allister"), ("PIONI", "Peony"), ("SEIBORI", "Avery"), ("YARO", "Milo"),
        ("SAITO", "Bea"), ("SIRUDHI", "Shielbert"),
    ];

    public CrownTundraViewModel(SaveFile sav, GameStrings strings, Action onChanged)
    {
        _onChanged = onChanged;
        if (sav is not SAV8SWSH swsh || !swsh.Blocks.TryGetBlock(KSeed, out _))
            return;

        _sav = swsh;
        _loading = true;

        SeedText = FormatSeed(GetU64(KSeed));
        DisconnectStreak = (int)GetU32(KDisconnect);
        EndlessStreak = (int)GetU32(KEndless);
        NotedSpecies = DescribeNoted(strings);
        PeoniaHint = DescribeSpecies(strings, GetU32(KPeoniaHint));

        for (int i = 1; i < LegendSpecies.Length; i++)
        {
            var species = LegendSpecies[i];
            var label = species == 0
                ? $"Slot {i:00} (unidentified)"
                : strings.specieslist[species];
            Add(Legendaries, $"FSYS_CHIKA_LEGEND_{i:00}", label);
        }

        Add(UltraBeasts, "FSYS_CHIKA_UB_OPEN", "Ultra Beasts appear in Dynamax Adventures");
        Add(Tournament, "FSYS_GST_OPEN", "Tournament unlocked");
        foreach (var (internalName, display) in TournamentPartners.OrderBy(t => t.Display, StringComparer.Ordinal))
            Add(Tournament, $"FSYS_GST_{internalName}", display);

        _loading = false;
        IsSupported = true;
        RefreshSummary();
    }

    private void Add(ObservableCollection<BlockFlagViewModel> into, string name, string label)
    {
        if (_sav is null || !_sav.Blocks.TryGetBlock(name, out var block))
            return;
        if (!block.Type.IsBoolean())
            return;
        into.Add(new BlockFlagViewModel(block, label, name, OnFlagChanged));
    }

    public bool IsSupported { get; }

    /// <summary>The 48 once-per-save legendary capture flags.</summary>
    public ObservableCollection<BlockFlagViewModel> Legendaries { get; } = [];
    public ObservableCollection<BlockFlagViewModel> UltraBeasts { get; } = [];
    public ObservableCollection<BlockFlagViewModel> Tournament { get; } = [];

    [ObservableProperty] private string _seedText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeedError))]
    private string _seedError = string.Empty;

    public bool HasSeedError => SeedError.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntryFee))]
    private int _disconnectStreak;
    [ObservableProperty] private int _endlessStreak;
    [ObservableProperty] private string _notedSpecies = string.Empty;
    [ObservableProperty] private string _peoniaHint = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True while the entry fee applies, which the game charges from three disconnects.</summary>
    public bool HasEntryFee => DisconnectStreak >= 3;

    /// <summary>Whether this save carries the Galarian Star Tournament blocks at all.</summary>
    public bool HasTournament => Tournament.Count > 0;

    private void OnFlagChanged()
    {
        RefreshSummary();
        _onChanged();
    }

    private void RefreshSummary()
    {
        var caught = Legendaries.Count(l => l.Value);
        Summary = $"{caught} of {Legendaries.Count} legendary Pokémon caught in the Max Lair";
    }

    // ── the rescue ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Frees a save that can never finish its first Dynamax Adventure.
    /// </summary>
    /// <remarks>
    /// The seed only advances when a run <em>completes</em>. If a run cannot complete — an
    /// emulator that hangs loading one particular Pokémon, say — the seed never rerolls and
    /// every retry regenerates the identical encounters, so the save is stuck for good.
    /// Rerolling breaks the loop by drawing a different path in. The tutorial's boss is fixed
    /// by the story script, so this does not change which legendary you are sent after.
    /// </remarks>
    [RelayCommand]
    public void FixStuckAdventure()
    {
        if (_sav is null)
            return;
        RerollSeed();
        DisconnectStreak = 0;
        Status = "New encounter path rolled and the entry fee cleared. "
               + "Start the adventure again — you will meet a different set of Pokémon on the way in.";
    }

    /// <summary>Draws a new adventure path without touching anything else.</summary>
    [RelayCommand]
    public void RerollSeed()
    {
        if (_sav is null)
            return;
        Span<byte> bytes = stackalloc byte[8];
        Random.Shared.NextBytes(bytes);
        var seed = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        _sav.Blocks.GetBlock(KSeed).SetValue(seed);
        _loading = true;
        SeedText = FormatSeed(seed);
        _loading = false;
        Status = "New encounter path rolled.";
        _onChanged();
    }

    /// <summary>Makes every legendary catchable again by clearing all 48 capture flags, as one change.</summary>
    [RelayCommand]
    public void ClearAllCaught()
    {
        foreach (var flag in Legendaries)
            flag.SetQuietly(false);
        RefreshSummary();
        Status = "Every legendary is available to catch again.";
        _onChanged();
    }

    // ── property plumbing ─────────────────────────────────────────────────────────

    partial void OnSeedTextChanged(string value)
    {
        if (_loading || _sav is null)
            return;
        if (!TryParseSeed(value, out var seed))
        {
            SeedError = "Seeds are 16 hex digits.";
            return;
        }
        SeedError = string.Empty;
        _sav.Blocks.GetBlock(KSeed).SetValue(seed);
        _onChanged();
    }

    partial void OnDisconnectStreakChanged(int value)
    {
        if (_loading || _sav is null)
            return;
        _sav.Blocks.GetBlock(KDisconnect).SetValue((uint)Math.Clamp(value, 0, MaxStreak));
        _onChanged();
    }

    partial void OnEndlessStreakChanged(int value)
    {
        if (_loading || _sav is null)
            return;
        _sav.Blocks.GetBlock(KEndless).SetValue((uint)Math.Clamp(value, 0, MaxEndlessStreak));
        _onChanged();
    }

    private ulong GetU64(uint key) => Convert.ToUInt64(_sav!.Blocks.GetBlock(key).GetValue(), CultureInfo.InvariantCulture);
    private uint GetU32(uint key) => Convert.ToUInt32(_sav!.Blocks.GetBlock(key).GetValue(), CultureInfo.InvariantCulture);

    private static string FormatSeed(ulong seed) => $"0x{seed:X16}";

    /// <summary>Accepts the hex form shown in the box, with or without the 0x prefix.</summary>
    public static bool TryParseSeed(string text, out ulong seed)
    {
        seed = 0;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];
        return trimmed.Length > 0
               && ulong.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed);
    }

    private string DescribeNoted(GameStrings strings)
    {
        var noted = new[] { GetU32(KNoted1), GetU32(KNoted2), GetU32(KNoted3) }
            .Where(s => s != 0)
            .Select(s => DescribeSpecies(strings, s))
            .ToArray();
        return noted.Length == 0 ? "none noted" : string.Join(", ", noted);
    }

    private static string DescribeSpecies(GameStrings strings, uint species) =>
        species == 0 || species >= strings.specieslist.Length ? "none" : strings.specieslist[species];
}

/// <summary>One boolean save block, presented as a checkbox.</summary>
public partial class BlockFlagViewModel : ObservableObject
{
    private readonly SCBlock _block;
    private readonly Action _onChanged;
    private bool _loading;

    public BlockFlagViewModel(SCBlock block, string label, string internalName, Action onChanged)
    {
        _block = block;
        _onChanged = onChanged;
        Label = label;
        InternalName = internalName;
        _value = block.Type == SCTypeCode.Bool2;
    }

    public string Label { get; }

    /// <summary>Shown as a tooltip so the underlying block stays traceable.</summary>
    public string InternalName { get; }

    [ObservableProperty] private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_loading)
            return;
        var wanted = value ? SCTypeCode.Bool2 : SCTypeCode.Bool1;
        if (_block.Type == wanted)
            return;
        _block.ChangeBooleanType(wanted);
        _onChanged();
    }

    /// <summary>Writes the flag without reporting it, for bulk edits that report once.</summary>
    internal void SetQuietly(bool value)
    {
        _block.ChangeBooleanType(value ? SCTypeCode.Bool2 : SCTypeCode.Bool1);
        _loading = true;
        Value = value;
        _loading = false;
    }
}
