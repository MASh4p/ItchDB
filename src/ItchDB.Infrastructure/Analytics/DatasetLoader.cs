using System.Data.Common;
using System.Globalization;
using ItchDB.Core.Analytics;
using ItchDB.Core.Catalog;
using ItchDB.Infrastructure.Crawling;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Analytics;

/// <summary>Builds the in-memory <see cref="AnalyticsDataset"/> that every page and report reads.</summary>
public sealed class DatasetLoader(IDbContextFactory<ItchDbContext> dbFactory, CursorStore cursors)
{
    /// <summary>Deltas (Δratings, rank gain) compare the latest snapshot with the oldest one inside this window.</summary>
    public const int DeltaWindowDays = 7;

    private const string DayFormat = "yyyy-MM-dd";

    public async Task<AnalyticsDataset> LoadAsync(AnalyticsParams p, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        await db.Database.OpenConnectionAsync(ct);
        var conn = db.Database.GetDbConnection();

        var tagMap = await LoadGameTagsAsync(conn, ct);
        var raw = await db.Games.Where(g => !g.IsRemoved).Select(g => new
        {
            g.Id, g.Title, g.Url, g.CoverUrl, g.ShortText, g.DeveloperId, g.AuthorName,
            g.RatingCount, g.RatingAvg, g.CommentsCount, g.PublishedAt, g.PublishedYear,
            g.PriceModel, g.PriceCents, g.SalePercent, g.Platforms, g.Genre, g.Status, g.Ai, g.ScreenshotsCount,
            g.IsNsfw, g.IsFeatured, g.FeaturedAt, g.IsJamEntry, g.DetailsFetchedAt, g.PopularRank, g.FirstSeenAt,
        }).ToListAsync(ct);

        var games = new List<GameRow>(raw.Count);
        foreach (var g in raw)
        {
            games.Add(new GameRow
            {
                Id = g.Id,
                Title = g.Title,
                Url = g.Url,
                CoverUrl = g.CoverUrl,
                ShortText = g.ShortText,
                DeveloperId = g.DeveloperId,
                AuthorName = g.AuthorName,
                Ratings = g.RatingCount ?? 0,
                RatingsKnown = g.RatingCount is not null,
                RatingAvg = g.RatingAvg,
                Comments = g.CommentsCount,
                PublishedAt = Utc(g.PublishedAt),
                Year = g.PublishedYear ?? g.PublishedAt?.Year,
                Price = g.PriceModel,
                PriceCents = g.PriceCents,
                SalePercent = g.SalePercent,
                Platforms = g.Platforms,
                Genre = g.Genre,
                Status = g.Status,
                Ai = g.Ai,
                Screenshots = g.ScreenshotsCount,
                IsNsfw = g.IsNsfw,
                IsFeatured = g.IsFeatured,
                FeaturedAt = Utc(g.FeaturedAt),
                IsJamEntry = g.IsJamEntry,
                HasDetails = g.DetailsFetchedAt is not null,
                PopularRank = g.PopularRank,
                FirstSeenAt = DateTime.SpecifyKind(g.FirstSeenAt, DateTimeKind.Utc),
                TagIds = tagMap.TryGetValue(g.Id, out var ids) ? ids : [],
            });
        }
        tagMap.Clear();
        var byId = games.ToDictionary(g => g.Id);

        await ApplyDeltasAsync(conn, byId, ct);
        var (positions, positionsDay) = await LoadLatestPositionsAsync(conn, ct);
        await ApplyRankGainAsync(conn, byId, ct);

        var listCursors = await cursors.GetAllAsync<ListCursor>("list:", ct);
        var totals = await LoadListingTotalsAsync(conn, ct);
        var history = await LoadTagHistoryAsync(conn, ct);
        var tagEntities = await db.Tags.Select(t => new
        {
            t.Id, t.Slug, t.Name, t.Kind, t.ProjectsCount, t.ThisWeekCount, t.Description, t.RelatedTags,
        }).ToListAsync(ct);

        var tags = new Dictionary<int, TagRow>(tagEntities.Count);
        foreach (var t in tagEntities)
        {
            tags[t.Id] = new TagRow
            {
                Id = t.Id,
                Slug = t.Slug,
                Name = t.Name,
                Kind = t.Kind,
                ProjectsCount = t.ProjectsCount,
                ThisWeekCount = t.ThisWeekCount,
                Description = t.Description,
                Related = string.IsNullOrWhiteSpace(t.RelatedTags) ? [] : t.RelatedTags.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                ListingTotal = totals.TryGetValue(t.Slug, out var total) ? total : null,
                RatedComplete = IsRatedComplete(ListSpec.Of(ListSpec.TopRated, t.Slug), listCursors),
                PopularPage1 = positions.TryGetValue("popular/" + t.Slug, out var pop)
                    ? pop.Take(ItchCatalog.PageSize).Where(id => id != 0).ToArray()
                    : [],
                History = history.TryGetValue(t.Id, out var h) ? h : [],
            };
        }

        var members = new Dictionary<int, List<GameRow>>();
        foreach (var g in games)
        {
            foreach (var id in g.TagIds)
            {
                if (!members.TryGetValue(id, out var list)) members[id] = list = [];
                list.Add(g);
            }
        }

        var developers = await LoadDevelopersAsync(db, conn, ct);
        var (snapshotDays, firstSnapshot) = await LoadSnapshotSpanAsync(conn, ct);
        var years = await CrawlMeta.AvailableYearsAsync(cursors, ct);

        Scores.ApplyWeightedRating(games, p.BayesMinVotes);
        Scores.ApplyHot(games, p, now);

        return new AnalyticsDataset
        {
            BuiltAt = now,
            Games = games,
            GamesById = byId,
            Tags = tags,
            TagsBySlug = tags.Values.GroupBy(t => t.Slug).ToDictionary(x => x.Key, x => x.First()),
            GamesByTag = members.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
            Developers = developers,
            ListingTotals = totals,
            LatestPositions = positions,
            PositionsDay = positionsDay,
            SnapshotDays = snapshotDays,
            FirstSnapshotDay = firstSnapshot,
            YearsComplete = years.Count(y => IsRatedComplete(ListSpec.Of(ListSpec.TopRated, $"year-{y}"), listCursors)),
            YearsTotal = years.Length,
        };
    }

    /// <summary>
    /// Every rated game of the list is in the database: the list itself reached unrated games,
    /// or it was truncated and each of its partitions is complete.
    /// </summary>
    public static bool IsRatedComplete(ListSpec spec, IReadOnlyDictionary<string, ListCursor> listCursors, int depth = 0)
    {
        if (!listCursors.TryGetValue("list:" + spec.Key, out var c)) return false;
        if (c.Status is ListStatus.Complete or ListStatus.Unsupported) return true;
        if (c.Status != ListStatus.Truncated || depth >= 3) return false;
        var parts = ListCrawler.SplitSpec(spec);
        return parts.Count > 0 && parts.All(s => IsRatedComplete(s, listCursors, depth + 1));
    }

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private static DateOnly ParseDay(string s) => DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture);

    private static string FormatDay(DateOnly d) => d.ToString(DayFormat, CultureInfo.InvariantCulture);

    private static DbCommand Command(DbConnection conn, string sql, params (string Name, object Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            var prm = cmd.CreateParameter();
            prm.ParameterName = name;
            prm.Value = value;
            cmd.Parameters.Add(prm);
        }
        return cmd;
    }

    private static async Task<string?> ScalarAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = Command(conn, sql);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<Dictionary<long, int[]>> LoadGameTagsAsync(DbConnection conn, CancellationToken ct)
    {
        var map = new Dictionary<long, int[]>();
        await using var cmd = Command(conn, "SELECT GameId, TagId FROM GameTags ORDER BY GameId, TagId");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        long current = -1;
        var buffer = new List<int>(32);
        while (await r.ReadAsync(ct))
        {
            var gameId = r.GetInt64(0);
            if (gameId != current)
            {
                if (buffer.Count > 0) map[current] = buffer.ToArray();
                buffer.Clear();
                current = gameId;
            }
            buffer.Add(r.GetInt32(1));
        }
        if (buffer.Count > 0) map[current] = buffer.ToArray();
        return map;
    }

    /// <summary>Δratings and Δcomments between the oldest and the latest snapshot of each game within the window.</summary>
    private static async Task ApplyDeltasAsync(DbConnection conn, Dictionary<long, GameRow> byId, CancellationToken ct)
    {
        if (await ScalarAsync(conn, "SELECT MAX(Day) FROM GameSnapshots", ct) is not { } maxDay) return;
        var from = ParseDay(maxDay).AddDays(-DeltaWindowDays);

        await using var cmd = Command(conn,
            "SELECT GameId, Day, RatingCount, CommentsCount FROM GameSnapshots WHERE Day >= $from ORDER BY GameId, Day",
            ("$from", FormatDay(from)));
        await using var r = await cmd.ExecuteReaderAsync(ct);

        long current = -1;
        DateOnly firstDay = default, lastDay = default;
        int? firstRatings = null, lastRatings = null, firstComments = null, lastComments = null;

        void Flush()
        {
            if (current < 0 || lastDay <= firstDay || !byId.TryGetValue(current, out var g)) return;
            g.DeltaDays = lastDay.DayNumber - firstDay.DayNumber;
            g.DeltaRatings = firstRatings is { } a && lastRatings is { } b ? b - a : null;
            g.DeltaComments = firstComments is { } c && lastComments is { } e ? e - c : null;
        }

        while (await r.ReadAsync(ct))
        {
            var gameId = r.GetInt64(0);
            var day = ParseDay(r.GetString(1));
            int? ratings = r.IsDBNull(2) ? null : r.GetInt32(2);
            int? comments = r.IsDBNull(3) ? null : r.GetInt32(3);
            if (gameId != current)
            {
                Flush();
                current = gameId;
                firstDay = day;
                firstRatings = ratings;
                firstComments = comments;
            }
            else
            {
                if (firstRatings is null && ratings is not null) firstRatings = ratings;
                if (firstComments is null && comments is not null) firstComments = comments;
            }
            lastDay = day;
            lastRatings = ratings ?? lastRatings;
            lastComments = comments ?? lastComments;
        }
        Flush();
    }

    /// <summary>
    /// Latest crawl of every positions list, as game ids indexed by position − 1 (0 where a page was not crawled).
    /// </summary>
    private static async Task<(Dictionary<string, long[]> Lists, DateOnly? Day)> LoadLatestPositionsAsync(DbConnection conn, CancellationToken ct)
    {
        var lists = new Dictionary<string, long[]>();
        DateOnly? latest = null;
        await using var cmd = Command(conn, """
            SELECT p.ListKey, p.Position, p.GameId, p.Day
            FROM TagPositions p
            JOIN (SELECT ListKey, MAX(Day) AS D FROM TagPositions GROUP BY ListKey) m ON m.ListKey = p.ListKey AND m.D = p.Day
            ORDER BY p.ListKey, p.Position
            """);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? key = null;
        var buffer = new List<long>(256);

        void Flush()
        {
            if (key is not null && buffer.Count > 0) lists[key] = buffer.ToArray();
            buffer.Clear();
        }

        while (await r.ReadAsync(ct))
        {
            var listKey = r.GetString(0);
            if (listKey != key)
            {
                Flush();
                key = listKey;
            }
            var position = r.GetInt32(1);
            while (buffer.Count < position - 1) buffer.Add(0);
            if (buffer.Count == position - 1) buffer.Add(r.GetInt64(2));
            var day = ParseDay(r.GetString(3));
            if (latest is null || day > latest) latest = day;
        }
        Flush();
        return (lists, latest);
    }

    /// <summary>
    /// Places climbed in the global "popular" list since the oldest crawl within the window.
    /// A game that was below the crawled depth then counts as depth + 1.
    /// </summary>
    private static async Task ApplyRankGainAsync(DbConnection conn, Dictionary<long, GameRow> byId, CancellationToken ct)
    {
        var days = new List<DateOnly>();
        await using (var cmd = Command(conn, "SELECT DISTINCT Day FROM TagPositions WHERE ListKey = 'popular' ORDER BY Day DESC LIMIT 30"))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) days.Add(ParseDay(r.GetString(0)));
        if (days.Count < 2) return;

        var latest = days[0];
        var baseDay = days.Where(d => d < latest && d >= latest.AddDays(-DeltaWindowDays)).DefaultIfEmpty(days[1]).Min();
        var now = await PositionsOnAsync(conn, latest, ct);
        var then = await PositionsOnAsync(conn, baseDay, ct);
        if (now.Count == 0 || then.Count == 0) return;
        var depth = then.Values.Max();
        foreach (var (gameId, position) in now)
        {
            if (!byId.TryGetValue(gameId, out var g)) continue;
            var before = then.TryGetValue(gameId, out var b) ? b : depth + 1;
            g.RankGain = before - position;
            if (g.DeltaDays == 0) g.DeltaDays = latest.DayNumber - baseDay.DayNumber;
        }
    }

    private static async Task<Dictionary<long, int>> PositionsOnAsync(DbConnection conn, DateOnly day, CancellationToken ct)
    {
        var map = new Dictionary<long, int>();
        await using var cmd = Command(conn, "SELECT GameId, Position FROM TagPositions WHERE Day = $day AND ListKey = 'popular'",
            ("$day", FormatDay(day)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) map.TryAdd(r.GetInt64(0), r.GetInt32(1));
        return map;
    }

    /// <summary>Latest "(N results)" for every filter combination ever crawled.</summary>
    private static async Task<Dictionary<string, int>> LoadListingTotalsAsync(DbConnection conn, CancellationToken ct)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var cmd = Command(conn, """
            SELECT t.FilterKey, t.Results
            FROM ListingTotals t
            JOIN (SELECT FilterKey, MAX(Day) AS D FROM ListingTotals GROUP BY FilterKey) m ON m.FilterKey = t.FilterKey AND m.D = t.Day
            """);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) map[r.GetString(0)] = r.GetInt32(1);
        DropInconsistentTotals(map);
        return map;
    }

    /// <summary>
    /// A filter can never match fewer games than a narrower filter that contains it ("tag-2d" ≥ "tag-2d/year-2024").
    /// Such a total was recorded from a curated list (featured, new and popular) and is discarded.
    /// </summary>
    public static void DropInconsistentTotals(Dictionary<string, int> totals)
    {
        var parts = totals.Keys.ToDictionary(k => k, k => k.Length == 0 ? [] : k.Split('/').ToHashSet(StringComparer.Ordinal));
        var invalid = new List<string>();
        foreach (var (key, value) in totals)
        {
            var own = parts[key];
            var narrower = totals.Where(t => t.Key != key && parts[t.Key].Count > own.Count && own.IsSubsetOf(parts[t.Key]))
                .Select(t => t.Value).DefaultIfEmpty(0).Max();
            if (value * 1.02 < narrower) invalid.Add(key);
        }
        foreach (var key in invalid) totals.Remove(key);
    }

    private static async Task<Dictionary<int, IReadOnlyList<(DateOnly Day, int Projects, int ThisWeek)>>> LoadTagHistoryAsync(
        DbConnection conn, CancellationToken ct)
    {
        var map = new Dictionary<int, List<(DateOnly, int, int)>>();
        await using var cmd = Command(conn, "SELECT TagId, Day, ProjectsCount, ThisWeekCount FROM TagCatalogSnapshots ORDER BY TagId, Day");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            if (!map.TryGetValue(id, out var list)) map[id] = list = [];
            list.Add((ParseDay(r.GetString(1)), r.GetInt32(2), r.GetInt32(3)));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(DateOnly, int, int)>)kv.Value);
    }

    /// <summary>Developers with the change of followers over ~30 days (at least a week apart).</summary>
    private static async Task<Dictionary<long, DeveloperRow>> LoadDevelopersAsync(ItchDbContext db, DbConnection conn, CancellationToken ct)
    {
        var deltas = new Dictionary<long, int>();
        if (await ScalarAsync(conn, "SELECT MAX(Day) FROM DeveloperSnapshots", ct) is { } maxDay)
        {
            var latest = ParseDay(maxDay);
            await using var cmd = Command(conn,
                "SELECT DeveloperId, Day, Followers FROM DeveloperSnapshots WHERE Day >= $from ORDER BY DeveloperId, Day",
                ("$from", FormatDay(latest.AddDays(-30))));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            long current = -1;
            DateOnly firstDay = default, lastDay = default;
            int first = 0, last = 0;

            void Flush()
            {
                if (current >= 0 && lastDay.DayNumber - firstDay.DayNumber >= 7) deltas[current] = last - first;
            }

            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt64(0);
                var day = ParseDay(r.GetString(1));
                var followers = r.GetInt32(2);
                if (id != current)
                {
                    Flush();
                    current = id;
                    firstDay = day;
                    first = followers;
                }
                lastDay = day;
                last = followers;
            }
            Flush();
        }

        var rows = await db.Developers.Select(d => new { d.Id, d.Username, d.DisplayName, d.Url, d.Followers, d.RegisteredAt }).ToListAsync(ct);
        return rows.ToDictionary(d => d.Id, d => new DeveloperRow
        {
            Id = d.Id,
            Username = d.Username,
            DisplayName = d.DisplayName,
            Url = d.Url,
            Followers = d.Followers,
            FollowersDelta30 = deltas.TryGetValue(d.Id, out var delta) ? delta : null,
            RegisteredAt = Utc(d.RegisteredAt),
        });
    }

    private static async Task<(int Days, DateOnly? First)> LoadSnapshotSpanAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = Command(conn, "SELECT COUNT(DISTINCT Day), MIN(Day) FROM GameSnapshots");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return (0, null);
        var days = r.IsDBNull(0) ? 0 : r.GetInt32(0);
        DateOnly? first = r.IsDBNull(1) ? null : ParseDay(r.GetString(1));
        return (days, first);
    }
}

public static class AnalyticsSettings
{
    public static AnalyticsParams ToAnalyticsParams(this AppSettingsModel s) => new()
    {
        SuccessThreshold = s.SuccessThreshold,
        ShrinkK = s.ShrinkK,
        MinSegmentSample = s.MinSegmentSample,
        WeightDemand = s.WeightDemand,
        WeightSuccess = s.WeightSuccess,
        WeightCompetition = s.WeightCompetition,
        WeightTrend = s.WeightTrend,
        HotA = s.HotA,
        HotB = s.HotB,
        HotG = s.HotG,
        BayesMinVotes = s.BayesMinVotes,
        RaterShare = s.RaterShare,
        RaterShareLow = s.RaterShareLow,
        RaterShareHigh = s.RaterShareHigh,
        WebPlayShare = s.WebPlayShare,
        DownloadShare = s.DownloadShare,
        CalibratedViewsPerRating = s.CalibratedViewsPerRating,
        CalibratedDownloadsPerRating = s.CalibratedDownloadsPerRating,
    };
}
