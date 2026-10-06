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
    public int ConsecutiveGamePageChallenges { get; private set; }
    public int ConsecutiveHttp400 { get; private set; }
    public int ConsecutiveConnectionFailures { get; private set; }
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
        if (ConsecutiveChallenges == 0 && ConsecutiveGamePageChallenges == 0 && ConsecutiveHttp400 == 0 && ConsecutiveConnectionFailures == 0 && !IsBlocked) return;
        lock (_gate)
        {
            ConsecutiveChallenges = 0;
            ConsecutiveGamePageChallenges = 0;
            ConsecutiveHttp400 = 0;
            ConsecutiveConnectionFailures = 0;
            IsBlocked = false;
        }
        Changed?.Invoke();
    }

    /// <summary>A challenge on one game page. The crawl skips that page until several arrive in a row.</summary>
    internal int NoteGamePageChallenge()
    {
        lock (_gate)
        {
            ConsecutiveGamePageChallenges++;
            LastChallengeAt = DateTime.UtcNow;
            return ConsecutiveGamePageChallenges;
        }
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

    /// <summary>
    /// Starts the next pause. Returns 0 when a pause is already running (parallel requests must not stack it)
    /// and null when the configured pauses are exhausted.
    /// </summary>
    internal int? StartPause(int[] minutes)
    {
        int n;
        var exhausted = false;
        lock (_gate)
        {
            if (PausedUntil is { } until && until > DateTime.UtcNow) return 0;
            ConsecutiveChallenges++;
            LastChallengeAt = DateTime.UtcNow;
            n = ConsecutiveChallenges;
            if (n > minutes.Length)
            {
                IsBlocked = true;
                exhausted = true;
            }
            else
            {
                PausedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(minutes[n - 1]);
                _skipPause.Dispose();
                _skipPause = new CancellationTokenSource();
            }
        }
        Changed?.Invoke();
        return exhausted ? null : n;
    }

    /// <summary>
    /// Counts a bare HTTP 400. The streak resets after a success. When it passes <paramref name="resetAfter"/>,
    /// the count returns to zero and <c>Reset</c> is true so only one caller resets the browser session.
    /// </summary>
    internal (int Count, bool Reset) NoteHttp400(int resetAfter)
    {
        lock (_gate)
        {
            ConsecutiveHttp400++;
            if (ConsecutiveHttp400 <= resetAfter) return (ConsecutiveHttp400, false);
            var seen = ConsecutiveHttp400;
            ConsecutiveHttp400 = 0;
            return (seen, true);
        }
    }

    /// <summary>
    /// Counts a host that did not answer. The streak resets after a success.
    /// <c>SlowDown</c> becomes true once the streak passes <paramref name="slowAfter"/>, and stays true for further failures.
    /// </summary>
    internal (int Count, bool SlowDown) NoteConnectionFailure(int slowAfter)
    {
        lock (_gate)
        {
            ConsecutiveConnectionFailures++;
            return (ConsecutiveConnectionFailures, ConsecutiveConnectionFailures > slowAfter);
        }
    }

    /// <summary>Starts a short pause unless one is already running, so parallel 400s do not stack or shorten a longer pause.</summary>
    internal bool TryBeginPause(TimeSpan duration)
    {
        lock (_gate)
        {
            if (PausedUntil is { } until && until > DateTime.UtcNow) return false;
            PausedUntil = DateTime.UtcNow + duration;
            _skipPause.Dispose();
            _skipPause = new CancellationTokenSource();
        }
        Changed?.Invoke();
        return true;
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
            ConsecutiveGamePageChallenges = 0;
            ConsecutiveHttp400 = 0;
            ConsecutiveConnectionFailures = 0;
            Notice = null;
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
