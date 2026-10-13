using System.Text.Encodings.Web;
using System.Text.Json;
using ItchDB.Core;
using ItchDB.Core.Analytics;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var root = RepoRoot();
var dataArg = args.FirstOrDefault(a => !a.StartsWith('-'));
var dataRoot = dataArg is not null ? Path.GetFullPath(dataArg) : Path.Combine(root, "src", "ItchDB.Web", "data");
var outFile = Path.Combine(root, "preview", "js", "sample.js");
Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

if (!File.Exists(Path.Combine(dataRoot, "itchdb.db")))
{
    WriteEmpty(outFile);
    Console.WriteLine("No database at " + dataRoot + ". Wrote an empty preview sample.");
    return;
}

try
{
    var services = new ServiceCollection();
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddItchInfrastructure(dataRoot);
    await using var sp = services.BuildServiceProvider();
    await DatabaseInitializer.InitializeAsync(sp);
    var loader = sp.GetRequiredService<DatasetLoader>();
    var data = await loader.LoadAsync(new AnalyticsParams());
    var snapshot = new AnalyticsSnapshot(data, new AnalyticsParams(), DateTime.UtcNow, UiLanguage.Ru);
    var filter = AnalyticsFilter.Default;
    var payload = data.IsEmpty ? EmptyPayload() : Build(snapshot, filter, sp);
    Write(outFile, payload);
    Console.WriteLine("Wrote " + outFile);
}
catch (Exception ex)
{
    WriteEmpty(outFile);
    Console.Error.WriteLine("Preview export failed, wrote an empty sample: " + ex.Message);
}

static object Build(AnalyticsSnapshot snapshot, AnalyticsFilter filter, ServiceProvider sp)
{
    var all = snapshot.All(filter);
    var genres = snapshot.Genres(filter);
    var tags = snapshot.Tags(filter);
    var engines = snapshot.Engines(filter);
    var formats = snapshot.Formats(filter);
    var languages = snapshot.Languages(filter);
    var games = snapshot.Games(filter).Where(g => g.RatingsKnown).ToList();
    var ordinaryGames = OrdinaryGames(games);

    var ruInsights = InsightGenerator.Build(Input(snapshot, filter, UiLanguage.Ru));
    var enInsights = InsightGenerator.Build(Input(snapshot, filter, UiLanguage.En));
    var insights = ruInsights.Zip(enInsights).Select(p => new { titleRu = p.First.Title, titleEn = p.Second.Title, tone = Tone(p.First.Tone) }).ToList();

    var midGenres = Middle(genres.Where(s => !IsLeader(s, genres) && s.Qualified > 0).OrderBy(s => s.Median), 8);
    var midTags = Middle(tags.Where(s => !IsLeader(s, tags) && s.Qualified > 0).OrderBy(s => s.Median), 8);

    var yearRows = snapshot.Years(filter).Where(y => y.Year >= 2015).OrderBy(y => y.Year)
        .Select(y => new { year = y.Year, games = y.Known, avg = Math.Round(y.Mean, 1) }).ToList();

    var known = games;
    (string Label, int From, int To)[] bins =
        [("1–4", 1, 4), ("5–9", 5, 9), ("10–24", 10, 24), ("25–49", 25, 49), ("50–99", 50, 99), ("100–249", 100, 249), ("250–999", 250, 999), ("1000+", 1000, int.MaxValue)];
    var distribution = new List<object> { new { label = "0", value = all.Bin0 } };
    distribution.AddRange(bins.Select(b => new { label = b.Label, value = known.Count(g => g.Ratings >= b.From && g.Ratings <= b.To) }));

    var mapPool = tags.Concat(genres).Where(s => s.Quadrant != Quadrant.BlueOcean && !IsLeader(s, tags.Concat(genres).ToList()) && s.Qualified > 0).ToList();
    var map = Middle(mapPool.OrderBy(s => s.Median), 12).Select(s =>
    {
        var (ru, en) = Names(s);
        return new { nameRu = ru, nameEn = en, median = Round(s.Median), games = s.Denominator, quadrant = Quad(s.Quadrant) };
    }).ToList();

    var combos = TagCombos.Compute(snapshot.Data, filter, snapshot.Params)
        .Where(c => c.Lift < 1.3)
        .OrderBy(c => c.Lift)
        .ToList();
    var midCombos = Middle(combos, 6).Select(c => new
    {
        aRu = Segments.DisplayName(c.A, UiLanguage.Ru),
        aEn = Segments.DisplayName(c.A, UiLanguage.En),
        bRu = Segments.DisplayName(c.B, UiLanguage.Ru),
        bEn = Segments.DisplayName(c.B, UiLanguage.En),
        median = Round(c.Median),
    }).ToList();

    var drivers = snapshot.Drivers(filter).Results
        .Where(r => r.Multiplier is null or <= 1)
        .ToList();
    var driversEn = Drivers.Analyze(snapshot.Data, filter, snapshot.Params, snapshot.Now, language: UiLanguage.En).Results
        .ToDictionary(r => r.Feature.Key);
    var midDrivers = Middle(drivers, 6).Select(r =>
    {
        driversEn.TryGetValue(r.Feature.Key, out var en);
        return new { nameRu = r.Name, nameEn = en?.Name ?? r.Name, groupRu = r.Group, groupEn = en?.Group ?? r.Group };
    }).ToList();

    var devRows = DeveloperRows(snapshot);
    var jamBlock = JamBlock(sp);

    return new
    {
        empty = false,
        builtAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        overview = new
        {
            catalogTotal = all.TotalItch ?? all.Denominator,
            known = snapshot.Data.Games.Count,
            zeroShare = all.Denominator > 0 ? (double)all.Bin0 / all.Denominator : (double?)null,
            median = Round(all.Median),
            qualified = all.Qualified,
            minRatings = filter.MinRatings,
            p90 = Round(all.P90),
            successShare = all.SuccessShare,
            successLow = all.SuccessLow,
            successHigh = all.SuccessHigh,
            successThreshold = snapshot.Params.SuccessThreshold,
            stars = all.MedianStars,
            distribution,
            years = yearRows,
            genres = midGenres.Select(SegmentJson),
            niches = midTags.Select(SegmentJson),
            games = ordinaryGames.Take(6).Select(GameJson),
            insights,
        },
        games = ordinaryGames.Select(GameJson),
        tops = new
        {
            demand = ordinaryGames.Select(GameJson),
            reviews = OrdinaryBy(games, g => g.WeightedRating).Select(GameJson),
            hot = Array.Empty<object>(),
            rising = Array.Empty<object>(),
            featured = OrdinaryBy(games.Where(g => g.IsFeatured), g => g.FeaturedAt?.Ticks ?? 0).Select(GameJson),
            comments = OrdinaryBy(games.Where(g => g.Comments is not null), g => g.Comments ?? 0).Select(GameJson),
        },
        segments = new
        {
            genres = midGenres.Select(SegmentJson),
            tags = midTags.Select(SegmentJson),
            engines = Middle(engines.Where(s => !IsLeader(s, engines)).OrderBy(s => s.Median), 8).Select(SegmentJson),
            formats = Middle(formats.Where(s => !IsLeader(s, formats)).OrderBy(s => s.Median), 8).Select(SegmentJson),
            languages = Middle(languages.OrderBy(s => s.Name), 8).Select(SegmentJson),
        },
        developer = new
        {
            insights,
            map,
            niches = midTags.Select(SegmentJson),
            formatGroups = formats.GroupBy(s => s.Segment.Kind).Select(g => new
            {
                titleRu = CatalogNames.SegmentKindName(g.Key, UiLanguage.Ru),
                titleEn = CatalogNames.SegmentKindName(g.Key, UiLanguage.En),
                items = Middle(g.Where(s => !IsLeader(s, formats)).OrderBy(s => s.Median), 4).Select(SegmentJson),
            }).Where(g => g.items.Any()),
            combos = midCombos,
            drivers = midDrivers,
        },
        developers = devRows,
        jams = jamBlock,
    };
}

static InsightInput Input(AnalyticsSnapshot snapshot, AnalyticsFilter filter, UiLanguage language) => new()
{
    Data = snapshot.Data,
    Filter = filter,
    Params = snapshot.Params,
    All = snapshot.All(filter),
    Genres = snapshot.Genres(filter),
    Tags = snapshot.Tags(filter),
    Formats = snapshot.Formats(filter),
    Drivers = snapshot.Drivers(filter),
    Combos = snapshot.Combos(filter),
    TrendCohorts = snapshot.Analyzer(filter).TrendCohorts,
    Language = language,
};

static bool IsLeader(SegmentStats s, IReadOnlyList<SegmentStats> peers)
{
    if (s.Quadrant == Quadrant.BlueOcean) return true;
    var ranked = peers.Where(x => x.Opportunity is not null).OrderByDescending(x => x.Opportunity!.Value).ToList();
    if (ranked.Count == 0) return false;
    var cut = Math.Max(1, (int)Math.Ceiling(ranked.Count / 4.0));
    return ranked.Take(cut).Any(x => x.Key == s.Key);
}

static List<GameRow> OrdinaryGames(List<GameRow> games) => OrdinaryBy(games, g => g.Ratings);

static List<GameRow> OrdinaryBy(IEnumerable<GameRow> games, Func<GameRow, double> score)
{
    var ranked = games.Where(g => g.Hot <= 0).OrderByDescending(score).ToList();
    if (ranked.Count == 0) return [];
    var skip = Math.Max(1, ranked.Count / 10);
    return Middle(ranked.Skip(skip), 8);
}

static List<T> Middle<T>(IEnumerable<T> items, int n)
{
    var list = items.ToList();
    if (list.Count <= n) return list;
    var start = (list.Count - n) / 2;
    return list.Skip(start).Take(n).ToList();
}

static object SegmentJson(SegmentStats s)
{
    var (ru, en) = Names(s);
    return new { key = s.Key, nameRu = ru, nameEn = en, median = Round(s.Median), chance = s.SuccessShare, games = s.Denominator };
}

static (string Ru, string En) Names(SegmentStats s)
{
    if (ItchCatalog.GenreFromSlug(s.Key) is { } genre) return (genre.RussianName, genre.EnglishName);
    if (s.Key.StartsWith("platform-", StringComparison.Ordinal))
        return (CatalogNames.Platform(s.Key, UiLanguage.Ru), CatalogNames.Platform(s.Key, UiLanguage.En));
    if (s.Key.StartsWith("duration-", StringComparison.Ordinal))
        return (CatalogNames.Duration(s.Key, UiLanguage.Ru), CatalogNames.Duration(s.Key, UiLanguage.En));
    if (s.Key.StartsWith("price-", StringComparison.Ordinal) || s.Key is "ai-none" or "ai-used" or "jam-entry")
        return (CatalogNames.PriceKey(s.Key, UiLanguage.Ru), CatalogNames.PriceKey(s.Key, UiLanguage.En));
    return (s.Name, s.Name);
}

static object GameJson(GameRow g)
{
    var genre = ItchCatalog.GenreFromSlug(g.Genre);
    return new
    {
        id = g.Id,
        title = g.Title,
        ratings = g.Ratings,
        stars = g.RatingAvg,
        genreRu = genre?.RussianName ?? g.Genre,
        genreEn = genre?.EnglishName ?? g.Genre,
        year = g.Year,
        priceRu = Price(g, UiLanguage.Ru),
        priceEn = Price(g, UiLanguage.En),
        platforms = PlatformsOf(g.Platforms),
        url = g.Url,
        author = g.AuthorName,
        authorId = g.DeveloperId,
    };
}

static string Price(GameRow g, UiLanguage language) => g.Price switch
{
    PriceModel.Free => CatalogNames.Pick(language, "бесплатно", "free"),
    PriceModel.NameYourPrice => CatalogNames.Pick(language, "своя цена", "name your price"),
    PriceModel.Paid when g.PriceCents is { } cents => "$" + (cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
    _ => "—",
};

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
    return string.Join(" · ", parts);
}

static object DeveloperRows(AnalyticsSnapshot snapshot)
{
    var rows = snapshot.Data.Developers.Values.Select(d =>
    {
        var games = snapshot.Data.Games.Where(g => g.DeveloperId == d.Id).ToList();
        var rated = games.Where(g => g.Ratings > 0).Select(g => (double)g.Ratings).OrderBy(x => x).ToArray();
        return new { d.Id, Name = d.Name, Followers = d.Followers ?? 0, Games = games.Count, Median = rated.Length == 0 ? 0 : rated[rated.Length / 2], Sum = games.Sum(g => g.Ratings) };
    }).Where(r => r.Games > 0).OrderByDescending(r => r.Sum).ToList();
    var skip = rows.Count == 0 ? 0 : Math.Max(1, rows.Count / 10);
    var mid = Middle(rows.Skip(skip), 8);
    var withFollowers = rows.Count(r => r.Followers > 0);
    var single = rows.Count == 0 ? 0 : (double)rows.Count(r => r.Games == 1) / rows.Count;
    var totalSum = rows.Sum(r => (double)r.Sum);
    var topCount = Math.Max(1, rows.Count / 100);
    var topShare = totalSum <= 0 ? 0 : rows.Take(topCount).Sum(r => (double)r.Sum) / totalSum;
    var followerValues = rows.Where(r => r.Followers > 0).Select(r => (double)r.Followers).OrderBy(x => x).ToArray();
    return new
    {
        kpis = new
        {
            count = rows.Count,
            withFollowers,
            singleShare = single,
            top1Share = topShare,
            medianFollowers = followerValues.Length == 0 ? 0 : followerValues[followerValues.Length / 2],
        },
        rows = mid.Select(r => new { id = r.Id, name = r.Name, followers = r.Followers, games = r.Games, median = r.Median, sum = r.Sum }),
    };
}

static object JamBlock(ServiceProvider sp)
{
    using var db = sp.GetRequiredService<IDbContextFactory<ItchDbContext>>().CreateDbContext();
    var now = DateTime.UtcNow;
    var jams = db.Jams.AsNoTracking().Where(j => j.Submissions != null).OrderByDescending(j => j.Submissions).ToList();
    var skip = jams.Count == 0 ? 0 : Math.Max(1, jams.Count / 10);
    var mid = Middle(jams.Skip(skip).OrderBy(j => j.Submissions), 8);
    double Conv(Jam j) => j.Joined is > 0 && j.Submissions is not null ? (double)j.Submissions.Value / j.Joined.Value : double.NaN;
    var conv = jams.Select(Conv).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
    return new
    {
        kpis = new
        {
            count = jams.Count,
            active = jams.Count(j => j.StartAt <= now && j.EndAt >= now),
            medianSubmissions = jams.Count == 0 ? 0 : jams.OrderBy(j => j.Submissions).ElementAt(jams.Count / 2).Submissions ?? 0,
            conversion = conv.Length == 0 ? 0 : conv[conv.Length / 2],
        },
        rows = mid.Select(j => new
        {
            id = j.Id,
            title = j.Title,
            submissions = j.Submissions ?? 0,
            joined = j.Joined ?? 0,
            conversion = double.IsNaN(Conv(j)) ? (double?)null : Conv(j),
        }),
    };
}

static string Tone(InsightTone tone) => tone switch
{
    InsightTone.Good => "good",
    InsightTone.Warn => "warn",
    InsightTone.Bad => "bad",
    _ => "info",
};

static string Quad(Quadrant q) => q switch
{
    Quadrant.RedOcean => "red",
    Quadrant.QuietNiche => "quiet",
    Quadrant.Trap => "trap",
    _ => "quiet",
};

static double Round(double v) => double.IsNaN(v) ? 0 : Math.Round(v, 1);

static object EmptyPayload() => new
{
    empty = true,
    builtAt = (string?)null,
    overview = (object?)null,
    games = Array.Empty<object>(),
    tops = new { demand = Array.Empty<object>(), reviews = Array.Empty<object>(), hot = Array.Empty<object>(), rising = Array.Empty<object>(), featured = Array.Empty<object>(), comments = Array.Empty<object>() },
    segments = new { genres = Array.Empty<object>(), tags = Array.Empty<object>(), engines = Array.Empty<object>(), formats = Array.Empty<object>(), languages = Array.Empty<object>() },
    developer = new { insights = Array.Empty<object>(), map = Array.Empty<object>(), niches = Array.Empty<object>(), formatGroups = Array.Empty<object>(), combos = Array.Empty<object>(), drivers = Array.Empty<object>() },
    developers = new { kpis = (object?)null, rows = Array.Empty<object>() },
    jams = new { kpis = (object?)null, rows = Array.Empty<object>() },
};

static void WriteEmpty(string path) => Write(path, EmptyPayload());

static void Write(string path, object payload)
{
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }).Replace("<", "\\u003c", StringComparison.Ordinal);
    File.WriteAllText(path, "window.ITCH_SAMPLE = " + json + ";\n");
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "preview")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}
