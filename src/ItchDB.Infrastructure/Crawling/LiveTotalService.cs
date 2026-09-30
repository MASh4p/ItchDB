using System.Collections.Concurrent;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

public sealed record LiveTotal(int? Total, string Url, bool Unsupported, string? Error, DateTime At);

/// <summary>
/// Reads "(N results)" of an arbitrary filter combination straight from itch.io (one request through the shared throttle)
/// and stores it with the other listing totals, so the dataset uses it too.
/// </summary>
public sealed class LiveTotalService(IServiceProvider services, CatalogIngestor ingestor, ILogger<LiveTotalService> log)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan CacheFailuresFor = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, LiveTotal> _cache = new(StringComparer.Ordinal);

    /// <summary>Key under which the total of these filters is stored in ListingTotals.</summary>
    public static string FilterKey(IReadOnlyList<string> filters) => Spec(filters).FilterKey;

    public LiveTotal? Cached(IReadOnlyList<string> filters) =>
        _cache.TryGetValue(FilterKey(filters), out var v) && DateTime.UtcNow - v.At < (v.Error is null ? CacheFor : CacheFailuresFor) ? v : null;

    public async Task<LiveTotal> GetAsync(IReadOnlyList<string> filters, CancellationToken ct)
    {
        var spec = Spec(filters);
        if (Cached(filters) is { } cached) return cached;

        var url = spec.Url(1, json: false);
        LiveTotal result;
        try
        {
            var http = services.GetRequiredService<ItchHttpClient>();
            var response = await http.GetNowAsync(url, ct);
            if (response.IsNotFound || response.IsSuccess && ListCrawler.IsRedirectedAway(response, spec))
            {
                result = new LiveTotal(null, url, true, null, DateTime.UtcNow);
            }
            else if (!response.IsSuccess)
            {
                result = new LiveTotal(null, url, false, response.StatusCode == 0 ? "нет соединения" : $"HTTP {response.StatusCode}", DateTime.UtcNow);
            }
            else
            {
                var total = ListingParser.ParseHtml(response.Body).TotalResults;
                if (total is { } t) await ingestor.SaveListingTotalAsync(DateOnly.FromDateTime(DateTime.UtcNow), spec.FilterKey, t, ct);
                result = new LiveTotal(total, url, false, total is null ? "счётчик не найден на странице" : null, DateTime.UtcNow);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Live total for {Url} failed: {Error}", url, ex.Message);
            result = new LiveTotal(null, url, false, ex.Message, DateTime.UtcNow);
        }

        _cache[spec.FilterKey] = result;
        return result;
    }

    private static ListSpec Spec(IReadOnlyList<string> filters) =>
        new(ListSpec.TopRated, filters.Where(f => f.Length > 0).Distinct(StringComparer.Ordinal).ToList());
}
