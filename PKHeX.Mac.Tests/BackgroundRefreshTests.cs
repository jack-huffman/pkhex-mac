using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The rule under test: a pass that has been superseded reports itself as such and never
/// hands back a result, because three view models used to apply stale results.
/// </summary>
public class BackgroundRefreshTests
{
    [Fact]
    public async Task ACompletedPassReturnsItsResult()
    {
        using var refresh = new BackgroundRefresh();
        var outcome = await refresh.RunAsync(_ => 42);
        Assert.True(outcome.Succeeded);
        Assert.Equal(42, outcome.Result);
    }

    [Fact]
    public async Task ANewerPassSupersedesTheOlderOne()
    {
        using var refresh = new BackgroundRefresh();
        var release = new ManualResetEventSlim();
        var first = refresh.RunAsync(token =>
        {
            release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
            token.ThrowIfCancellationRequested();
            return "old";
        });
        var second = refresh.RunAsync(_ => "new");

        var newer = await second;
        release.Set();
        var older = await first;

        Assert.True(newer.Succeeded);
        Assert.Equal("new", newer.Result);
        Assert.True(older.IsSuperseded);
        Assert.Null(older.Result);
    }

    [Fact]
    public async Task AFailureIsReportedNotThrown()
    {
        using var refresh = new BackgroundRefresh();
        var outcome = await refresh.RunAsync<int>(_ => throw new InvalidOperationException("boom"));
        Assert.Equal(RefreshStatus.Failed, outcome.Status);
        Assert.IsType<InvalidOperationException>(outcome.Error);
    }

    [Fact]
    public async Task CancelMakesTheInFlightPassSuperseded()
    {
        using var refresh = new BackgroundRefresh();
        var task = refresh.RunAsync(token =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            token.ThrowIfCancellationRequested();
            return 1;
        });
        refresh.Cancel();
        Assert.True((await task).IsSuperseded);
    }
}
