using System.Net;
using System.Text;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Infrastructure.Crawling;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace ItchDB.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddItchInfrastructure(this IServiceCollection services, string dataRoot)
    {
        var paths = new DataPaths(dataRoot);
        services.AddSingleton(paths);

        services.AddDbContextFactory<ItchDbContext>(o => o
            .UseSqlite(paths.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor()));

        var dp = services.AddDataProtection()
            .SetApplicationName("ItchDB")
            .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<NetworkStatus>();
        services.AddSingleton<RequestThrottle>();
        services.AddSingleton<CookieStore>();
        services.AddSingleton<BrowserFetcher>();

        services.AddSingleton<CatalogIngestor>();
        services.AddSingleton<CursorStore>();
        services.AddSingleton<CrawlCoordinator>();
        services.AddSingleton<CoverageReport>();
        services.AddSingleton<LiveTotalService>();
        services.AddHostedService<CrawlScheduler>();

        services.AddSingleton<DatasetLoader>();
        services.AddSingleton<AnalyticsService>();
        services.AddSingleton<DataQueries>();

        services.AddHttpClient<ItchApiClient>(c => c.Timeout = TimeSpan.FromSeconds(30));

        services.AddHttpClient<ItchHttpClient>(c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                CookieContainer = sp.GetRequiredService<CookieStore>().Container,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ConnectCallback = HostConnector.ConnectAsync,
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("itch", b =>
            {
                b.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(4),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome)),
                });
                b.AddTimeout(TimeSpan.FromSeconds(60));
            });

        return services;
    }

    /// <summary>Reads the body for classification, leaving a fresh copy for the caller that consumes the response.</summary>
    private static string ReadBody(HttpResponseMessage response)
    {
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var type = response.Content.Headers.ContentType;
        response.Content = new StringContent(body, Encoding.UTF8);
        if (type is not null) response.Content.Headers.ContentType = type;
        return body;
    }

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        if (HostConnector.IsRefused(outcome.Exception)) return false;
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException) return true;
        if (outcome.Result is not { } r) return false;
        // Cloudflare challenges are handled by ItchHttpClient with long pauses, never by quick retries.
        if (r.Headers.Contains("cf-mitigated")) return false;
        if (r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
            && ItchHttpClient.IsChallengeBody(ReadBody(r)))
            return false;
        return (int)r.StatusCode is 429 or 500 or 502 or 503 or 504;
    }
}
