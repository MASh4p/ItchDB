using System.Net;
using System.Net.Http.Headers;
using ItchDB.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace ItchDB.Infrastructure.Http;

public sealed record FetchResult(int StatusCode, string Body, string Url)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
    public bool IsNotFound => StatusCode is 404 or 410;
}

public sealed class CloudflareBlockedException(string message) : Exception(message);

/// <summary>
/// The only way the app talks to itch.io. Browser tabs load in parallel and each request waits
/// only for its own tab; the plain HTTP client stays one at a time. Transient errors are retried
/// and a Cloudflare challenge escalates into a shared pause.
/// </summary>
public sealed class ItchHttpClient(
    HttpClient http,
    RequestThrottle throttle,
    NetworkStatus status,
    CookieStore cookies,
    SettingsService settings,
    ILogger<ItchHttpClient> log,
    BrowserFetcher? browser = null)
{
    private const string DefaultAccept = "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8";
    private static readonly TimeSpan Http400Pause = TimeSpan.FromSeconds(5);
    private const int Http400ResetAfter = 5;
    private static readonly TimeSpan ConnectionPause = TimeSpan.FromSeconds(5);
    private const int ConnectionSlowAfter = 5;
    private const string BlockedMessage =
        "Cloudflare продолжает показывать проверку. Откройте itch.io в браузере, пройдите проверку и импортируйте cookie cf_clearance на странице «Сбор данных».";

    /// <summary>For background collection: sits out Cloudflare pauses and retries after each one.</summary>
    public async Task<FetchResult> GetAsync(string url, CancellationToken ct, string accept = DefaultAccept, bool gamePage = false)
    {
        while (true)
        {
            await status.WaitWhilePausedAsync(ct);
            var (result, challenge) = await SendThrottledAsync(url, accept, ct, gamePage);
            if (!challenge) return result;
            // One protected game page must not freeze the card queue. A streak means the session itself is blocked.
            if (gamePage && status.NoteGamePageChallenge() < 4)
            {
                status.SetNotice(null);
                log.LogWarning("Cloudflare on game page {Url}; skipping it and continuing", url);
                return new FetchResult(429, "", result.Url);
            }
            if (OnChallenge(url) is null) throw new CloudflareBlockedException(BlockedMessage);
            await status.WaitWhilePausedAsync(ct);
        }
    }

    /// <summary>
    /// For requests a user is waiting on: never waits out a pause and fails at once on a challenge.
    /// Such a challenge does not pause collection: a single page can be protected while ordinary lists still load,
    /// and if everything is challenged the collector finds out on its own next request.
    /// </summary>
    public async Task<FetchResult> GetNowAsync(string url, CancellationToken ct, string accept = DefaultAccept)
    {
        if (status.IsPaused)
            throw new CloudflareBlockedException(
                $"itch.io недавно показал проверку Cloudflare, запросы к нему приостановлены до {status.PausedUntil!.Value.ToLocalTime():HH:mm}.");
        var (result, challenge) = await SendThrottledAsync(url, accept, ct);
        if (!challenge) return result;
        log.LogWarning("Cloudflare challenge on interactive request {Url}", url);
        throw new CloudflareBlockedException(
            "itch.io показал проверку Cloudflare на этой странице. Если сбор тоже встанет, импортируйте cookie cf_clearance на странице «Сбор данных».");
    }

    private async Task<(FetchResult Result, bool Challenge)> SendThrottledAsync(string url, string accept, CancellationToken ct, bool gamePage = false)
    {
        (FetchResult Result, bool Challenge) r;
        using (await throttle.AcquireAsync(ct, gamePage))
        {
            r = await SendAsync(url, accept, ct, gamePage);
        }
        if (r.Result.StatusCode == 0)
        {
            HandleConnectionFailure(url);
            return r;
        }
        if (r.Result.StatusCode == 400 && !IsChallengeBody(r.Result.Body))
        {
            await HandleHttp400Async(url, ct);
            return r;
        }
        if (!r.Challenge)
        {
            status.RecordSuccess();
            status.SetNotice(null);
            cookies.Save();
        }
        return r;
    }

    /// <summary>A bare 400 is a burned browser session, not a captcha. Wait briefly; after five in a row, start a clean window.</summary>
    private async Task HandleHttp400Async(string url, CancellationToken ct)
    {
        var (count, reset) = status.NoteHttp400(Http400ResetAfter);
        if (reset)
        {
            log.LogWarning("HTTP 400 for {Url} happened {Count} times in a row; resetting the browser session", url, count);
            status.SetNotice("Код 400 повторился больше 5 раз подряд. Сеанс браузера сброшен, следующий запрос откроет чистое окно.");
            if (browser is not null)
            {
                try
                {
                    await browser.ResetSessionAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Browser session reset after HTTP 400 failed");
                }
            }
        }
        else
        {
            log.LogWarning("HTTP 400 for {Url}; pausing {Seconds:0}s ({Count} in a row)", url, Http400Pause.TotalSeconds, count);
            status.SetNotice($"Код 400. Пауза {Http400Pause.TotalSeconds:0} с ({count} подряд). После пяти подряд сеанс браузера сбросится сам.");
        }
        status.TryBeginPause(Http400Pause);
    }

    /// <summary>
    /// A dead game host is not a Cloudflare check. Skip it. Only a long streak earns a few seconds of pause,
    /// so one unreachable page cannot stop the rest of the crawl.
    /// </summary>
    private void HandleConnectionFailure(string url)
    {
        var (count, slow) = status.NoteConnectionFailure(ConnectionSlowAfter);
        if (!slow)
        {
            log.LogWarning("No response from {Url} ({Count} in a row); continuing", url, count);
            return;
        }
        log.LogWarning("No response from {Url} ({Count} in a row); pausing {Seconds:0}s", url, count, ConnectionPause.TotalSeconds);
        status.SetNotice($"Нет ответа уже {count} раз подряд. Пауза {ConnectionPause.TotalSeconds:0} с, затем сбор продолжится. Это не проверка Cloudflare.");
        status.TryBeginPause(ConnectionPause);
    }

    /// <summary>
    /// Escalates the shared pause. Null means the pauses are exhausted.
    /// Zero means another request already started the current pause.
    /// </summary>
    private TimeSpan? OnChallenge(string url)
    {
        var pauses = settings.Current.CloudflarePauseMinutes is { Length: > 0 } p ? p : [10, 20, 40];
        var n = status.StartPause(pauses);
        if (n is null or 0) return n is null ? null : TimeSpan.Zero;
        var pause = TimeSpan.FromMinutes(pauses[n.Value - 1]);
        log.LogWarning("itch.io blocked {Url}; pausing for {Minutes} min (attempt {N})", url, pause.TotalMinutes, n);
        return pause;
    }

    private async Task<(FetchResult Result, bool Challenge)> SendAsync(string url, string accept, CancellationToken ct, bool gamePage = false)
    {
        if (browser is not null)
        {
            var page = await browser.TryGetAsync(url, ct, gamePage);
            if (page is not null)
            {
                var challenge = IsCaptcha(page.Status, page.Body);
                throttle.PreferFast(!challenge);
                if (challenge) status.SetNotice(BrowserFetcher.BlockNotice);
                status.RecordRequest(url, page.Status, challenge || page.Status == 0 || page.Status >= 400 && page.Status != 404);
                return (new FetchResult(page.Status, challenge ? "" : page.Body, page.FinalUrl), challenge);
            }
            throttle.PreferFast(false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var ua = settings.Current.UserAgent;
        if (!string.IsNullOrWhiteSpace(ua)) request.Headers.TryAddWithoutValidation("User-Agent", ua);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));
        BrowserHeaders.Apply(request, ua, accept);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var code = (int)response.StatusCode;
            var challenge = IsChallenge(response, body) || code == 429;
            if (challenge) status.SetNotice(BrowserFetcher.BlockNotice);
            status.RecordRequest(url, code, challenge || code >= 400 && code != 404);
            return (new FetchResult(code, challenge ? "" : body, response.RequestMessage?.RequestUri?.ToString() ?? url), challenge);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or TimeoutRejectedException)
        {
            status.RecordRequest(url, 0, true);
            log.LogWarning("Request failed for {Url}: {Message}", url, ex.Message);
            return (new FetchResult(0, "", url), false);
        }
    }

    /// <summary>One request to see whether the imported cookie is accepted. Does not start a Cloudflare pause.</summary>
    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        var (result, challenge) = await SendThrottledAsync("https://itch.io/games", DefaultAccept, ct);
        return !challenge && result.IsSuccess;
    }

    public static bool IsChallenge(HttpResponseMessage response, string body)
    {
        if (response.Headers.TryGetValues("cf-mitigated", out var values) &&
            values.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests))
            return false;
        return IsChallengeBody(body);
    }

    /// <summary>A captcha page, or the 400/429 streak itch.io returns once it stops accepting the client.</summary>
    public static bool IsBlocked(int status, string body) => IsChallengeBody(body) || status is 400 or 429;

    /// <summary>A check the user can solve. A bare HTTP 400 is handled separately: a short pause, then a session reset.</summary>
    private static bool IsCaptcha(int status, string body) => IsChallengeBody(body) || status == 429;

    public static bool IsChallengeBody(string body) =>
        body.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Performing security verification", StringComparison.OrdinalIgnoreCase)
        || body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
        || body.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase);
}
