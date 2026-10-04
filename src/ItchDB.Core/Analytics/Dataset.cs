using ItchDB.Core.Domain;

namespace ItchDB.Core.Analytics;

/// <summary>Compact, read-only view of a game used by all analytics (loaded once per crawl run).</summary>
public sealed class GameRow
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? ShortText { get; init; }
    public long? DeveloperId { get; init; }
    public string? AuthorName { get; init; }

    /// <summary>Number of ratings; 0 when the game has none or the count was never seen.</summary>
    public int Ratings { get; init; }
    public bool RatingsKnown { get; init; }
    public double? RatingAvg { get; init; }
    public int? Comments { get; init; }

    public DateTime? PublishedAt { get; init; }
    public int? Year { get; init; }
    public PriceModel Price { get; init; }
    public int? PriceCents { get; init; }
    public int? SalePercent { get; init; }
    public Platforms Platforms { get; init; }
    public string? Genre { get; init; }
    public GameStatus Status { get; init; }
    public AiUsage Ai { get; init; }
    public int Screenshots { get; init; }
    public bool IsNsfw { get; init; }
    public bool IsFeatured { get; init; }
    public DateTime? FeaturedAt { get; init; }
    public bool IsJamEntry { get; init; }
    public bool HasDetails { get; init; }
    public int? PopularRank { get; init; }
    public DateTime FirstSeenAt { get; init; }

    /// <summary>Tag ids sorted ascending (list memberships and tags from the game page).</summary>
    public int[] TagIds { get; init; } = [];

    // Filled from daily snapshots by the loader.
    public int? DeltaRatings { get; set; }
    public int? DeltaComments { get; set; }
    public int DeltaDays { get; set; }
    public int? RankGain { get; set; }
    public double Hot { get; set; }
    public double WeightedRating { get; set; }

    public bool HasTag(int tagId) => Array.BinarySearch(TagIds, tagId) >= 0;

    /// <summary>Age in days; falls back to the middle of the publication year, then to the first time the crawler saw it.</summary>
    public double AgeDays(DateTime now)
    {
        var start = PublishedAt ?? (Year is { } y ? new DateTime(y, 7, 1, 0, 0, 0, DateTimeKind.Utc) : FirstSeenAt);
        if (start > now) start = now;
        return (now - start).TotalDays;
    }

    public bool IsFree => Price is PriceModel.Free or PriceModel.NameYourPrice || PriceCents is 0;
}

public sealed class TagRow
{
    public int Id { get; init; }
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    public TagKind Kind { get; init; }
    public int? ProjectsCount { get; init; }
    public int? ThisWeekCount { get; init; }
    public string? Description { get; init; }
    public string[] Related { get; init; } = [];

    /// <summary>"(N results)" of the browse list for this filter; the most precise size of the segment.</summary>
    public int? ListingTotal { get; init; }

    /// <summary>The top-rated list was crawled until unrated games appeared: every rated game of the segment is known.</summary>
    public bool RatedComplete { get; init; }

    /// <summary>Game ids of page 1 of the "popular" list for this tag on the latest crawled day.</summary>
    public long[] PopularPage1 { get; init; } = [];

    public IReadOnlyList<(DateOnly Day, int Projects, int ThisWeek)> History { get; init; } = [];

    public int? TotalItch => ListingTotal ?? ProjectsCount;
}

public sealed class DeveloperRow
{
    public long Id { get; init; }
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Url { get; init; }
    public int? Followers { get; init; }
    public int? FollowersDelta30 { get; init; }
    public DateTime? RegisteredAt { get; init; }

    public string Name => DisplayName ?? Username ?? Id.ToString();
}

public sealed class AnalyticsDataset
{
    public static readonly AnalyticsDataset Empty = new();

    public DateTime BuiltAt { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<GameRow> Games { get; init; } = [];
    public IReadOnlyDictionary<long, GameRow> GamesById { get; init; } = new Dictionary<long, GameRow>();
    public IReadOnlyDictionary<int, TagRow> Tags { get; init; } = new Dictionary<int, TagRow>();
    public IReadOnlyDictionary<string, TagRow> TagsBySlug { get; init; } = new Dictionary<string, TagRow>();
    public IReadOnlyDictionary<int, GameRow[]> GamesByTag { get; init; } = new Dictionary<int, GameRow[]>();
    public IReadOnlyDictionary<long, DeveloperRow> Developers { get; init; } = new Dictionary<long, DeveloperRow>();

    /// <summary>Latest "(N results)" per filter key ("" = whole catalog, "year-2024", "tag-2d", "free/genre-action/year-2024").</summary>
    public IReadOnlyDictionary<string, int> ListingTotals { get; init; } = new Dictionary<string, int>();

    /// <summary>Latest crawled "popular"-type lists: list key → game ids in order.</summary>
    public IReadOnlyDictionary<string, long[]> LatestPositions { get; init; } = new Dictionary<string, long[]>();
    public DateOnly? PositionsDay { get; init; }

    public int SnapshotDays { get; init; }
    public DateOnly? FirstSnapshotDay { get; init; }
    public int YearsComplete { get; init; }
    public int YearsTotal { get; init; }

    public int? CatalogTotal => ListingTotals.TryGetValue("", out var v) ? v : null;
    public bool IsEmpty => Games.Count == 0;

    public TagRow? Tag(string slug) => TagsBySlug.TryGetValue(slug, out var t) ? t : null;

    public IReadOnlyList<GameRow> Members(int tagId) => GamesByTag.TryGetValue(tagId, out var g) ? g : [];

    public int? Total(string filterKey) => ListingTotals.TryGetValue(filterKey, out var v) ? v : null;
}

/// <summary>Global UI filters that every metric respects.</summary>
public sealed record AnalyticsFilter(bool ShowNsfw, int MinRatings, bool IncludeUnrated = true)
{
    public static readonly AnalyticsFilter Default = new(false, 4, true);

    public bool Includes(GameRow g) => ShowNsfw || !g.IsNsfw;

    public string Key => $"{(ShowNsfw ? 1 : 0)}:{MinRatings}:{(IncludeUnrated ? 1 : 0)}";
}

/// <summary>Tunable analytics parameters (mirrors the settings page).</summary>
public sealed record AnalyticsParams
{
    public int SuccessThreshold { get; init; } = 10;
    public double ShrinkK { get; init; } = 20;
    public int MinSegmentSample { get; init; } = 15;
    public double WeightDemand { get; init; } = 0.35;
    public double WeightSuccess { get; init; } = 0.25;
    public double WeightCompetition { get; init; } = 0.25;
    public double WeightTrend { get; init; } = 0.15;
    public double HotA { get; init; } = 0.5;
    public double HotB { get; init; } = 0.25;
    public double HotG { get; init; } = 0.25;
    public int BayesMinVotes { get; init; } = 20;
    public double RaterShare { get; init; } = 0.01;
    public double RaterShareLow { get; init; } = 0.005;
    public double RaterShareHigh { get; init; } = 0.02;
    public double WebPlayShare { get; init; } = 0.37;
    public double DownloadShare { get; init; } = 0.08;
    public double? CalibratedViewsPerRating { get; init; }
    public double? CalibratedDownloadsPerRating { get; init; }
}
