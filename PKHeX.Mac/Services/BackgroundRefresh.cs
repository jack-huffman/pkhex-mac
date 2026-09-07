using System;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.Mac.Services;

/// <summary>
/// Runs one background computation at a time for a view model, where a newer request
/// supersedes any older one still in flight.
/// </summary>
/// <remarks>
/// Three views used to hand-roll this with a <see cref="CancellationTokenSource"/> field,
/// and each made the same mistake a different way: a superseded pass would finish later
/// and overwrite the state of the pass that replaced it. Here the caller gets a single
/// outcome and is told plainly when its result is stale, so the rule "a superseded pass
/// touches nothing" lives in one place.
/// </remarks>
public sealed class BackgroundRefresh : IDisposable
{
    private CancellationTokenSource? _current;

    /// <summary>
    /// Starts <paramref name="work"/> on the thread pool, cancelling whatever was running.
    /// Resumes on the calling context, so the outcome can be applied to bound properties.
    /// </summary>
    public async Task<RefreshOutcome<T>> RunAsync<T>(Func<CancellationToken, T> work)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _current = cts;
        try
        {
            var result = await Task.Run(() => work(cts.Token), cts.Token);
            return cts.IsCancellationRequested ? RefreshOutcome.Superseded<T>() : RefreshOutcome.Completed(result);
        }
        catch (OperationCanceledException)
        {
            return RefreshOutcome.Superseded<T>();
        }
        catch (Exception ex)
        {
            return cts.IsCancellationRequested ? RefreshOutcome.Superseded<T>() : RefreshOutcome.Failed<T>(ex);
        }
        finally
        {
            if (ReferenceEquals(_current, cts))
                _current = null;
            cts.Dispose();
        }
    }

    /// <summary>Abandons the pass in flight, if any. Its outcome will read as superseded.</summary>
    public void Cancel() => _current?.Cancel();

    public void Dispose() => Cancel();
}

/// <summary>What became of one background pass.</summary>
public readonly record struct RefreshOutcome<T>(RefreshStatus Status, T? Result, Exception? Error)
{
    /// <summary>A newer pass replaced this one; apply nothing.</summary>
    public bool IsSuperseded => Status == RefreshStatus.Superseded;

    public bool Succeeded => Status == RefreshStatus.Completed;
}

public static class RefreshOutcome
{
    public static RefreshOutcome<T> Superseded<T>() => new(RefreshStatus.Superseded, default, null);
    public static RefreshOutcome<T> Completed<T>(T result) => new(RefreshStatus.Completed, result, null);
    public static RefreshOutcome<T> Failed<T>(Exception error) => new(RefreshStatus.Failed, default, error);
}

public enum RefreshStatus
{
    Completed,
    Superseded,
    Failed,
}
