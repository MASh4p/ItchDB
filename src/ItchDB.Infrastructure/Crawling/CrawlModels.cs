using System.Globalization;
using ItchDB.Core.Catalog;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

public enum CrawlProfile
{
    QuickStart = 0,
    Deep = 1,
    Daily = 2,
    Tracking = 3,
    GamePages = 4,
}

public enum CrawlState
{
    Idle = 0,
    Running = 1,
    Paused = 2,
    Stopping = 3,
}

public sealed record CrawlLogEntry(DateTime At, LogLevel Level, string Message);

public static class CrawlProfiles
{
    public static string Name(CrawlProfile p) => p switch
    {
        CrawlProfile.QuickStart => "Быстрый старт",
        CrawlProfile.Deep => "Глубокий сбор",
        CrawlProfile.Daily => "Ежедневное обновление",
        CrawlProfile.Tracking => "Проверка отслеживаемых игр",
        CrawlProfile.GamePages => "Карточки игр",
        _ => p.ToString(),
    };

    public static string Name(string stored) => Enum.TryParse<CrawlProfile>(stored, out var p) ? Name(p) : stored;
}

/// <summary>A browse list on itch.io: a sort order plus filter slugs that combine in the URL path.</summary>
public sealed record ListSpec(string Sort, IReadOnlyList<string> Filters)
{
    public const string TopRated = "top-rated";
    public const string Popular = "";
    public const string NewAndPopular = "new-and-popular";
    public const string Newest = "newest";
    public const string Featured = "featured";

    public static ListSpec Of(string sort, params string[] filters) => new(sort, filters);

    public bool IsTopRated => Sort == TopRated;

    public string FilterKey => string.Join('/', Filters.OrderBy(f => f, StringComparer.Ordinal));

    /// <summary>Stable key for cursors and positions: "popular/tag-2d", "top-rated/year-2024", "new-and-popular".</summary>
    public string Key => (Sort.Length == 0 ? "popular" : Sort) + (Filters.Count > 0 ? "/" + FilterKey : "");

    public string Path => "/games" + (Sort.Length > 0 ? "/" + Sort : "") + (Filters.Count > 0 ? "/" + string.Join('/', Filters) : "");

    public string Url(int page, bool json) =>
        ItchCatalog.BaseUrl + Path + (json ? $"?page={page}&format=json" : page > 1 ? $"?page={page}" : "");

    public string RssUrl(int page) => ItchCatalog.BaseUrl + Path + ".xml" + (page > 1 ? $"?page={page}" : "");

    public int? Year => Filters
        .Where(f => f.StartsWith("year-", StringComparison.Ordinal))
        .Select(f => int.TryParse(f[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : (int?)null)
        .FirstOrDefault();

    public bool IsNsfw => Filters.Contains("nsfw");

    public bool HasPriceFilter => Filters.Any(f => f is "free" or "store" or "on-sale" or "5-dollars-or-less" or "15-dollars-or-less");

    public bool HasGenreFilter => Filters.Any(ItchCatalog.IsGenreSlug);

    public bool HasPlatformFilter => Filters.Any(f => f.StartsWith("platform-", StringComparison.Ordinal));

    /// <summary>Filters that mean "the game belongs to this segment" and are stored as game tags.</summary>
    public IReadOnlyList<string> MembershipFilters => Filters.Where(IsMembershipSlug).ToList();

    public static bool IsMembershipSlug(string f) =>
        f.StartsWith("tag-", StringComparison.Ordinal) || f.StartsWith("genre-", StringComparison.Ordinal)
        || f.StartsWith("made-with-", StringComparison.Ordinal) || f.StartsWith("duration-", StringComparison.Ordinal)
        || f.StartsWith("lang-", StringComparison.Ordinal) || f.StartsWith("input-", StringComparison.Ordinal)
        || f.StartsWith("accessibility-", StringComparison.Ordinal) || f.StartsWith("multiplayer-", StringComparison.Ordinal);

    public ListSpec With(string filter) => new(Sort, [.. Filters, filter]);

    public override string ToString() => Path;
}

public enum ListStatus
{
    InProgress = 0,
    Complete = 1,
    Truncated = 2,
    Unsupported = 3,
}

public sealed record ListCursor(int Page, ListStatus Status, int? Total, DateTime UpdatedAt);

/// <summary>A unit of work for <see cref="ListCrawler"/>.</summary>
public sealed record ListTask(
    ListSpec Spec,
    int MaxPages,
    bool StopAtUnrated = true,
    bool Split = false,
    bool UseCursor = false,
    bool RecordPositions = false);

public sealed record ListOutcome(int Pages, bool ReachedEnd, bool Truncated, int? Total, bool Skipped = false, bool Unsupported = false);

public static class CrawlMeta
{
    public const string YearsKey = "meta:years";

    /// <summary>
    /// Years for which itch.io has a "year-YYYY" filter; any other year redirects to the unfiltered list.
    /// Later years are added once a probe of their page 1 is not redirected.
    /// </summary>
    public static readonly int[] KnownYears = Enumerable.Range(ItchCatalog.FirstYear, 2024 - ItchCatalog.FirstYear + 1).ToArray();

    public static async Task<int[]> AvailableYearsAsync(CursorStore cursors, CancellationToken ct)
    {
        var confirmed = await cursors.GetAsync<int[]>(YearsKey, ct) ?? [];
        return confirmed.Union(KnownYears).Order().ToArray();
    }

    public static async Task ConfirmYearAsync(CursorStore cursors, int year, CancellationToken ct)
    {
        var confirmed = await cursors.GetAsync<int[]>(YearsKey, ct) ?? [];
        if (confirmed.Contains(year) || KnownYears.Contains(year)) return;
        await cursors.SetAsync(YearsKey, confirmed.Append(year).Order().ToArray(), ct);
    }
}
