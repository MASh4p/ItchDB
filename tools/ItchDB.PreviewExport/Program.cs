using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ItchDB.Core;
using ItchDB.Core.Analytics;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Infrastructure.Crawling;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var root = RepoRoot();
var dataArg = args.FirstOrDefault(a => !a.StartsWith('-'));
var dataRoot = dataArg is not null ? Path.GetFullPath(dataArg) : Path.Combine(root, "src", "ItchDB.Web", "data");
var outFile = Path.Combine(root, "preview", "data", "demo.json");
Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

if (!File.Exists(Path.Combine(dataRoot, "itchdb.db")))
{
    Console.Error.WriteLine("No database at " + dataRoot);
    return 1;
}

var services = new ServiceCollection();
services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
services.AddItchInfrastructure(dataRoot);
await using var sp = services.BuildServiceProvider();
await DatabaseInitializer.InitializeAsync(sp);

var settings = sp.GetRequiredService<SettingsService>().Current;
var parameters = settings.ToAnalyticsParams();
var filter = new AnalyticsFilter(settings.DefaultShowNsfw, Math.Max(1, settings.DefaultMinRatings), settings.DefaultIncludeUnrated);
var smallGames = Math.Clamp(settings.SmallGameThreshold > 0 ? settings.SmallGameThreshold : settings.MinSegmentSample, 1, 100_000);
var useMean = settings.DemandUsesMean;

Console.WriteLine("Loading dataset…");
var loader = sp.GetRequiredService<DatasetLoader>();
var data = await loader.LoadAsync(parameters);
var now = DateTime.UtcNow;
TextLang.Set(UiLanguage.Ru);
var ru = new AnalyticsSnapshot(data, parameters, now, UiLanguage.Ru);
TextLang.Set(UiLanguage.En);
var en = new AnalyticsSnapshot(data, parameters, now, UiLanguage.En);
TextLang.Set(UiLanguage.Ru);

Console.WriteLine($"Loaded {data.Games.Count} games. Building pages…");
var payload = Build(data, ru, en, filter, settings, parameters, smallGames, useMean, sp, now);
var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
});
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
File.WriteAllText(outFile, json, utf8);
var script = Path.Combine(Path.GetDirectoryName(outFile)!, "demo.js");
File.WriteAllText(script, "window.ITCH_DEMO = " + json + ";\n", utf8);
Console.WriteLine("Wrote " + outFile);
return 0;

static object Build(AnalyticsDataset data, AnalyticsSnapshot ru, AnalyticsSnapshot en, AnalyticsFilter filter, AppSettingsModel settings, AnalyticsParams parameters, int smallGames, bool useMean, ServiceProvider sp, DateTime now)
{
    var games = ru.Games(filter).ToList();
    var all = ru.All(filter);
    var genresRu = SegmentAnalyzer.WithDemand(ru.Genres(filter), parameters, useMean);
    var genresEn = SegmentAnalyzer.WithDemand(en.Genres(filter), parameters, useMean).ToDictionary(s => s.Key);
    var tagsRu = SegmentAnalyzer.WithDemand(ru.Tags(filter), parameters, useMean);
    var tagsEn = SegmentAnalyzer.WithDemand(en.Tags(filter), parameters, useMean).ToDictionary(s => s.Key);

    var insightsRu = ru.Insights(filter);
    var insightsEn = en.Insights(filter);
    var insights = insightsRu.Zip(insightsEn).Select(p => new
    {
        title = Bi(p.First.Title, p.Second.Title),
        text = Bi(p.First.Text, p.Second.Text),
        tone = p.First.Tone.ToString().ToLowerInvariant(),
        link = p.First.Link,
    }).ToList();

    var known = games.Where(g => g.RatingsKnown).ToList();
    (string Label, int From, int To)[] bins =
        [("1–4", 1, 4), ("5–9", 5, 9), ("10–24", 10, 24), ("25–49", 25, 49), ("50–99", 50, 99), ("100–249", 100, 249), ("250–999", 250, 999), ("1000+", 1000, int.MaxValue)];
    var distribution = new List<object> { new { label = "0", value = all.Bin0 } };
    distribution.AddRange(bins.Select(b => new { label = b.Label, value = known.Count(g => g.Ratings >= b.From && g.Ratings <= b.To) }));

    var yearPoints = ru.Years(filter).Where(y => y.Year >= 2015 && (y.TotalItch is not null || y.Known >= 20)).OrderBy(y => y.Year).ToList();
    var years = yearPoints.Select(y => new { year = y.Year, itch = y.TotalItch ?? 0, known = y.Known, mean = double.IsFinite(y.Mean) ? Math.Round(y.Mean, 4) : 0 }).ToList();

    var genreChart = genresRu.Where(s => s.Qualified > 0).OrderByDescending(s => useMean ? s.AdjMean : s.AdjMedian).Select(s =>
    {
        genresEn.TryGetValue(s.Key, out var e);
        return new { name = Bi(s.Name, e?.Name ?? s.Name), value = Math.Round(useMean ? s.AdjMean : s.AdjMedian, 4) };
    }).ToList();
    var genreSupply = genresRu.OrderByDescending(s => s.Denominator).Select(s =>
    {
        genresEn.TryGetValue(s.Key, out var e);
        return new { name = Bi(s.Name, e?.Name ?? s.Name), value = s.Denominator };
    }).ToList();

    var niches = tagsRu.Where(s => s.Opportunity is not null).OrderByDescending(s => s.Opportunity).Take(7).Select(s =>
    {
        tagsEn.TryGetValue(s.Key, out var e);
        return Seg(s, e, useMean);
    }).ToList();

    var hotAvailable = games.Any(g => g.Hot > 0);
    var topGames = (hotAvailable
        ? games.Where(g => g.Hot > 0).OrderByDescending(g => g.Hot)
        : games.OrderByDescending(g => g.Ratings)).Take(6).Select(GameJson).ToList();

    var matched = games.OrderByDescending(g => g.Ratings).ThenByDescending(g => g.RatingAvg).ToList();
    const int gamesCap = 5000;
    var shown = Math.Min(matched.Count, gamesCap);
    var gamePage = matched.Take(50).Select(GameJson).ToList();

    var demand = games.Where(g => g.Ratings > 0).OrderByDescending(g => g.Ratings).ThenByDescending(g => g.RatingAvg).Take(500).ToList();
    var topsPage = demand.Take(25).Select(GameJson).ToList();

    var genreRows = genresRu.OrderByDescending(s => s.Opportunity ?? -1).Take(25).Select(s =>
    {
        genresEn.TryGetValue(s.Key, out var e);
        return Seg(s, e, useMean);
    }).ToList();

    var driversRu = ru.Drivers(filter);
    var driversEn = en.Drivers(filter).Results.ToDictionary(r => r.Feature.Key);
    var topDrivers = driversRu.Results.Where(r => r.Multiplier is not null && r.Significant && r.Group is not ("Витрина" or "Storefront"))
        .OrderByDescending(r => Math.Abs(Math.Log(r.Multiplier!.Value))).Take(6).Select(r =>
        {
            driversEn.TryGetValue(r.Feature.Key, out var e);
            return new
            {
                name = Bi(r.Name, e?.Name ?? r.Name),
                group = Bi(r.Group, e?.Group ?? r.Group),
                multiplier = r.Multiplier,
                successWith = r.SuccessWith,
                successWithout = r.SuccessWithout,
                good = r.Multiplier > 1,
            };
        }).ToList();

    var map = Map(tagsRu, tagsEn, parameters, smallGames, useMean);

    var devs = Developers(ru, filter);
    var jams = Jams(ru, en, filter, parameters, sp, now);
    var collection = Collection(sp, settings, now);

    var tagOptions = data.Tags.Values
        .Where(t => data.Members(t.Id).Count > 0 && (filter.ShowNsfw || !ItchCatalog.NsfwTags.Contains(t.Slug)))
        .OrderByDescending(t => data.Members(t.Id).Count)
        .Take(30)
        .Select(t => new { slug = t.Slug, name = Bi(Segments.DisplayName(t, UiLanguage.Ru), Segments.DisplayName(t, UiLanguage.En)) })
        .ToList();

    return new
    {
        builtAt = now.ToString("yyyy-MM-dd"),
        filters = new
        {
            minRatings = filter.MinRatings,
            includeUnrated = filter.IncludeUnrated,
            showNsfw = filter.ShowNsfw,
            success = parameters.SuccessThreshold,
            smallGames,
            demandMean = useMean,
            bayes = parameters.BayesMinVotes,
            weights = new { demand = parameters.WeightDemand, success = parameters.WeightSuccess, competition = parameters.WeightCompetition, trend = parameters.WeightTrend },
            viewsPerRating = parameters.CalibratedViewsPerRating is > 0 ? parameters.CalibratedViewsPerRating.Value : 1 / Math.Max(1e-6, parameters.RaterShare),
            downloadsPerRating = parameters.CalibratedDownloadsPerRating is > 0
                ? parameters.CalibratedDownloadsPerRating.Value
                : (parameters.CalibratedViewsPerRating is > 0 ? parameters.CalibratedViewsPerRating.Value : 1 / Math.Max(1e-6, parameters.RaterShare)) * parameters.DownloadShare,
            calibrated = parameters.CalibratedViewsPerRating is > 0,
        },
        counts = new
        {
            genres = genresRu.Count,
            tags = tagsRu.Count,
            engines = ru.Engines(filter).Count,
            formats = ru.Formats(filter).Count,
            languages = ru.Languages(filter).Count,
        },
        options = new
        {
            genres = ItchCatalog.Genres.OrderBy(g => g.RussianName).Select(g => new { slug = g.Slug, name = Bi(g.RussianName, g.EnglishName) }),
            platforms = ItchCatalog.PlatformFilters.Select(p => new { slug = p.Slug, name = Bi(CatalogNames.Platform(p.Slug, UiLanguage.Ru), CatalogNames.Platform(p.Slug, UiLanguage.En)) }),
            tags = tagOptions,
            years = Enumerable.Range(ItchCatalog.FirstYear, DateTime.UtcNow.Year - ItchCatalog.FirstYear + 1).Reverse().ToArray(),
            minRatings = new[] { 1, 2, 4, 5, 10, 25, 50, 100 },
        },
        overview = new
        {
            catalogTotal = all.TotalItch ?? all.Denominator,
            known = games.Count,
            withYear = games.Count(g => g.Year is not null),
            zeroShare = all.Denominator > 0 ? (double)all.Bin0 / all.Denominator : (double?)null,
            median = Num(useMean ? all.Mean : all.Median),
            qualified = all.Qualified,
            p90 = Num(all.P90),
            successShare = Num(all.SuccessShare),
            successLow = Num(all.SuccessLow),
            successHigh = Num(all.SuccessHigh),
            stars = all.MedianStars,
            distribution,
            years,
            yearHasItch = yearPoints.Any(y => y.TotalItch is not null),
            genres = genreChart,
            niches,
            topGames,
            hotAvailable,
            insights,
        },
        games = new { total = matched.Count, shown, rows = gamePage },
        tops = new { total = demand.Count, rows = topsPage },
        genres = new { demand = genreChart, supply = genreSupply, rows = genreRows, cohorts = ru.Analyzer(filter).TrendCohorts },
        developer = new
        {
            insights,
            age = driversRu.AgeDoublingMultiplier,
            screenshots = driversRu.ScreenshotsDoublingMultiplier,
            sample = driversRu.Sample,
            drivers = topDrivers,
            map,
        },
        developers = devs,
        jams,
        collection,
    };
}

static object Developers(AnalyticsSnapshot snapshot, AnalyticsFilter filter)
{
    var min = filter.MinRatings;
    var byDev = snapshot.Games(filter).Where(g => g.DeveloperId is not null).GroupBy(g => g.DeveloperId!.Value).ToList();
    var rows = new List<(long Id, string Name, int? Followers, int? Delta, int Games, long Sum, double Median, string? Best, int? BestRatings, long? BestId)>();
    foreach (var grp in byDev)
    {
        var list = grp.ToList();
        snapshot.Data.Developers.TryGetValue(grp.Key, out var dev);
        var rated = Stats.QualifiedRatings(list, min);
        var name = dev?.Name ?? list.Select(g => g.AuthorName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? grp.Key.ToString();
        var best = list.Where(g => g.Ratings > 0).MaxBy(g => g.Ratings);
        rows.Add((grp.Key, name, dev?.Followers, dev?.FollowersDelta30, list.Count, list.Sum(g => (long)g.Ratings),
            rated.Length > 0 ? Stats.Median(rated) : double.NaN, best?.Title, best?.Ratings, best?.Id));
    }

    var followers = Stats.Sorted(rows.Where(r => r.Followers is not null).Select(r => (double)r.Followers!.Value));
    var totalRatings = rows.Sum(r => r.Sum);
    var top1 = rows.OrderByDescending(r => r.Sum).Take(Math.Max(1, rows.Count / 100)).Sum(r => r.Sum);
    (string Ru, string En, int From, int To)[] buckets =
    [
        ("1 игра", "1 game", 1, 1),
        ("2–4 игры", "2–4 games", 2, 4),
        ("5–9 игр", "5–9 games", 5, 9),
        ("10+ игр", "10+ games", 10, int.MaxValue),
    ];
    var experience = buckets.Select(b =>
    {
        var groups = byDev.Where(g => g.Count() >= b.From && g.Count() <= b.To).ToList();
        var games = groups.SelectMany(g => g).ToList();
        var rated = Stats.QualifiedRatings(games, min);
        var success = games.Count(g => g.Ratings >= snapshot.Params.SuccessThreshold);
        return new
        {
            label = Bi(b.Ru, b.En),
            developers = groups.Count,
            games = games.Count,
            median = rated.Length > 0 ? Stats.Median(rated) : (double?)null,
            success = games.Count > 0 ? (double)success / games.Count : (double?)null,
        };
    }).Where(b => b.developers > 0).ToList();

    var growing = rows.Where(r => r.Delta > 0).OrderByDescending(r => r.Delta).Take(10).Select(r => new
    {
        id = r.Id,
        name = r.Name,
        followers = r.Followers,
        delta = r.Delta,
    }).ToList();

    var page = rows.OrderByDescending(r => r.Followers ?? -1).Take(50).Select(r => new
    {
        id = r.Id,
        name = r.Name,
        followers = r.Followers,
        delta = r.Delta,
        games = r.Games,
        sum = r.Sum,
        median = double.IsNaN(r.Median) ? (double?)null : r.Median,
        best = r.Best,
        bestRatings = r.BestRatings,
        bestId = r.BestId,
    }).ToList();

    return new
    {
        total = rows.Count,
        withFollowers = followers.Length,
        singleShare = rows.Count > 0 ? (double)rows.Count(r => r.Games == 1) / rows.Count : (double?)null,
        top1Share = totalRatings > 0 ? (double)top1 / totalRatings : (double?)null,
        medianFollowers = followers.Length > 0 ? Stats.Median(followers) : (double?)null,
        experience,
        growing,
        rows = page,
    };
}

static object Jams(AnalyticsSnapshot ru, AnalyticsSnapshot en, AnalyticsFilter filter, AnalyticsParams parameters, ServiceProvider sp, DateTime now)
{
    var queries = sp.GetRequiredService<DataQueries>();
    var jams = queries.JamsAsync().GetAwaiter().GetResult();
    var counts = queries.JamEntryCountsAsync().GetAwaiter().GetResult();
    var rows = jams.Select(j =>
    {
        var state = StateOf(j, now);
        double? conv = j is { Joined: > 0, Submissions: { } s } ? Math.Min(1, (double)s / j.Joined.Value) : null;
        return new { j, entries = counts.GetValueOrDefault(j.Id), conv, state };
    }).OrderByDescending(r => r.j.EndAt ?? DateTime.MinValue).ToList();

    var submissions = Stats.Sorted(rows.Where(r => r.j.Submissions is > 0).Select(r => (double)r.j.Submissions!.Value));
    var conversions = Stats.Sorted(rows.Where(r => r.conv is not null).Select(r => r.conv!.Value));
    var games = ru.Games(filter).ToList();
    var jamGames = games.Where(g => g.IsJamEntry).ToList();
    var other = games.Where(g => !g.IsJamEntry).ToList();
    var jamRated = Stats.QualifiedRatings(jamGames, filter.MinRatings);
    var otherRated = Stats.QualifiedRatings(other, filter.MinRatings);
    double Share(List<GameRow> list) => list.Count > 0 ? (double)list.Count(g => g.Ratings >= parameters.SuccessThreshold) / list.Count : double.NaN;

    return new
    {
        total = rows.Count,
        withEntries = rows.Count(r => r.entries > 0),
        active = rows.Count(r => r.state is "active" or "voting"),
        upcoming = rows.Count(r => r.state == "upcoming"),
        medianSubmissions = submissions.Length > 0 ? Stats.Median(submissions) : (double?)null,
        medianConversion = conversions.Length > 0 ? Stats.Median(conversions) : (double?)null,
        jamGames = jamGames.Count,
        jamMedian = jamRated.Length > 0 ? Stats.Median(jamRated) : (double?)null,
        otherMedian = otherRated.Length > 0 ? Stats.Median(otherRated) : (double?)null,
        jamSuccess = Num(Share(jamGames)),
        otherSuccess = Num(Share(other)),
        rows = rows.Take(50).Select(r => new
        {
            id = r.j.Id,
            title = r.j.Title,
            hosts = r.j.Hosts,
            featured = r.j.IsFeatured,
            ranked = r.j.IsRanked,
            start = Date(r.j.StartAt),
            end = Date(r.j.EndAt),
            joined = r.j.Joined,
            submissions = r.j.Submissions,
            conversion = r.conv,
            entries = r.entries,
            state = r.state,
        }),
    };
}

static string StateOf(Jam j, DateTime now)
{
    if (j.StartAt is { } s && s > now) return "upcoming";
    if (j.EndAt is { } e && e > now) return "active";
    if (j.VotingEndAt is { } v && v > now) return "voting";
    if (j.EndAt is not null) return "ended";
    return "unknown";
}

static object Collection(ServiceProvider sp, AppSettingsModel settings, DateTime now)
{
    var coverage = sp.GetRequiredService<CoverageReport>().LoadAsync().GetAwaiter().GetResult();
    using var db = sp.GetRequiredService<IDbContextFactory<ItchDbContext>>().CreateDbContext();
    var last = db.CrawlRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstOrDefault();
    string[] regular = [nameof(CrawlProfile.QuickStart), nameof(CrawlProfile.Daily)];
    var lastSuccess = db.CrawlRuns.AsNoTracking()
        .Where(r => r.Status == CrawlRunStatus.Completed && regular.Contains(r.Profile))
        .OrderByDescending(r => r.FinishedAt)
        .Select(r => r.FinishedAt)
        .FirstOrDefault();
    DateTime? next = settings.AutoDailyEnabled && lastSuccess is { } ok ? ok.AddHours(Math.Max(1, settings.DailyIntervalHours)) : null;

    var cookies = 0;
    try { cookies = sp.GetRequiredService<CookieStore>().Count; } catch { /* count only; values stay out of the file */ }

    object Profile(CrawlProfile profile)
    {
        var requests = CrawlPlanner.EstimateRequests(profile, settings);
        var allPages = profile == CrawlProfile.GamePages && settings.GamePagesPerRun <= 0;
        var parallel = Math.Clamp(settings.ParallelRequests, 1, 8);
        var per = (Math.Max(0, settings.RequestIntervalMs) + settings.RequestJitterMs / 2.0 + 350) / 1000.0 / parallel;
        var time = TimeSpan.FromSeconds(requests * per);
        var left = Math.Max(0, coverage.Games - coverage.GamesWithDetails);
        return new
        {
            id = profile.ToString(),
            requests,
            allPages,
            left,
            duration = Bi(Duration(time, false), Duration(time, true)),
            recommended = coverage.Games == 0 ? profile == CrawlProfile.QuickStart
                : coverage.ListsComplete < 10 ? profile == CrawlProfile.Deep
                : profile == CrawlProfile.Daily,
        };
    }

    return new
    {
        idle = true,
        last = last is null ? null : new
        {
            status = last.Status.ToString(),
            ago = Bi(Ago(last.FinishedAt ?? last.StartedAt, false), Ago(last.FinishedAt ?? last.StartedAt, true)),
            message = Sanitize(last.Message),
            requests = last.Requests,
            games = last.GamesUpserted,
            errors = last.Errors,
            parseErrors = last.ParseErrors,
        },
        schedule = new
        {
            auto = settings.AutoDailyEnabled,
            hours = settings.DailyIntervalHours,
            lastSuccess = Bi(Ago(lastSuccess, false), Ago(lastSuccess, true)),
            next = next is null ? null : next <= now ? Bi("в ближайшую минуту", "within a minute") : Bi(Local(next), Local(next)),
        },
        cookies,
        userAgent = settings.UserAgent,
        pauses = string.Join(", ", settings.CloudflarePauseMinutes),
        stats = new
        {
            games = coverage.Games,
            nsfw = coverage.NsfwGames,
            rated = coverage.RatedGames,
            details = coverage.GamesWithDetails,
            catalog = coverage.CatalogTotal,
            counters = coverage.ListingCounters,
            developers = coverage.Developers,
            developersProfile = coverage.DevelopersWithProfile,
            tags = coverage.Tags,
            jams = coverage.Jams,
            jamsWithEntries = coverage.JamsWithEntries,
            jamEntries = coverage.JamEntries,
            snapshotDays = coverage.SnapshotDays,
            firstDay = coverage.FirstSnapshotDay?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            listsComplete = coverage.ListsComplete,
            listsTruncated = coverage.ListsTruncated,
            listsInProgress = coverage.ListsInProgress,
            megabytes = Math.Round(coverage.DatabaseBytes / 1048576.0, 1),
        },
        years = coverage.Years.Select(y => new
        {
            year = y.Year,
            total = y.TotalResults,
            known = y.KnownGames,
            rated = y.RatedGames,
            status = Bi(YearStatus(y.Status, false), YearStatus(y.Status, true)),
            done = y.Status == "полностью",
            fresh = y.Status != "не начат" && y.Status != "полностью",
        }),
        profiles = new[] { CrawlProfile.QuickStart, CrawlProfile.Daily, CrawlProfile.Deep, CrawlProfile.GamePages }.Select(Profile),
        settings = new
        {
            settings.RequestIntervalMs,
            settings.GamePageIntervalMs,
            settings.ParallelRequests,
            settings.RequestJitterMs,
            settings.UserAgent,
            pauses = string.Join(", ", settings.CloudflarePauseMinutes),
            settings.AutoDailyEnabled,
            settings.DailyIntervalHours,
            settings.SuccessThreshold,
            settings.MinSegmentSample,
            settings.ShrinkK,
            settings.BayesMinVotes,
            settings.WeightDemand,
            settings.WeightSuccess,
            settings.WeightCompetition,
            settings.WeightTrend,
            settings.HotA,
            settings.HotB,
            settings.HotG,
            settings.RaterShare,
            settings.RaterShareLow,
            settings.RaterShareHigh,
            settings.WebPlayShare,
            settings.DownloadShare,
            settings.QuickTopTags,
            settings.QuickPagesPerList,
            settings.QuickDetails,
            settings.JamEntriesPerRun,
            settings.DailyPopularTags,
            settings.DailyPopularPages,
            settings.DailyTopRatedPages,
            settings.DailyNewestMaxPages,
            settings.DailyDetails,
            settings.DailyProfiles,
            settings.DailyBacklogPages,
            settings.DeepListPages,
            settings.DeepMinTagProjects,
            settings.DeepRefreshDays,
            settings.DeepDetails,
            settings.DeepProfiles,
            settings.GamePagesPerRun,
        },
        quickTopTags = settings.QuickTopTags,
        quickDetails = settings.QuickDetails,
        dailyPopularTags = settings.DailyPopularTags,
        dailyDetails = settings.DailyDetails,
        dailyProfiles = settings.DailyProfiles,
        deepMinTagProjects = settings.DeepMinTagProjects,
        gamePageSeconds = (int)Math.Round(settings.GamePageIntervalMs / 1000.0),
        tabs = Math.Clamp(settings.ParallelRequests, 1, 8),
        listFrom = Math.Round(settings.RequestIntervalMs / 1000.0, 1),
        listTo = Math.Round((settings.RequestIntervalMs + settings.RequestJitterMs) / 1000.0, 1),
    };
}

static object Map(List<SegmentStats> tagsRu, Dictionary<string, SegmentStats> tagsEn, AnalyticsParams parameters, int smallGames, bool useMean)
{
    var source = tagsRu.Where(s => s.Qualified >= smallGames && s.Denominator > 0).Select(s => s.Clone()).ToList();
    SegmentAnalyzer.Rank(source, parameters, byMean: useMean, minQualified: smallGames);
    double Demand(SegmentStats s) => useMean ? s.AdjMean : s.Typical;
    var pool = source.Where(s => s.Opportunity is not null && Demand(s) > 0).ToList();
    var picked = new List<SegmentStats>();
    foreach (var q in new[] { Quadrant.BlueOcean, Quadrant.RedOcean, Quadrant.QuietNiche, Quadrant.Trap })
        picked.AddRange(pool.Where(s => s.Quadrant == q).OrderByDescending(s => s.Opportunity).Take(6));
    if (picked.Count < 24)
        picked.AddRange(pool.Except(picked).OrderByDescending(s => s.Opportunity).Take(24 - picked.Count));
    picked = picked.Take(24).ToList();

    const int W = 1000, H = 580, L = 64, R = 24, T = 16, B = 48;
    if (picked.Count == 0)
        return new { points = Array.Empty<object>(), xTicks = Array.Empty<object>(), yTicks = Array.Empty<object>(), quadrants = Array.Empty<object>() };

    var xs = picked.Select(s => Math.Log10(s.Denominator)).ToList();
    var ys = picked.Select(s => Math.Log10(Demand(s))).ToList();
    double x0 = Math.Floor(xs.Min() * 2) / 2, x1 = Math.Ceiling(xs.Max() * 2) / 2;
    double y0 = Math.Max(0, Math.Floor(ys.Min() * 4) / 4), y1 = Math.Ceiling(ys.Max() * 4) / 4;
    if (x1 - x0 < 0.5) x1 = x0 + 0.5;
    if (y1 - y0 < 0.25) y1 = y0 + 0.25;
    double Px(double v) => L + (v - x0) / (x1 - x0) * (W - L - R);
    double Py(double v) => H - B - (v - y0) / (y1 - y0) * (H - T - B);
    var splitX = Px(Median(xs));
    var splitY = Py(Median(ys));
    var maxShare = Math.Max(1e-9, picked.Max(s => s.SuccessShare));

    var points = picked.Select((s, i) =>
    {
        tagsEn.TryGetValue(s.Key, out var e);
        var name = s.Name;
        var nameEn = e?.Name ?? s.Name;
        var width = Math.Max(name.Length, nameEn.Length) * 6.6 + 4;
        return new Pt(s.Key, name, nameEn, Px(xs[i]), Py(ys[i]), 4 + 11 * Math.Sqrt(s.SuccessShare / maxShare), width, s);
    }).ToList();

    var plot = new Box(L, T, W - R, H - B);
    var obstacles = points.Select(p => new Box(p.X - p.Radius, p.Y - p.Radius, p.X + p.Radius, p.Y + p.Radius)).ToList();
    obstacles.Add(new Box(L + 6, T + 2, L + 140, T + 24));
    obstacles.Add(new Box(W - R - 140, T + 2, W - R - 6, T + 24));
    obstacles.Add(new Box(L + 6, H - B - 26, L + 120, H - B - 4));
    obstacles.Add(new Box(W - R - 90, H - B - 26, W - R - 6, H - B - 4));
    var labeled = 0;
    foreach (var p in points.OrderByDescending(p => p.Stats.Opportunity).ThenByDescending(p => p.Stats.Denominator))
    {
        if (labeled >= 28) break;
        var w = p.Width;
        (Box Box, double X, double Y, string Anchor)[] candidates =
        [
            (new Box(p.X + p.Radius + 2, p.Y - 8, p.X + p.Radius + 2 + w, p.Y + 7), p.X + p.Radius + 3, p.Y + 4, "start"),
            (new Box(p.X - p.Radius - 2 - w, p.Y - 8, p.X - p.Radius - 2, p.Y + 7), p.X - p.Radius - 3, p.Y + 4, "end"),
            (new Box(p.X - w / 2, p.Y - p.Radius - 16, p.X + w / 2, p.Y - p.Radius - 1), p.X, p.Y - p.Radius - 4, "middle"),
            (new Box(p.X - w / 2, p.Y + p.Radius + 1, p.X + w / 2, p.Y + p.Radius + 16), p.X, p.Y + p.Radius + 12, "middle"),
        ];
        foreach (var c in candidates)
        {
            if (c.Box.X1 < plot.X1 || c.Box.X2 > plot.X2 || c.Box.Y1 < plot.Y1 || c.Box.Y2 > plot.Y2) continue;
            if (obstacles.Any(o => c.Box.Hits(o))) continue;
            obstacles.Add(c.Box);
            p.Label = true;
            p.LabelX = c.X;
            p.LabelY = c.Y;
            p.Anchor = c.Anchor;
            labeled++;
            break;
        }
    }

    var xTicks = new List<object>();
    for (var e = (int)Math.Ceiling(x0); e <= Math.Floor(x1); e++)
        xTicks.Add(new { x = Math.Round(Px(e), 1), label = Short(Math.Pow(10, e)) });
    var yTicks = new List<object>();
    foreach (var v in new double[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 })
    {
        var lv = Math.Log10(v);
        if (lv >= y0 && lv <= y1) yTicks.Add(new { y = Math.Round(Py(lv), 1), label = Short(v) });
    }

    var quadrants = new[] { Quadrant.BlueOcean, Quadrant.RedOcean, Quadrant.QuietNiche, Quadrant.Trap }.Select(q => new
    {
        key = q.ToString(),
        name = Bi(SegmentAnalyzer.QuadrantName(q, UiLanguage.Ru), SegmentAnalyzer.QuadrantName(q, UiLanguage.En)),
        hint = Bi(SegmentAnalyzer.QuadrantHint(q, UiLanguage.Ru), SegmentAnalyzer.QuadrantHint(q, UiLanguage.En)),
        color = q switch { Quadrant.BlueOcean => "#5CC8FA", Quadrant.RedOcean => "#FF6B6B", Quadrant.QuietNiche => "#4CC38A", _ => "#F5B84A" },
        items = picked.Where(s => s.Quadrant == q).OrderByDescending(s => s.Opportunity).Take(8).Select(s =>
        {
            tagsEn.TryGetValue(s.Key, out var e);
            return new { key = s.Key, name = Bi(s.Name, e?.Name ?? s.Name), value = Math.Round(useMean ? s.Mean : s.Median, useMean ? 1 : 0), opportunity = s.Opportunity };
        }),
    });

    return new
    {
        splitX = Math.Round(splitX, 1),
        splitY = Math.Round(splitY, 1),
        xTicks,
        yTicks,
        points = points.OrderByDescending(p => p.Radius).Select(p => new
        {
            key = p.Key,
            name = Bi(p.Name, p.NameEn),
            x = Math.Round(p.X, 1),
            y = Math.Round(p.Y, 1),
            r = Math.Round(p.Radius, 1),
            color = p.Stats.Quadrant switch { Quadrant.BlueOcean => "#5CC8FA", Quadrant.RedOcean => "#FF6B6B", Quadrant.QuietNiche => "#4CC38A", _ => "#F5B84A" },
            label = p.Label,
            labelX = Math.Round(p.LabelX, 1),
            labelY = Math.Round(p.LabelY, 1),
            anchor = p.Anchor,
        }),
        quadrants,
    };
}

static object GameJson(GameRow g)
{
    var genre = ItchCatalog.GenreFromSlug(g.Genre);
    return new
    {
        id = g.Id,
        title = g.Title,
        author = g.AuthorName,
        cover = g.CoverUrl,
        ratings = g.Ratings,
        ratingsKnown = g.RatingsKnown,
        stars = g.RatingAvg,
        wr = g.WeightedRating > 0 ? Math.Round(g.WeightedRating, 2) : (double?)null,
        hot = g.Hot > 0 ? Math.Round(g.Hot, 2) : (double?)null,
        delta = g.DeltaRatings,
        comments = g.Comments,
        genre = Bi(genre?.RussianName ?? g.Genre ?? "—", genre?.EnglishName ?? g.Genre ?? "—"),
        released = g.PublishedAt is { } p ? Date(p) : g.Year?.ToString() ?? "—",
        platforms = PlatformsOf(g.Platforms),
        price = Bi(Price(g, false), Price(g, true)),
    };
}

static object Seg(SegmentStats s, SegmentStats? en, bool useMean) => new
{
    key = s.Key,
    name = Bi(s.Name, en?.Name ?? s.Name),
    denominator = s.Denominator,
    estimated = s.DenominatorEstimated,
    qualified = s.Qualified,
    central = Num(useMean ? s.Mean : s.Median),
    adjusted = Num(useMean ? s.AdjMean : s.AdjMedian),
    p75 = Num(s.P75),
    p90 = Num(s.P90),
    success = Num(s.SuccessShare),
    successLow = Num(s.SuccessLow),
    successHigh = Num(s.SuccessHigh),
    successes = s.Successes,
    trend = s.Trend,
    entry = s.EntryThreshold is null ? (double?)null : Math.Round(s.EntryThreshold.Value, 0),
    opportunity = s.Opportunity,
    quadrant = s.Quadrant.ToString(),
    small = s.SmallSample,
    median = Num(s.Median),
    mean = Num(s.Mean),
};

static object Bi(string ru, string en) => new { ru, en };

static double? Num(double v) => double.IsFinite(v) ? v : null;

static string? Sanitize(string? message)
{
    if (string.IsNullOrWhiteSpace(message)) return null;
    if (message.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase) || message.Contains("cookie", StringComparison.OrdinalIgnoreCase))
        return null;
    return message;
}

static string PlatformsOf(Platforms p)
{
    if (p == Platforms.None) return "—";
    var parts = new List<string>();
    if (p.HasFlag(Platforms.Web)) parts.Add("Web");
    if (p.HasFlag(Platforms.Windows)) parts.Add("Win");
    if (p.HasFlag(Platforms.MacOS)) parts.Add("Mac");
    if (p.HasFlag(Platforms.Linux)) parts.Add("Linux");
    if (p.HasFlag(Platforms.Android)) parts.Add("Android");
    if (p.HasFlag(Platforms.IOS)) parts.Add("iOS");
    return parts.Count == 0 ? "—" : string.Join(" · ", parts);
}

static string Price(GameRow g, bool english) => g.Price switch
{
    PriceModel.Free => english ? "free" : "бесплатно",
    PriceModel.NameYourPrice when g.PriceCents > 0 => (english ? "from $" : "от $") + (g.PriceCents.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture),
    PriceModel.NameYourPrice => english ? "name your price" : "своя цена",
    PriceModel.Paid when g.PriceCents is { } cents => "$" + (cents / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + (g.SalePercent > 0 ? $" (−{g.SalePercent}%)" : ""),
    _ => "—",
};

static string Date(DateTime? utc) => utc is null ? "—" : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

static string Local(DateTime? utc) => utc is null ? "—" : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

static string Ago(DateTime? utc, bool english)
{
    if (utc is null) return english ? "never" : "никогда";
    var d = DateTime.UtcNow - utc.Value;
    if (d < TimeSpan.Zero) return (english ? "in " : "через ") + Duration(-d, english);
    if (d.TotalMinutes < 1) return english ? "just now" : "только что";
    if (d.TotalHours < 1) return english ? $"{(int)d.TotalMinutes} min ago" : $"{(int)d.TotalMinutes} мин назад";
    if (d.TotalDays < 1) return english ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalHours} ч назад";
    if (d.TotalDays < 60)
    {
        var days = (int)d.TotalDays;
        return english ? $"{days} {(days == 1 ? "day" : "days")} ago" : $"{days} {Plural(days, "день", "дня", "дней")} назад";
    }
    return Date(utc);
}

static string Duration(TimeSpan t, bool english)
{
    if (t < TimeSpan.Zero) t = TimeSpan.Zero;
    if (english)
    {
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min";
        if (t.TotalHours < 48) return $"{(int)t.TotalHours} h {t.Minutes:00} min";
        var days = (int)t.TotalDays;
        return $"{days} {(days == 1 ? "day" : "days")}";
    }
    if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} с";
    if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} мин";
    if (t.TotalHours < 48) return $"{(int)t.TotalHours} ч {t.Minutes:00} мин";
    return $"{(int)t.TotalDays} {Plural((int)t.TotalDays, "день", "дня", "дней")}";
}

static string Plural(int n, string one, string few, string many)
{
    n = Math.Abs(n) % 100;
    if (n is >= 11 and <= 19) return many;
    return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
}

static string YearStatus(string status, bool english)
{
    if (!english) return status;
    if (status == "не начат") return "not started";
    if (status == "полностью") return "complete";
    if (status == "нужно дробление") return "needs splitting";
    if (status == "фильтр не поддерживается") return "filter not supported";
    if (status == "нет фильтра года — через жанры и теги") return "no year filter — via genres and tags";
    const string progress = "в процессе, стр. ";
    if (status.StartsWith(progress, StringComparison.Ordinal)) return "in progress, page " + status[progress.Length..];
    const string split = "дробится: ";
    const string splitEnd = " частей готово";
    if (status.StartsWith(split, StringComparison.Ordinal) && status.EndsWith(splitEnd, StringComparison.Ordinal))
        return "splitting: " + status[split.Length..^splitEnd.Length] + " parts done";
    return status;
}

static string Short(double x)
{
    var c = CultureInfo.GetCultureInfo("ru-RU");
    return Math.Abs(x) switch
    {
        >= 1_000_000 => (x / 1_000_000).ToString("0.#", c) + " млн",
        >= 10_000 => (x / 1_000).ToString("0", c) + " тыс.",
        >= 1_000 => (x / 1_000).ToString("0.#", c) + " тыс.",
        _ => x.ToString(x % 1 == 0 ? "0" : "0.#", c),
    };
}

static double Median(List<double> values)
{
    var sorted = values.Order().ToArray();
    return Stats.Median(sorted);
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "src")) && File.Exists(Path.Combine(dir.FullName, "README.md")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}

sealed class Pt(string key, string name, string nameEn, double x, double y, double radius, double width, SegmentStats stats)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string NameEn { get; } = nameEn;
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Radius { get; } = radius;
    public double Width { get; } = width;
    public SegmentStats Stats { get; } = stats;
    public bool Label { get; set; }
    public double LabelX { get; set; }
    public double LabelY { get; set; }
    public string Anchor { get; set; } = "start";
}

readonly record struct Box(double X1, double Y1, double X2, double Y2)
{
    public bool Hits(Box o) => X1 < o.X2 && o.X1 < X2 && Y1 < o.Y2 && o.Y1 < Y2;
}
