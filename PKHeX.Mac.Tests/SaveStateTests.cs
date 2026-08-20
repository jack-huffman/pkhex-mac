using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The unsaved-changes model decides whether closing the window warns the user, so its
/// state machine is worth pinning down.
/// </summary>
public class SaveStateTests
{
    [Fact]
    public void StartsClean()
    {
        var state = new SaveStateViewModel();
        Assert.False(state.HasUnsavedChanges);
        Assert.Equal(0, state.PendingChanges);
        Assert.Equal("No unsaved changes", state.CountText);
        Assert.Empty(state.Log);
    }

    [Fact]
    public void CountsAndDescribesChanges()
    {
        var state = new SaveStateViewModel();
        state.NoteChange("Pokédex updated");
        Assert.True(state.HasUnsavedChanges);
        Assert.Equal("1 unsaved change", state.CountText);

        state.NoteChange("Mail updated");
        Assert.Equal("2 unsaved changes", state.CountText);
        Assert.Equal("Mail updated", state.LastChange);
    }

    [Fact]
    public void LogIsNewestFirst()
    {
        var state = new SaveStateViewModel();
        state.NoteChange("first");
        state.NoteChange("second");
        state.NoteChange("third");
        Assert.Equal("third", state.Log[0].Description);
        Assert.Equal("first", state.Log[^1].Description);
    }

    [Fact]
    public void ExportClearsTheCountButKeepsHistory()
    {
        var state = new SaveStateViewModel();
        state.NoteChange("edited a Pokémon");
        state.MarkSaved("main");

        Assert.False(state.HasUnsavedChanges);
        Assert.Equal(0, state.PendingChanges);
        Assert.Equal("Exported to main", state.SavedText);
        // The export itself is logged, and the earlier edit is still visible.
        Assert.True(state.Log[0].IsMilestone);
        Assert.Contains(state.Log, e => e.Description == "edited a Pokémon");
    }

    [Fact]
    public void EditingAfterExportGoesDirtyAgain()
    {
        var state = new SaveStateViewModel();
        state.NoteChange("one");
        state.MarkSaved("main");
        state.NoteChange("two");

        Assert.True(state.HasUnsavedChanges);
        Assert.Equal(1, state.PendingChanges);
    }

    [Fact]
    public void OpeningAnotherSaveResetsEverything()
    {
        var state = new SaveStateViewModel();
        state.NoteChange("one");
        state.MarkSaved("main");
        state.Reset();

        Assert.False(state.HasUnsavedChanges);
        Assert.Empty(state.Log);
        Assert.Equal("Not exported this session", state.SavedText);
    }

    [Fact]
    public void LogIsCappedButTheCountIsNot()
    {
        var state = new SaveStateViewModel();
        for (int i = 0; i < 200; i++)
            state.NoteChange($"change {i}");

        Assert.Equal(200, state.PendingChanges);   // the count must stay truthful
        Assert.Equal(60, state.Log.Count);          // the log is bounded
        Assert.Equal("change 199", state.Log[0].Description);
    }

    [Fact]
    public void HistoryTogglesOpenAndShut()
    {
        var state = new SaveStateViewModel();
        Assert.False(state.IsLogOpen);
        state.ToggleLogCommand.Execute(null);
        Assert.True(state.IsLogOpen);
        state.ToggleLogCommand.Execute(null);
        Assert.False(state.IsLogOpen);
    }
}
