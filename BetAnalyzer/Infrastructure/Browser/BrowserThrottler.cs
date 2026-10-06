namespace BetAnalyzer.Infrastructure.Browser;

public sealed class BrowserThrottler
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _delay;
    public BrowserThrottler(TimeSpan delay) => _delay = delay;
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try { var result = await action(); await Task.Delay(_delay, cancellationToken); return result; }
        finally { _semaphore.Release(); }
    }
    public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try { await action(); await Task.Delay(_delay, cancellationToken); }
        finally { _semaphore.Release(); }
    }
}
