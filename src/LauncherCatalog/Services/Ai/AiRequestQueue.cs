using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LauncherCatalog.Services.Ai;

/// <summary>
/// Serializes AI work strictly first-in-first-out: exactly one item runs at a time and later
/// arrivals wait for earlier ones to finish. Each item carries its own wall-clock timeout (measured
/// from the moment it is enqueued, so time spent waiting in the queue counts toward it).
/// </summary>
public sealed class AiRequestQueue : IDisposable
{
    private readonly Channel<Func<Task>> _channel = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;

    public AiRequestQueue()
    {
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// Enqueues <paramref name="work"/> and waits for its result. Throws <see cref="TimeoutException"/>
    /// if the work does not complete within <paramref name="timeout"/> (including queue wait time).
    /// </summary>
    public async Task<T> EnqueueAsync<T>(
        Func<CancellationToken, Task<T>> work,
        TimeSpan timeout,
        CancellationToken callerToken)
    {
        var timeoutCts = new CancellationTokenSource(timeout);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, callerToken, _shutdown.Token);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Run()
        {
            if (linked.IsCancellationRequested)
            {
                tcs.TrySetCanceled();
                return;
            }

            try
            {
                tcs.TrySetResult(await work(linked.Token));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        await _channel.Writer.WriteAsync(Run, callerToken).ConfigureAwait(false);

        using var registration = linked.Token.Register(() => tcs.TrySetCanceled());
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (timeoutCts.IsCancellationRequested && !callerToken.IsCancellationRequested)
            {
                throw new TimeoutException("The AI request exceeded its time limit.");
            }

            throw;
        }
        finally
        {
            linked.Dispose();
            timeoutCts.Dispose();
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var run in _channel.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await run().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _channel.Writer.TryComplete();
        _shutdown.Dispose();
    }
}
