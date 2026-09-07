using PKHeX.Core;
using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Gen 7+ shows a six-digit TID and a four-digit SID carved from one 32-bit value. The
/// engine wraps silently when the pair does not fit, so the editor has to refuse it.
/// Gen 7 saves are used because a blank Gen 9 save has no readable trainer blocks.
/// </summary>
public class TrainerEditorTests
{
    [Fact]
    public void SevenDigitSidCeilingIsWhatTheEngineCanStore()
    {
        var sav = new SAV7SM();
        var vm = new TrainerEditorViewModel(sav, () => { });
        Assert.Equal(999_999u, vm.MaxTid);
        Assert.Equal(4294u, vm.MaxSid);
        Assert.True(sav.IsValidTrainerID7(vm.MaxSid, 0));
        Assert.False(sav.IsValidTrainerID7(vm.MaxSid + 1, 0));
    }

    [Fact]
    public void OlderGamesUseSixteenBitIds()
    {
        var vm = new TrainerEditorViewModel(new SAV5B2W2(), () => { });
        Assert.Equal(65_535u, vm.MaxTid);
        Assert.Equal(65_535u, vm.MaxSid);
    }

    [Fact]
    public void ApplyRefusesAPairTheGameCannotStore()
    {
        var sav = new SAV7SM();
        var vm = new TrainerEditorViewModel(sav, () => { }) { Tid = 999_999, Sid = 4294 };
        Assert.False(vm.Apply(out var problem));
        Assert.NotEmpty(problem);
        Assert.NotEqual(4294u, sav.DisplaySID);
    }

    [Fact]
    public void ApplyWritesAValidPair()
    {
        var sav = new SAV7SM();
        var vm = new TrainerEditorViewModel(sav, () => { }) { Tid = 123_456, Sid = 4000, OtName = "Selene" };
        Assert.True(vm.Apply(out _));
        Assert.Equal(123_456u, sav.DisplayTID);
        Assert.Equal(4000u, sav.DisplaySID);
        Assert.Equal("Selene", sav.OT);
    }

    [Fact]
    public void LoadingTheCurrentValuesIsNotAnEdit()
    {
        var changes = 0;
        _ = new TrainerEditorViewModel(new SAV7SM(), () => changes++);
        Assert.Equal(0, changes);
    }
}
