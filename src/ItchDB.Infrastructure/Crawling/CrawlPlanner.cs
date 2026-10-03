using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Crawling;

public sealed record PlannedJob(ICrawlJob Job, int Budget);

/// <summary>Turns a crawl profile into an ordered list of jobs with request budgets.</summary>
public static class CrawlPlanner
{
    private static readonly string[] FallbackTags =
    [
        "tag-2d", "tag-pixel-art", "tag-horror", "tag-singleplayer", "tag-3d", "tag-short", "tag-cute", "tag-atmospheric",
        "tag-retro", "tag-funny", "tag-story-rich", "tag-dating-sim", "tag-romance", "tag-fantasy", "tag-sci-fi",
        "tag-roguelike", "tag-point-click", "tag-exploration", "tag-psychological-horror", "tag-multiplayer",
        "tag-fangame", "tag-anime", "tag-comedy", "tag-mystery", "tag-female-protagonist", "tag-lgbt", "tag-cozy",
        "tag-dark", "tag-low-poly", "tag-magic",
    ];

    private static readonly string[] FallbackEngines =
    [
        "made-with-unity", "made-with-godot", "made-with-renpy", "made-with-gamemaker", "made-with-construct",
        "made-with-rpg-maker", "made-with-pico-8", "made-with-twine", "made-with-bitsy", "made-with-unreal-engine",
        "made-with-gdevelop", "made-with-love2d",
    ];

    private static readonly (string Path, int Pages)[] QuickJams = [("/jams/upcoming", 1), ("/jams/in-progress", 1), ("/jams/past", 1), ("/jams/past/sort-date", 1)];
    private static readonly (string Path, int Pages)[] DailyJams = [("/jams/upcoming", 2), ("/jams/in-progress", 2), ("/jams/past/sort-date", 3)];
    private static readonly (string Path, int Pages)[] DeepJams = [("/jams/upcoming", 3), ("/jams/in-progress", 3), ("/jams/past", 10), ("/jams/past/sort-date", 5)];

    public static IReadOnlyList<PlannedJob> Build(CrawlProfile profile, AppSettingsModel s) => profile switch
    {
        CrawlProfile.QuickStart => Quick(s),
        CrawlProfile.Daily => Daily(s),
        CrawlProfile.Tracking => [new(new TrackedGamesJob(), 400)],
        _ => Deep(s),
    };

    /// <summary>Rough request count of a profile for the UI (lists usually end earlier than their page limits).</summary>
    public static int EstimateRequests(CrawlProfile profile, AppSettingsModel s) => Build(profile, s).Sum(j => j.Budget);

    private static List<PlannedJob> Quick(AppSettingsModel s)
    {
        return
        [
            new(new TagCatalogJob(), 30),
            new(new ListsJob("Топ по оценкам: весь каталог, годы, жанры, крупные теги", async ctx =>
            {
                var tasks = new List<ListTask> { new(ListSpec.Of(ListSpec.TopRated), 15) };
                var years = await CrawlMeta.AvailableYearsAsync(ctx.Cursors, ctx.Ct);
                tasks.AddRange(years.OrderDescending().Take(2).Select(y => new ListTask(ListSpec.Of(ListSpec.TopRated, $"year-{y}"), 25)));
                tasks.AddRange(ItchCatalog.Genres.Select(g => new ListTask(ListSpec.Of(ListSpec.TopRated, g.Slug), 6)));
                tasks.AddRange((await TopTagsAsync(ctx, s.QuickTopTags, 0)).Select(t => new ListTask(ListSpec.Of(ListSpec.TopRated, t), s.QuickPagesPerList)));
                return tasks;
            }), 65 + ItchCatalog.Genres.Count * 6 + s.QuickTopTags * s.QuickPagesPerList),
            new(new ListsJob("Позиции в popular", async ctx =>
            {
                var tasks = new List<ListTask>
                {
                    new(ListSpec.Of(ListSpec.Popular), 2, StopAtUnrated: false, RecordPositions: true),
                    new(ListSpec.Of(ListSpec.NewAndPopular), 1, StopAtUnrated: false, RecordPositions: true),
                };
                tasks.AddRange((await TopTagsAsync(ctx, s.QuickTopTags, 0)).Select(t => new ListTask(ListSpec.Of(ListSpec.Popular, t), 1, StopAtUnrated: false, RecordPositions: true)));
                return tasks;
            }), 3 + s.QuickTopTags),
            new(new NewReleasesJob(3, stopAtKnown: false), 6),
            new(new FeaturedJob(2), 3),
            new(new GameDetailsJob(s.QuickDetails), s.QuickDetails),
            new(new DevelopersJob(30), 30),
            new(new JamsJob(QuickJams, 2), 8),
        ];
    }

    private static List<PlannedJob> Daily(AppSettingsModel s)
    {
        return
        [
            new(new TagCatalogJob(), 30),
            new(new ListsJob("Позиции в popular и new-and-popular", async ctx =>
            {
                var tasks = new List<ListTask>
                {
                    new(ListSpec.Of(ListSpec.Popular), 5, StopAtUnrated: false, RecordPositions: true),
                    new(ListSpec.Of(ListSpec.NewAndPopular), 3, StopAtUnrated: false, RecordPositions: true),
                };
                tasks.AddRange(ItchCatalog.Genres.Select(g => new ListTask(ListSpec.Of(ListSpec.Popular, g.Slug), 1, StopAtUnrated: false, RecordPositions: true)));
                tasks.AddRange((await TopTagsAsync(ctx, s.DailyPopularTags, 0)).Select(t =>
                    new ListTask(ListSpec.Of(ListSpec.Popular, t), s.DailyPopularPages, StopAtUnrated: false, RecordPositions: true)));
                return tasks;
            }), 8 + ItchCatalog.Genres.Count + s.DailyPopularTags * s.DailyPopularPages),
            new(new TrackedGamesJob(), 250),
            new(new ListsJob("Обновление оценок (top-rated)", async ctx =>
            {
                var tasks = new List<ListTask>
                {
                    new(ListSpec.Of(ListSpec.TopRated), 20),
                    new(ListSpec.Of(ListSpec.TopRated, "nsfw"), 3),
                };
                var years = await CrawlMeta.AvailableYearsAsync(ctx.Cursors, ctx.Ct);
                tasks.Add(new ListTask(ListSpec.Of(ListSpec.TopRated, $"year-{years.Max()}"), 10));
                // Probes for years itch.io may have added since: a redirect is remembered as unsupported for DeepRefreshDays.
                for (var y = years.Max() + 1; y <= DateTime.UtcNow.Year; y++)
                    tasks.Add(new ListTask(ListSpec.Of(ListSpec.TopRated, $"year-{y}"), 1));
                tasks.AddRange(ItchCatalog.Genres.Select(g => new ListTask(ListSpec.Of(ListSpec.TopRated, g.Slug), 3)));
                tasks.AddRange((await TopTagsAsync(ctx, 40, 0)).Select(t => new ListTask(ListSpec.Of(ListSpec.TopRated, t), s.DailyTopRatedPages)));
                return tasks;
            }), 33 + ItchCatalog.Genres.Count * 3 + 40 * s.DailyTopRatedPages),
            new(new NewReleasesJob(s.DailyNewestMaxPages, stopAtKnown: true), s.DailyNewestMaxPages * 2),
            new(new FeaturedJob(2), 3),
            new(new GameDetailsJob(s.DailyDetails), s.DailyDetails),
            new(new DevelopersJob(s.DailyProfiles), s.DailyProfiles),
            new(new JamsJob(DailyJams, s.JamEntriesPerRun), 7 + s.JamEntriesPerRun * 2),
            new(new ListsJob("Глубокий сбор: продолжение по курсорам", DeepBacklogAsync), s.DailyBacklogPages),
        ];
    }

    private static List<PlannedJob> Deep(AppSettingsModel s) =>
    [
        new(new TagCatalogJob(), 30),
        new(new NewReleasesJob(40, stopAtKnown: false), 80),
        new(new GameDetailsJob(Math.Max(s.DeepDetails, 8_000)), Math.Max(s.DeepDetails, 8_000)),
        new(new ListsJob("Полный каталог по сегментам (годы → цена → жанр, теги, движки, 18+)", DeepBacklogAsync), CatalogBudget(s)),
        new(new ListsJob("Порог входа: первая страница popular каждого тега", async ctx =>
        {
            var tasks = new List<ListTask> { new(ListSpec.Of(ListSpec.Popular), 5, StopAtUnrated: false, RecordPositions: true) };
            tasks.AddRange(ItchCatalog.Genres.Select(g => new ListTask(ListSpec.Of(ListSpec.Popular, g.Slug), 1, StopAtUnrated: false, RecordPositions: true)));
            tasks.AddRange((await TopTagsAsync(ctx, 600, s.DeepMinTagProjects)).Select(t => new ListTask(ListSpec.Of(ListSpec.Popular, t), 1, StopAtUnrated: false, RecordPositions: true)));
            tasks.AddRange((await EnginesAsync(ctx)).Select(t => new ListTask(ListSpec.Of(ListSpec.Popular, t), 1, StopAtUnrated: false, RecordPositions: true)));
            return tasks;
        }), 700),
        new(new FeaturedJob(10), 11),
        new(new GameDetailsJob(Math.Max(s.DeepDetails, 8_000)), Math.Max(s.DeepDetails, 8_000)),
        new(new DevelopersJob(s.DeepProfiles), s.DeepProfiles),
        new(new JamsJob(DeepJams, s.JamEntriesPerRun * 3), 21 + s.JamEntriesPerRun * 6),
    ];

    /// <summary>
    /// The full coverage plan. Year lists label the publication year and are complete once unrated games appear;
    /// genre lists cover all years, including years without a year filter; then big tags, durations, engines and NSFW.
    /// Truncated lists are split; finished lists are skipped for DeepRefreshDays.
    /// </summary>
    private static async Task<IReadOnlyList<ListTask>> DeepBacklogAsync(CrawlContext ctx)
    {
        var tasks = new List<ListTask>();
        ListTask Seg(params string[] filters) => new(ListSpec.Of(ListSpec.TopRated, filters), ItchCatalog.MaxPages, Split: true, UseCursor: true);

        var years = await CrawlMeta.AvailableYearsAsync(ctx.Cursors, ctx.Ct);
        tasks.AddRange(years.OrderDescending().Select(y => Seg($"year-{y}")));
        tasks.AddRange(ItchCatalog.Genres.Select(g => Seg(g.Slug)));
        tasks.AddRange((await TopTagsAsync(ctx, 600, ctx.Settings.DeepMinTagProjects)).Select(t => Seg(t)));
        tasks.AddRange(ItchCatalog.Durations.Select(d => Seg(d.Slug)));
        tasks.AddRange((await EnginesAsync(ctx)).Select(e => Seg(e)));
        tasks.Add(Seg("nsfw"));
        return tasks;
    }

    /// <summary>0 and the old default of 6000 mean "walk the lists until they are done", not a short cap.</summary>
    public static int CatalogBudget(AppSettingsModel s) => s.DeepListPages is 0 or 6000 ? 500_000 : s.DeepListPages;

    private static readonly List<string> ExcludedTagSlugs = [.. ItchCatalog.NsfwTags, .. ItchCatalog.Genres.Select(g => g.Slug)];

    private static async Task<List<string>> TopTagsAsync(CrawlContext ctx, int count, int minProjects)
    {
        await using var db = await ctx.Db.CreateDbContextAsync(ctx.Ct);
        var tags = await db.Tags.AsNoTracking()
            .Where(t => t.Kind == TagKind.Tag && t.ProjectsCount != null && t.ProjectsCount >= minProjects && !ExcludedTagSlugs.Contains(t.Slug))
            .OrderByDescending(t => t.ProjectsCount)
            .Select(t => t.Slug)
            .Take(count)
            .ToListAsync(ctx.Ct);
        return tags.Count > 0 ? tags : FallbackTags.Take(count).ToList();
    }

    private static async Task<List<string>> EnginesAsync(CrawlContext ctx)
    {
        await using var db = await ctx.Db.CreateDbContextAsync(ctx.Ct);
        var engines = await db.Tags.AsNoTracking()
            .Where(t => t.Kind == TagKind.Engine)
            .Select(t => new { t.Slug, Games = t.Games.Count })
            .OrderByDescending(t => t.Games)
            .Take(14)
            .ToListAsync(ctx.Ct);
        return engines.Where(e => e.Games >= 20).Select(e => e.Slug).Union(FallbackEngines).Take(16).ToList();
    }
}
