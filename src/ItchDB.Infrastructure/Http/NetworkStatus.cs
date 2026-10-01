namespace ItchDB.Infrastructure.Http;

/// <summary>Process-wide network state shown in the app bar: request counters and the Cloudflare back-off.</summary>
public sealed class NetworkStatus
{
    private readonly object _gate = new();
    private CancellationTokenSource _skipPause = new();

    public event Action? Changed;

    public long TotalRequests { get; private set; }
    public long FailedRequests { get; private set; }
    public DateTime? LastRequestAt { get; private set; }
    public string? LastUrl { get; private set; }
    public int LastStatusCode { get; private set; }

    public DateTime? PausedUntil { get; private set; }
    public int ConsecutiveChallenges { get; private set; }
    public DateTime? LastChallengeAt { get; private set; }
    public bool IsBlocked { get; private set; }

    /// <summary>What the user should do right now, for example solve the check in the Chrome window.</summary>
    public string? Notice { get; private set; }

    public bool IsPaused => PausedUntil is { } until && until > DateTime.UtcNow;

    public void SetNotice(string? value)
    {
        if (Notice == value) return;
        Notice = value;
        Changed?.Invoke();
    }

    internal void RecordRequest(string url, int statusCode, bool failed)
    {
        lock (_gate)
        {
            TotalRequests++;
            if (failed) FailedRequests++;
            LastRequestAt = DateTime.UtcNow;
            LastUrl = url;
            LastStatusCode = statusCode;
        }
        Changed?.Invoke();
    }

    internal void RecordSuccess()
    {
        if (ConsecutiveChallenges == 0 && !IsBlocked) return;
        lock (_gate)
        {
            ConsecutiveChallenges = 0;
            IsBlocked = false;
        }
        Changed?.Invoke();
    }

    internal int RecordChallenge()
    {
        lock (_gate)
        {
            ConsecutiveChallenges++;
            LastChallengeAt = DateTime.UtcNow;
            return ConsecutiveChallenges;
        }
    }

    internal void BeginPause(TimeSpan duration)
    {
        lock (_gate)
        {
            PausedUntil = DateTime.UtcNow + duration;
            _skipPause.Dispose();
            _skipPause = new CancellationTokenSource();
        }
        Changed?.Invoke();
    }

    internal void MarkBlocked()
    {
        lock (_gate) IsBlocked = true;
        Changed?.Invoke();
    }

    /// <summary>Ends the Cloudflare pause early, e.g. after the user imported a fresh cf_clearance cookie.</summary>
    public void SkipPause()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            PausedUntil = null;
            IsBlocked = false;
            ConsecutiveChallenges = 0;
            cts = _skipPause;
        }
        cts.Cancel();
        Changed?.Invoke();
    }

    internal async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (true)
        {
            DateTime? until;
            CancellationToken skip;
            lock (_gate)
            {
                until = PausedUntil;
                skip = _skipPause.Token;
            }
            if (until is null) return;
            var wait = until.Value - DateTime.UtcNow;
            if (wait <= TimeSpan.Zero)
            {
                lock (_gate) PausedUntil = null;
                Changed?.Invoke();
                return;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, skip);
            try
            {
                await Task.Delay(wait, linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
