using PKHeX.Core;
using PKHeX.Mac.ViewModels;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The workspace decides which saves are open and whether the window may close. These
/// tests exercise it with real saves written to disk, the way the window does.
/// </summary>
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkhex-workspace-" + Guid.NewGuid().ToString("N"));

    public WorkspaceTests() => Directory.CreateDirectory(_dir);

    /// <summary>
    /// A blank Gen 5 save on disk. Blank saves keep no trainer name through a write and
    /// re-parse, so a tab shows the file name; the tests lean on that.
    /// </summary>
    private string WriteSave(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new SAV5B2W2().Write().ToArray());
        return path;
    }

    [Fact]
    public void StartsWithOneEmptyTabAndNoStrip()
    {
        using var workspace = new WorkspaceViewModel();
        var tab = workspace.AddEmptyTab();
        Assert.Single(workspace.Tabs);
        Assert.Same(tab, workspace.Active);
        Assert.True(tab.IsActive);
        Assert.False(workspace.ShowTabStrip);
        Assert.Null(tab.Session.Save);
    }

    [Fact]
    public void OpeningIntoTheEmptyPlaceholderReusesIt()
    {
        using var workspace = new WorkspaceViewModel();
        var placeholder = workspace.AddEmptyTab();

        Assert.True(workspace.Open(WriteSave("ash"), out var error), error);
        Assert.Single(workspace.Tabs);
        Assert.Same(placeholder, workspace.Active);
        Assert.Equal("ash", placeholder.Title);
        Assert.False(workspace.ShowTabStrip);
    }

    [Fact]
    public void ASecondSaveOpensInANewActiveTab()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        Assert.True(workspace.Open(WriteSave("ash"), out _));
        Assert.True(workspace.Open(WriteSave("gary"), out _));

        Assert.Equal(2, workspace.Tabs.Count);
        Assert.True(workspace.ShowTabStrip);
        Assert.Equal("gary", workspace.Active!.Title);
        Assert.False(workspace.Tabs[0].IsActive);
        Assert.True(workspace.Tabs[1].IsActive);
    }

    [Fact]
    public void AFileThatIsNotASaveDoesNotLeaveATabBehind()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        Assert.True(workspace.Open(WriteSave("ash"), out _));
        var junk = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(junk, "not a save");

        Assert.False(workspace.Open(junk, out var error));
        Assert.NotEmpty(error);
        Assert.Single(workspace.Tabs);
    }

    [Fact]
    public void ClosingTheActiveTabActivatesItsNeighbour()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        workspace.Open(WriteSave("ash"), out _);
        workspace.Open(WriteSave("gary"), out _);
        workspace.Open(WriteSave("misty"), out _);
        workspace.Active = workspace.Tabs[1];

        workspace.Close(workspace.Tabs[1]);

        Assert.Equal(2, workspace.Tabs.Count);
        Assert.Equal("misty", workspace.Active!.Title);
    }

    [Fact]
    public void ClosingTheLastTabLeavesAnEmptyOne()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        workspace.Open(WriteSave("ash"), out _);

        workspace.Close(workspace.Tabs[0]);

        var remaining = Assert.Single(workspace.Tabs);
        Assert.Same(remaining, workspace.Active);
        Assert.Null(remaining.Session.Save);
    }

    [Fact]
    public void PendingWorkInABackgroundTabStillCounts()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        workspace.Open(WriteSave("ash"), out _);
        workspace.Open(WriteSave("gary"), out _);
        var background = workspace.Tabs[0];

        Assert.False(workspace.HasPendingWork);
        background.Session.NoteExternalChange("edited elsewhere");

        Assert.True(workspace.HasPendingWork);
        Assert.Same(background, workspace.FirstPendingTab());
        // The dirty dot belongs to the tab whose save changed, not the active one.
        Assert.True(background.IsDirty);
        Assert.False(workspace.Active!.IsDirty);
        Assert.Equal(string.Empty, workspace.DescribePendingTabs()); // one dirty tab needs no roll call
    }

    [Fact]
    public void SeveralDirtyTabsAreNamed()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        workspace.Open(WriteSave("ash"), out _);
        workspace.Open(WriteSave("gary"), out _);
        foreach (var tab in workspace.Tabs)
            tab.Session.NoteExternalChange("edit");

        var summary = workspace.DescribePendingTabs();
        Assert.Contains("2 open saves", summary, StringComparison.Ordinal);
        Assert.Contains("ash", summary, StringComparison.Ordinal);
        Assert.Contains("gary", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherSavesAreOfferedAsTransferTargets()
    {
        using var workspace = new WorkspaceViewModel();
        workspace.AddEmptyTab();
        workspace.Open(WriteSave("ash"), out _);
        workspace.Open(WriteSave("gary"), out _);

        var first = workspace.Tabs[0].Session;
        var targets = first.OtherSaves!();
        var target = Assert.Single(targets);
        Assert.StartsWith("gary", target.Name, StringComparison.Ordinal);
        Assert.True(first.CanTransfer);
    }

    [Fact]
    public void EverySessionIsAnnouncedOnceForWiring()
    {
        var announced = new List<MainWindowViewModel>();
        using var workspace = new WorkspaceViewModel();
        workspace.SessionCreated += announced.Add;
        workspace.AddEmptyTab();
        workspace.Open(WriteSave("ash"), out _);   // reuses the placeholder
        workspace.Open(WriteSave("gary"), out _);  // a new session
        workspace.Active = workspace.Tabs[0];
        workspace.Active = workspace.Tabs[1];           // switching never re-wires

        Assert.Equal(2, announced.Count);
        Assert.Equal(announced.Distinct().Count(), announced.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
