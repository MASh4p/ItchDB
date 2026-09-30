using System.Diagnostics;
using ItchDB.Infrastructure.Settings;

namespace ItchDB.Infrastructure.Http;

/// <summary>Serializes requests to itch.io and keeps a minimum interval with random jitter between them.</summary>
public sealed class RequestThrottle(SettingsService settings)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _nextAllowed = TimeSpan.Zero;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var wait = _nextAllowed - _clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }
        catch
        {
            _lock.Release();
            throw;
        }
        return new Releaser(this);
    }

    private void Release()
    {
        var s = settings.Current;
        var interval = Math.Max(250, s.RequestIntervalMs);
        var jitter = s.RequestJitterMs > 0 ? Random.Shared.Next(0, s.RequestJitterMs + 1) : 0;
        _nextAllowed = _clock.Elapsed + TimeSpan.FromMilliseconds(interval + jitter);
        _lock.Release();
    }

    private sealed class Releaser(RequestThrottle owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release();
        }
    }
}
