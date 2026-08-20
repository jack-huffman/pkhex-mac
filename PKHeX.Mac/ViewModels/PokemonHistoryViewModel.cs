using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Mac.ViewModels;

/// <summary>
/// The "Trainer &amp; History" group of the Pokémon editor: current handler, the
/// handling-trainer block, OT/HT memories, contest stats, markings, and the
/// Pokémon HOME tracker. Operates on the same working clone as the main editor.
/// </summary>
public partial class PokemonHistoryViewModel : ObservableObject
{
    private readonly GameStrings _strings;
    private readonly MemoryStrings _memories;
    private readonly Action _markDirty;
    private PKM? _pk;
    private bool _loading;

    public PokemonHistoryViewModel(GameStrings strings, Action markDirty)
    {
        _strings = strings;
        _memories = new MemoryStrings(strings);
        _markDirty = markDirty;
        MemoryChoices = _memories.Memory;
        IntensityChoices = BuildIndexed(_memories.GetMemoryQualities());
    }

    // ---- Support flags: each group hides when the format lacks it ----
    [ObservableProperty] private bool _hasPokemon;
    [ObservableProperty] private bool _supportsHandler;
    [ObservableProperty] private bool _supportsHandlerLanguage;
    [ObservableProperty] private bool _supportsMemories;
    [ObservableProperty] private bool _supportsContest;
    [ObservableProperty] private bool _supportsMarkings;
    [ObservableProperty] private bool _supportsTracker;
    [ObservableProperty] private bool _supportsAffection;

    // ---- Current handler ----
    [ObservableProperty] private int _currentHandlerIndex;
    [ObservableProperty] private string _handlerSummary = string.Empty;

    // ---- Handling trainer ----
    [ObservableProperty] private string _htName = string.Empty;
    [ObservableProperty] private int _htGenderIndex;
    [ObservableProperty] private int _htFriendship;
    [ObservableProperty] private int _htLanguageValue;

    // ---- Memories ----
    [ObservableProperty] private int _otMemory;
    [ObservableProperty] private int _otMemoryIntensity;
    [ObservableProperty] private int _otMemoryFeeling;
    [ObservableProperty] private int _otMemoryVariable;
    [ObservableProperty] private string _otMemoryText = string.Empty;
    [ObservableProperty] private int _htMemory;
    [ObservableProperty] private int _htMemoryIntensity;
    [ObservableProperty] private int _htMemoryFeeling;
    [ObservableProperty] private int _htMemoryVariable;
    [ObservableProperty] private string _htMemoryText = string.Empty;
    [ObservableProperty] private int _otAffection;
    [ObservableProperty] private int _htAffection;

    // ---- Contest stats ----
    [ObservableProperty] private int _contestCool;
    [ObservableProperty] private int _contestBeauty;
    [ObservableProperty] private int _contestCute;
    [ObservableProperty] private int _contestSmart;
    [ObservableProperty] private int _contestTough;
    [ObservableProperty] private int _contestSheen;

    // ---- HOME tracker ----
    [ObservableProperty] private string _trackerText = string.Empty;
    [ObservableProperty] private bool _hideTracker = true;

    public IReadOnlyList<ComboItem> MemoryChoices { get; }
    public IReadOnlyList<ComboItem> IntensityChoices { get; }
    [ObservableProperty] private IReadOnlyList<ComboItem> _feelingChoices = [];
    [ObservableProperty] private IReadOnlyList<ComboItem> _languageChoices = [];
    public IReadOnlyList<string> HandlerChoices { get; } = ["Original Trainer", "Handling Trainer"];
    public IReadOnlyList<string> GenderChoices { get; } = ["♂ Male", "♀ Female"];

    /// <summary>Marking rows (6 tri-state markings for Gen 7+).</summary>
    public List<MarkingRowViewModel> Markings { get; } = [];

    public void SetLanguageChoices(IReadOnlyList<ComboItem> languages) => LanguageChoices = languages;

    public void Load(PKM? pk)
    {
        _loading = true;
        try
        {
            _pk = pk;
            HasPokemon = pk is { Species: > 0 };
            if (!HasPokemon || pk is null)
                return;

            SupportsHandler = pk.Format >= 6;
            SupportsHandlerLanguage = pk is IHandlerLanguage;
            SupportsMemories = pk is IMemoryOT or IMemoryHT;
            SupportsContest = pk is IContestStats;
            SupportsTracker = pk is IHomeTrack;
            SupportsAffection = pk is IAffection;

            CurrentHandlerIndex = pk.CurrentHandler;
            HtName = pk.HandlingTrainerName;
            HtGenderIndex = pk.HandlingTrainerGender;
            HtFriendship = pk.HandlingTrainerFriendship;
            HtLanguageValue = pk is IHandlerLanguage hl ? hl.HandlingTrainerLanguage : 0;
            RefreshHandlerSummary(pk);

            FeelingChoices = BuildIndexed(_memories.GetMemoryFeelings(pk.Format));

            if (pk is IMemoryOT mo)
            {
                OtMemory = mo.OriginalTrainerMemory;
                OtMemoryIntensity = mo.OriginalTrainerMemoryIntensity;
                OtMemoryFeeling = mo.OriginalTrainerMemoryFeeling;
                OtMemoryVariable = mo.OriginalTrainerMemoryVariable;
            }
            if (pk is IMemoryHT mh)
            {
                HtMemory = mh.HandlingTrainerMemory;
                HtMemoryIntensity = mh.HandlingTrainerMemoryIntensity;
                HtMemoryFeeling = mh.HandlingTrainerMemoryFeeling;
                HtMemoryVariable = mh.HandlingTrainerMemoryVariable;
            }
            if (pk is IAffection af)
            {
                OtAffection = af.OriginalTrainerAffection;
                HtAffection = af.HandlingTrainerAffection;
            }
            RefreshMemoryText();

            if (pk is IContestStats cs)
            {
                ContestCool = cs.ContestCool;
                ContestBeauty = cs.ContestBeauty;
                ContestCute = cs.ContestCute;
                ContestSmart = cs.ContestSmart;
                ContestTough = cs.ContestTough;
                ContestSheen = cs.ContestSheen;
            }

            BuildMarkings(pk);
            TrackerText = pk is IHomeTrack tr ? $"{tr.Tracker:X16}" : string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private static IReadOnlyList<ComboItem> BuildIndexed(ReadOnlySpan<string> values)
    {
        var list = new List<ComboItem>(values.Length);
        for (int i = 0; i < values.Length; i++)
            list.Add(new ComboItem(values[i], i));
        return list;
    }

    private void BuildMarkings(PKM pk)
    {
        Markings.Clear();
        SupportsMarkings = false;
        if (pk is IAppliedMarkings7 m7)
        {
            SupportsMarkings = true;
            for (int i = 0; i < m7.MarkingCount; i++)
                Markings.Add(new MarkingRowViewModel(this, i, MarkingNames[i], (int)m7.GetMarking(i)));
        }
        else if (pk is IAppliedMarkings<bool> mb)
        {
            SupportsMarkings = true;
            for (int i = 0; i < mb.MarkingCount; i++)
                Markings.Add(new MarkingRowViewModel(this, i, MarkingNames[i], mb.GetMarking(i) ? 1 : 0));
        }
        OnPropertyChanged(nameof(Markings));
    }

    private static readonly string[] MarkingNames = ["Circle", "Triangle", "Square", "Heart", "Star", "Diamond"];

    internal void SetMarking(int index, int value)
    {
        if (_loading || _pk is null)
            return;
        if (_pk is IAppliedMarkings7 m7)
            m7.SetMarking(index, (MarkingColor)value);
        else if (_pk is IAppliedMarkings<bool> mb)
            mb.SetMarking(index, value != 0);
        Touch();
    }

    private void RefreshHandlerSummary(PKM pk)
    {
        var ot = pk.OriginalTrainerName;
        HandlerSummary = pk.CurrentHandler == 0
            ? $"{(string.IsNullOrWhiteSpace(ot) ? "the original trainer" : ot)} currently holds this Pokémon."
            : $"{(string.IsNullOrWhiteSpace(HtName) ? "A handling trainer" : HtName)} currently holds this Pokémon (traded).";
    }

    private void RefreshMemoryText()
    {
        OtMemoryText = DescribeMemory(OtMemory, OtMemoryIntensity, OtMemoryFeeling);
        HtMemoryText = DescribeMemory(HtMemory, HtMemoryIntensity, HtMemoryFeeling);
    }

    private string DescribeMemory(int memory, int intensity, int feeling)
    {
        if (memory == 0)
            return "No memory recorded.";
        var text = _memories.Memory.Find(m => m.Value == memory)?.Text ?? $"Memory #{memory}";
        var qualities = _memories.GetMemoryQualities();
        var feelings = _memories.GetMemoryFeelings(_pk?.Format ?? 9);
        var q = (uint)intensity < qualities.Length ? qualities[intensity] : intensity.ToString();
        var f = (uint)feeling < feelings.Length ? feelings[feeling] : feeling.ToString();
        return $"{text}  ·  intensity: {q}  ·  feeling: {f}";
    }

    private void Touch()
    {
        if (_loading)
            return;
        _markDirty();
    }

    // ---- Handlers ----

    partial void OnCurrentHandlerIndexChanged(int value)
    {
        if (_loading || _pk is null || value is < 0 or > 1)
            return;
        _pk.CurrentHandler = (byte)value;
        RefreshHandlerSummary(_pk);
        Touch();
    }

    partial void OnHtNameChanged(string value)
    {
        if (_loading || _pk is null)
            return;
        _pk.HandlingTrainerName = value;
        RefreshHandlerSummary(_pk);
        Touch();
    }

    partial void OnHtGenderIndexChanged(int value)
    {
        if (_loading || _pk is null || value is < 0 or > 1)
            return;
        _pk.HandlingTrainerGender = (byte)value;
        Touch();
    }

    partial void OnHtFriendshipChanged(int value)
    {
        if (_loading || _pk is null)
            return;
        _pk.HandlingTrainerFriendship = (byte)Math.Clamp(value, 0, 255);
        Touch();
    }

    partial void OnHtLanguageValueChanged(int value)
    {
        if (_loading || _pk is not IHandlerLanguage hl || value < 0)
            return;
        hl.HandlingTrainerLanguage = (byte)value;
        Touch();
    }

    partial void OnOtMemoryChanged(int value) => SetMemory(ot: true);
    partial void OnOtMemoryIntensityChanged(int value) => SetMemory(ot: true);
    partial void OnOtMemoryFeelingChanged(int value) => SetMemory(ot: true);
    partial void OnOtMemoryVariableChanged(int value) => SetMemory(ot: true);
    partial void OnHtMemoryChanged(int value) => SetMemory(ot: false);
    partial void OnHtMemoryIntensityChanged(int value) => SetMemory(ot: false);
    partial void OnHtMemoryFeelingChanged(int value) => SetMemory(ot: false);
    partial void OnHtMemoryVariableChanged(int value) => SetMemory(ot: false);

    private void SetMemory(bool ot)
    {
        if (_loading || _pk is null)
            return;
        if (ot && _pk is IMemoryOT mo)
        {
            mo.OriginalTrainerMemory = (byte)Math.Max(0, OtMemory);
            mo.OriginalTrainerMemoryIntensity = (byte)Math.Max(0, OtMemoryIntensity);
            mo.OriginalTrainerMemoryFeeling = (byte)Math.Max(0, OtMemoryFeeling);
            mo.OriginalTrainerMemoryVariable = (ushort)Math.Max(0, OtMemoryVariable);
        }
        else if (!ot && _pk is IMemoryHT mh)
        {
            mh.HandlingTrainerMemory = (byte)Math.Max(0, HtMemory);
            mh.HandlingTrainerMemoryIntensity = (byte)Math.Max(0, HtMemoryIntensity);
            mh.HandlingTrainerMemoryFeeling = (byte)Math.Max(0, HtMemoryFeeling);
            mh.HandlingTrainerMemoryVariable = (ushort)Math.Max(0, HtMemoryVariable);
        }
        RefreshMemoryText();
        Touch();
    }

    partial void OnOtAffectionChanged(int value)
    {
        if (_loading || _pk is not IAffection af)
            return;
        af.OriginalTrainerAffection = (byte)Math.Clamp(value, 0, 255);
        Touch();
    }

    partial void OnHtAffectionChanged(int value)
    {
        if (_loading || _pk is not IAffection af)
            return;
        af.HandlingTrainerAffection = (byte)Math.Clamp(value, 0, 255);
        Touch();
    }

    private void SetContest()
    {
        if (_loading || _pk is not IContestStats cs)
            return;
        cs.ContestCool = (byte)Math.Clamp(ContestCool, 0, 255);
        cs.ContestBeauty = (byte)Math.Clamp(ContestBeauty, 0, 255);
        cs.ContestCute = (byte)Math.Clamp(ContestCute, 0, 255);
        cs.ContestSmart = (byte)Math.Clamp(ContestSmart, 0, 255);
        cs.ContestTough = (byte)Math.Clamp(ContestTough, 0, 255);
        cs.ContestSheen = (byte)Math.Clamp(ContestSheen, 0, 255);
        Touch();
    }

    partial void OnContestCoolChanged(int value) => SetContest();
    partial void OnContestBeautyChanged(int value) => SetContest();
    partial void OnContestCuteChanged(int value) => SetContest();
    partial void OnContestSmartChanged(int value) => SetContest();
    partial void OnContestToughChanged(int value) => SetContest();
    partial void OnContestSheenChanged(int value) => SetContest();

    partial void OnTrackerTextChanged(string value)
    {
        if (_loading || _pk is not IHomeTrack tr)
            return;
        // Accept partial/malformed hex without fighting the user mid-typing.
        var cleaned = value.Trim();
        if (cleaned.Length == 0)
        {
            tr.Tracker = 0;
            Touch();
            return;
        }
        if (ulong.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            tr.Tracker = parsed;
            Touch();
        }
    }

    [RelayCommand]
    public void ClearTracker() => TrackerText = "0000000000000000";

    [RelayCommand]
    public void MaxContest()
    {
        ContestCool = ContestBeauty = ContestCute = ContestSmart = ContestTough = 255;
        ContestSheen = 255;
    }

    [RelayCommand]
    public void ClearContest()
    {
        ContestCool = ContestBeauty = ContestCute = ContestSmart = ContestTough = ContestSheen = 0;
    }

    [RelayCommand]
    public void ClearMemories()
    {
        if (_pk is null)
            return;
        _loading = true;
        OtMemory = OtMemoryIntensity = OtMemoryFeeling = OtMemoryVariable = 0;
        HtMemory = HtMemoryIntensity = HtMemoryFeeling = HtMemoryVariable = 0;
        _loading = false;
        if (_pk is IMemoryOT mo)
            mo.ClearMemoriesOT();
        if (_pk is IMemoryHT mh)
            mh.ClearMemoriesHT();
        RefreshMemoryText();
        Touch();
    }
}

/// <summary>One marking (Circle/Triangle/…) as None / Blue / Pink.</summary>
public partial class MarkingRowViewModel : ObservableObject
{
    private readonly PokemonHistoryViewModel _parent;
    private readonly int _index;
    private bool _loading;

    public MarkingRowViewModel(PokemonHistoryViewModel parent, int index, string name, int value)
    {
        _parent = parent;
        _index = index;
        Name = name;
        _loading = true;
        Value = value;
        _loading = false;
    }

    public string Name { get; }
    public IReadOnlyList<string> Choices { get; } = ["None", "Blue", "Pink"];

    [ObservableProperty] private int _value;

    partial void OnValueChanged(int value)
    {
        if (_loading)
            return;
        _parent.SetMarking(_index, value);
    }
}
