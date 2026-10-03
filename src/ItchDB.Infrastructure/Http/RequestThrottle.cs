using System.Diagnostics;
using ItchDB.Infrastructure.Settings;

namespace ItchDB.Infrastructure.Http;

/// <summary>Serializes requests to itch.io and keeps a minimum interval with random jitter between them.</summary>
public sealed class RequestThrottle(SettingsService settings)
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly SemaphoreSlim _parallel = new(4, 4);
    private readonly object _pace = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _nextAllowed = TimeSpan.Zero;
    private bool _fast;

    /// <summary>
    /// A live browser session already passed Cloudflare. List and game pages then start about five times a second,
    /// up to four at once. The plain HTTP client stays one-at-a-time at the configured interval.
    /// </summary>
    public void PreferFast(bool fast) => _fast = fast;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        var slots = _fast ? _parallel : _serial;
        await slots.WaitAsync(ct);
        try
        {
            TimeSpan wait;
            lock (_pace)
            {
                wait = _nextAllowed - _clock.Elapsed;
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                var s = settings.Current;
                var interval = _fast ? 200 : Math.Max(250, s.RequestIntervalMs);
                var jitterCap = _fast ? 80 : s.RequestJitterMs;
                var jitter = jitterCap > 0 ? Random.Shared.Next(0, jitterCap + 1) : 0;
                _nextAllowed = _clock.Elapsed + wait + TimeSpan.FromMilliseconds(interval + jitter);
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }
        catch
        {
            slots.Release();
            throw;
        }
        return new Releaser(slots);
    }

    private sealed class Releaser(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}
