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
/// The only way the app talks to itch.io: one request at a time, a polite interval with jitter,
/// retries for transient errors (resilience handler) and an escalating pause on Cloudflare challenges.
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
    private const string BlockedMessage =
        "Cloudflare продолжает показывать проверку. Откройте itch.io в браузере, пройдите проверку и импортируйте cookie cf_clearance на странице «Сбор данных».";

    /// <summary>For background collection: sits out Cloudflare pauses and retries after each one.</summary>
    public async Task<FetchResult> GetAsync(string url, CancellationToken ct, string accept = DefaultAccept)
    {
        while (true)
        {
            await status.WaitWhilePausedAsync(ct);
            var (result, challenge) = await SendThrottledAsync(url, accept, ct);
            if (!challenge) return result;
            if (OnChallenge(url) is null) throw new CloudflareBlockedException(BlockedMessage);
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

    private async Task<(FetchResult Result, bool Challenge)> SendThrottledAsync(string url, string accept, CancellationToken ct)
    {
        (FetchResult Result, bool Challenge) r;
        using (await throttle.AcquireAsync(ct))
        {
            r = await SendAsync(url, accept, ct);
        }
        if (!r.Challenge)
        {
            status.RecordSuccess();
            status.SetNotice(null);
            cookies.Save();
        }
        return r;
    }

    /// <summary>Escalates the shared pause; returns null once the configured pauses are exhausted.</summary>
    private TimeSpan? OnChallenge(string url)
    {
        var n = status.RecordChallenge();
        var pauses = settings.Current.CloudflarePauseMinutes is { Length: > 0 } p ? p : [10, 20, 40];
        if (n > pauses.Length)
        {
            status.MarkBlocked();
            return null;
        }
        var pause = TimeSpan.FromMinutes(pauses[n - 1]);
        log.LogWarning("Cloudflare challenge on {Url}; pausing for {Minutes} min (attempt {N})", url, pause.TotalMinutes, n);
        status.BeginPause(pause);
        return pause;
    }

    private async Task<(FetchResult Result, bool Challenge)> SendAsync(string url, string accept, CancellationToken ct)
    {
        if (browser is not null)
        {
            var page = await browser.TryGetAsync(url, ct);
            if (page is not null)
            {
                var challenge = IsChallengeBody(page.Body);
                status.RecordRequest(url, page.Status, challenge || page.Status >= 400 && page.Status != 404);
                return (new FetchResult(page.Status, challenge ? "" : page.Body, page.FinalUrl), challenge);
            }
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
            var challenge = IsChallenge(response, body);
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

    public static bool IsChallengeBody(string body) =>
        body.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Performing security verification", StringComparison.OrdinalIgnoreCase)
        || body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
        || body.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase);
}
