using System.Diagnostics;
using ItchDB.Infrastructure.Settings;

namespace ItchDB.Infrastructure.Http;

/// <summary>
/// Caps how many itch.io requests are in flight. A live browser session runs up to the parallel-request
/// setting at once; each of those waits only for its own tab. The plain HTTP client stays one-at-a-time
/// and keeps the configured gap.
/// </summary>
public sealed class RequestThrottle(SettingsService settings)
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly SlotGate _lists = new();
    private readonly object _pace = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _nextList = TimeSpan.Zero;
    private TimeSpan _nextGame = TimeSpan.Zero;
    private bool _fast;

    /// <summary>A live browser session can run several list requests at once. The plain HTTP client cannot.</summary>
    public void PreferFast(bool fast) => _fast = fast;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct, bool gamePage = false)
    {
        if (!_fast)
        {
            await _serial.WaitAsync(ct);
            if (!_fast)
            {
                try
                {
                    await WaitTurnAsync(gamePage, ct);
                }
                catch
                {
                    _serial.Release();
                    throw;
                }
                return new SerialReleaser(_serial);
            }
            _serial.Release();
        }

        var limit = Math.Clamp(settings.Current.ParallelRequests, 1, 8);
        return await _lists.EnterAsync(limit, ct);
    }

    private async Task WaitTurnAsync(bool gamePage, CancellationToken ct)
    {
        TimeSpan wait;
        lock (_pace)
        {
            var s = settings.Current;
            var next = gamePage ? _nextGame : _nextList;
            wait = next - _clock.Elapsed;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            var interval = Math.Max(0, gamePage ? s.GamePageIntervalMs : s.RequestIntervalMs);
            var jitterCap = s.RequestJitterMs;
            var jitter = jitterCap > 0 ? Random.Shared.Next(0, jitterCap + 1) : 0;
            var updated = _clock.Elapsed + wait + TimeSpan.FromMilliseconds(interval + jitter);
            if (gamePage) _nextGame = updated;
            else _nextList = updated;
        }
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }

    private sealed class SerialReleaser(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }

    /// <summary>A concurrency limit that follows the current setting, including a change while a crawl is running.</summary>
    private sealed class SlotGate
    {
        private readonly object _gate = new();
        private readonly Queue<TaskCompletionSource<bool>> _waiting = new();
        private int _used;

        public async Task<IDisposable> EnterAsync(int limit, CancellationToken ct)
        {
            limit = Math.Max(1, limit);
            TaskCompletionSource<bool>? pending = null;
            lock (_gate)
            {
                if (_used < limit)
                {
                    _used++;
                    return new Release(this);
                }
                pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Enqueue(pending);
            }

            try
            {
                await pending.Task.WaitAsync(ct);
            }
            catch
            {
                if (pending.Task.IsCompletedSuccessfully) Exit();
                else pending.TrySetCanceled(ct);
                throw;
            }
            return new Release(this);
        }

        private void Exit()
        {
            lock (_gate)
            {
                while (_waiting.Count > 0)
                {
                    var next = _waiting.Dequeue();
                    if (next.Task.IsCompleted) continue;
                    if (next.TrySetResult(true)) return;
                }
                _used--;
            }
        }

        private sealed class Release(SlotGate gate) : IDisposable
        {
            private int _once;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _once, 1) == 0) gate.Exit();
            }
        }
    }
}
