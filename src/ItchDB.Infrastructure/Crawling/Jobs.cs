using System.Text.Json;
using System.Xml;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Parsing;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Crawling;

public sealed class TagCatalogJob : ICrawlJob
{
    public string Title => "Каталог тегов (/tags)";

    public async Task RunAsync(CrawlContext ctx)
    {
        int page = 1, total = 1;
        do
        {
            if (!ctx.HasBudget) break;
            ctx.Step($"/tags — стр. {page} из {total}", page - 1, total);
            var r = await ctx.FetchAsync($"{ItchCatalog.BaseUrl}/tags?page={page}");
            if (r is null || !r.IsSuccess) break;
            var parsed = TagsPageParser.Parse(r.Body);
            if (parsed.Entries.Count == 0)
            {
                ctx.ParseError($"/tags?page={page}: не найдено ни одного тега");
                break;
            }
            total = parsed.TotalPages;
            await ctx.Ingest.IngestTagCatalogAsync(parsed, ctx.Today, ctx.Ct);
            page++;
        } while (page <= total && page <= 60);
        ctx.Info($"Каталог тегов обновлён: {page - 1} стр.");
    }
}

/// <summary>Crawls a planned set of browse lists (top-rated segments, popular positions, deep backlog).</summary>
public sealed class ListsJob(string title, Func<CrawlContext, Task<IReadOnlyList<ListTask>>> planner) : ICrawlJob
{
    public string Title => title;

    public async Task RunAsync(CrawlContext ctx)
    {
        var tasks = await planner(ctx);
        int done = 0, complete = 0, truncated = 0, skipped = 0;
        foreach (var task in tasks)
        {
            if (!ctx.HasBudget)
            {
                ctx.Info($"{title}: бюджет запросов исчерпан, осталось {tasks.Count - done} списков — продолжим в следующий раз");
                break;
            }
            var outcome = await ListCrawler.CrawlAsync(ctx, task);
            done++;
            if (outcome.Skipped) skipped++;
            else if (outcome.Truncated) truncated++;
            else if (outcome.ReachedEnd) complete++;
        }
        ctx.Info($"{title}: списков {done}/{tasks.Count}, полностью {complete}, дробились {truncated}, пропущено как свежие {skipped}");
    }
}

public sealed class NewReleasesJob(int maxPages, bool stopAtKnown) : ICrawlJob
{
    public string Title => "Новые релизы (newest + RSS с датами)";

    public async Task RunAsync(CrawlContext ctx)
    {
        var spec = ListSpec.Of(ListSpec.Newest);
        int page = 1, inserted = 0, dated = 0;
        for (; page <= maxPages && ctx.HasBudget; page++)
        {
            ctx.Step($"newest — стр. {page}", page, maxPages);
            var r = await ctx.FetchAsync(spec.Url(page, json: true));
            if (r is null || !r.IsSuccess) break;
            ListingPage lp;
            try
            {
                lp = ListingParser.ParseRequested(r.Body, json: true);
            }
            catch (JsonException ex)
            {
                ctx.ParseError($"newest p{page}: {ex.Message}");
                break;
            }
            var res = await ctx.Ingest.IngestCardsAsync(lp.Cards, CardContext.Plain, ctx.Ct);
            ctx.CountGames(res);
            inserted += res.Inserted;

            if (ctx.HasBudget)
            {
                var rss = await ctx.FetchAsync(spec.RssUrl(page), "application/rss+xml,application/xml;q=0.9,*/*;q=0.8");
                if (rss is { IsSuccess: true })
                {
                    try
                    {
                        dated += await ctx.Ingest.ApplyRssAsync(RssParser.Parse(rss.Body), ctx.Ct);
                    }
                    catch (XmlException ex)
                    {
                        ctx.ParseError($"newest.xml p{page}: {ex.Message}");
                    }
                }
            }
            if (lp.Cards.Count < ItchCatalog.PageSize) break;
            if (stopAtKnown && page >= 2 && res.Inserted == 0) break;
        }
        ctx.Info($"Новые релизы: новых игр {inserted}, дат из RSS {dated}");
    }
}

public sealed class FeaturedJob(int pages) : ICrawlJob
{
    public string Title => "Featured";

    public async Task RunAsync(CrawlContext ctx)
    {
        await ListCrawler.CrawlAsync(ctx, new ListTask(ListSpec.Of(ListSpec.Featured), pages, StopAtUnrated: false));
        if (!ctx.HasBudget) return;
        var r = await ctx.FetchAsync($"{ItchCatalog.BaseUrl}/games/featured.xml", "application/rss+xml,application/xml;q=0.9,*/*;q=0.8");
        if (r is not { IsSuccess: true }) return;
        try
        {
            var added = await ctx.Ingest.IngestFeaturedAsync(RssParser.Parse(r.Body), ctx.Ct);
            ctx.Info($"Featured: новых записей {added}");
        }
        catch (XmlException ex)
        {
            ctx.ParseError($"featured.xml: {ex.Message}");
        }
    }
}

/// <summary>Fetches game pages in priority order: tracked, fresh releases, most rated without details, stale.</summary>
public sealed class GameDetailsJob(int limit) : ICrawlJob
{
    public string Title => "Страницы игр (теги, даты, движок, комментарии)";

    public async Task RunAsync(CrawlContext ctx)
    {
        var queue = await SelectAsync(ctx, Math.Min(limit, ctx.RemainingBudget));
        int ok = 0, failed = 0;
        try
        {
            for (var i = 0; i < queue.Count && ctx.HasBudget; i++)
            {
                var item = queue[i];
                ctx.Step($"Игра {i + 1} из {queue.Count}: {item.Url}", i + 1, queue.Count);
                var fetched = await ctx.FetchAsync(item.Url, gamePage: true);
                if (await SaveOneAsync(ctx, item.Id, item.Url, fetched)) ok++;
                else failed++;
            }
        }
        finally
        {
            ctx.Info($"Страницы игр: обработано {ok}, ошибок {failed}");
        }
    }

    /// <returns>true when the page was stored.</returns>
    private static async Task<bool> SaveOneAsync(CrawlContext ctx, long id, string url, FetchResult? r)
    {
        if (r is null) return false;
        if (r.IsNotFound)
        {
            await ctx.Ingest.MarkDetailsFailedAsync(id, removed: true, ctx.Ct);
            return false;
        }
        if (!r.IsSuccess)
        {
            if (r.StatusCode is not (400 or 401 or 408 or 429))
                await ctx.Ingest.MarkDetailsFailedAsync(id, removed: false, ctx.Ct);
            return false;
        }
        var data = GamePageParser.Parse(r.Body);
        if (data.Id is null)
        {
            if (!LooksLikeGamePage(r.Body))
            {
                ctx.Warn($"{url}: ответ не похож на страницу игры, повторим позже");
                return false;
            }
            ctx.ParseError($"{url}: не найден id игры");
            await ctx.Ingest.MarkDetailsFailedAsync(id, removed: false, ctx.Ct);
            return false;
        }
        await ctx.Ingest.IngestGameDetailsAsync(data, url, ctx.Ct);
        return true;
    }

    /// <summary>
    /// An embed widget (itch.io/embed/…) also contains game_title, but it is not the game page.
    /// Treating it as one counted a parse failure and, after three of them, stopped retrying the title.
    /// </summary>
    private static bool LooksLikeGamePage(string body) =>
        body.Contains("name=\"itch:path\"", StringComparison.Ordinal)
        || body.Contains("init_ViewGame(", StringComparison.Ordinal)
        || body.Contains("init_ViewHtmlGame(", StringComparison.Ordinal)
        || body.Contains("name=\"game_id\"", StringComparison.Ordinal)
        || body.Contains("I.current_game", StringComparison.Ordinal);

    private static async Task<List<(long Id, string Url)>> SelectAsync(CrawlContext ctx, int limit)
    {
        if (limit <= 0) return [];
        await using var db = await ctx.Db.CreateDbContextAsync(ctx.Ct);
        var stale = DateTime.UtcNow.AddDays(-30);
        var q = db.Games.AsNoTracking().Where(g => !g.IsRemoved && g.DetailsFailures < 3 && g.Url != "");

        var tracked = await db.TrackedGames.AsNoTracking().Where(t => t.GameId != null).Select(t => t.GameId!.Value).ToListAsync(ctx.Ct);
        var result = new List<(long, string)>();
        var seen = new HashSet<long>();
        void Add(IEnumerable<(long Id, string Url)> items, int cap)
        {
            foreach (var it in items)
            {
                if (result.Count >= limit || cap-- <= 0) return;
                if (seen.Add(it.Id)) result.Add(it);
            }
        }

        var trackedRows = await q.Where(g => tracked.Contains(g.Id)).Select(g => new { g.Id, g.Url }).ToListAsync(ctx.Ct);
        Add(trackedRows.Select(x => (x.Id, x.Url)), limit);

        // Most-rated games first, so every genre's popular titles get a page before the long tail.
        var never = await q.Where(g => g.DetailsFetchedAt == null && g.RatingCount > 0)
            .OrderByDescending(g => g.RatingCount).Select(g => new { g.Id, g.Url }).Take(limit).ToListAsync(ctx.Ct);
        Add(never.Select(x => (x.Id, x.Url)), limit);

        // 2025 and 2026 have no year filter: a page is the only way to learn the year. Take a slice of them
        // after the current popularity head, not instead of it.
        var undated = await q.Where(g => g.DetailsFetchedAt == null && g.PublishedYear == null)
            .OrderByDescending(g => g.RatingCount).ThenByDescending(g => g.FirstSeenAt)
            .Select(g => new { g.Id, g.Url }).Take(limit).ToListAsync(ctx.Ct);
        Add(undated.Select(x => (x.Id, x.Url)), Math.Max(1, limit / 5));

        var old = await q.Where(g => g.DetailsFetchedAt < stale && g.RatingCount > 0)
            .OrderByDescending(g => g.RatingCount).Select(g => new { g.Id, g.Url }).Take(limit).ToListAsync(ctx.Ct);
        Add(old.Select(x => (x.Id, x.Url)), limit);
        return result;
    }
}

public sealed class DevelopersJob(int limit) : ICrawlJob
{
    public string Title => "Профили разработчиков (подписчики)";

    public async Task RunAsync(CrawlContext ctx)
    {
        List<(long Id, string Username)> queue;
        await using (var db = await ctx.Db.CreateDbContextAsync(ctx.Ct))
        {
            var ranked = await db.Games.AsNoTracking()
                .Where(g => g.DeveloperId != null && g.RatingCount > 0)
                .GroupBy(g => g.DeveloperId!.Value)
                .Select(x => new { Id = x.Key, Ratings = x.Sum(g => g.RatingCount ?? 0) })
                .OrderByDescending(x => x.Ratings)
                .Take(Math.Max(limit * 5, 200))
                .ToListAsync(ctx.Ct);
            var ids = ranked.Select(r => r.Id).ToList();
            var cut = DateTime.UtcNow.AddDays(-14);
            var due = await db.Developers.AsNoTracking()
                .Where(d => ids.Contains(d.Id) && d.Username != null && d.ProfileFailures < 3 && (d.ProfileFetchedAt == null || d.ProfileFetchedAt < cut))
                .Select(d => new { d.Id, d.Username })
                .ToDictionaryAsync(d => d.Id, d => d.Username!, ctx.Ct);
            queue = ranked.Where(r => due.ContainsKey(r.Id)).Take(limit).Select(r => (r.Id, due[r.Id])).ToList();
        }

        var ok = 0;
        for (var i = 0; i < queue.Count && ctx.HasBudget; i++)
        {
            var (id, username) = queue[i];
            ctx.Step($"Профиль {i + 1} из {queue.Count}: {username}", i + 1, queue.Count);
            var r = await ctx.FetchAsync($"{ItchCatalog.BaseUrl}/profile/{username}");
            if (r is null) continue;
            if (!r.IsSuccess)
            {
                await ctx.Ingest.MarkProfileFailedAsync(id, ctx.Ct);
                continue;
            }
            var profile = ProfileParser.Parse(r.Body);
            if (profile.Followers is null) ctx.ParseError($"/profile/{username}: не найдено число подписчиков");
            await ctx.Ingest.IngestProfileAsync(id, profile, ctx.Ct);
            ok++;
        }
        ctx.Info($"Профили разработчиков: обновлено {ok}");
    }
}

public sealed class JamsJob(IReadOnlyList<(string Path, int Pages)> lists, int entriesJams) : ICrawlJob
{
    public string Title => "Джемы";

    public async Task RunAsync(CrawlContext ctx)
    {
        var seen = 0;
        foreach (var (path, pages) in lists)
        {
            for (var page = 1; page <= pages && ctx.HasBudget; page++)
            {
                ctx.Step($"{path} — стр. {page}", page, pages);
                var r = await ctx.FetchAsync($"{ItchCatalog.BaseUrl}{path}{(page > 1 ? $"?page={page}" : "")}");
                if (r is null || !r.IsSuccess) break;
                var parsed = JamParsers.ParseList(r.Body);
                if (parsed.Jams.Count == 0)
                {
                    if (page == 1) ctx.ParseError($"{path}: не найдено ни одного джема");
                    break;
                }
                await ctx.Ingest.IngestJamCellsAsync(parsed.Jams, ctx.Ct);
                seen += parsed.Jams.Count;
                if (page >= parsed.TotalPages) break;
            }
        }

        List<(int Id, string Slug)> targets;
        await using (var db = await ctx.Db.CreateDbContextAsync(ctx.Ct))
        {
            var now = DateTime.UtcNow;
            var since = now.AddDays(-400);
            var candidates = await db.Jams.AsNoTracking()
                .Where(j => j.EntriesFetchedAt == null && j.Submissions >= 10 && j.Submissions <= 20000
                            && (j.VotingEndAt ?? j.EndAt) < now.AddDays(-1) && (j.VotingEndAt ?? j.EndAt) > since)
                .OrderByDescending(j => j.IsRanked).ThenByDescending(j => j.Joined)
                .Select(j => new { j.Id, j.Slug })
                .Take(entriesJams)
                .ToListAsync(ctx.Ct);
            targets = candidates.Select(c => (c.Id, c.Slug)).ToList();
        }

        var entries = 0;
        foreach (var (id, slug) in targets)
        {
            if (ctx.RemainingBudget < 2) break;
            ctx.Step($"Джем {slug}: участники");
            var page = await ctx.FetchAsync($"{ItchCatalog.BaseUrl}/jam/{slug}");
            if (page is not { IsSuccess: true }) continue;
            var data = JamParsers.ParseJamPage(page.Body);
            await ctx.Ingest.IngestJamPageAsync(id, data, ctx.Ct);
            if (data.ItchJamId is null)
            {
                ctx.ParseError($"/jam/{slug}: не найден id джема");
                continue;
            }
            var json = await ctx.FetchAsync($"{ItchCatalog.BaseUrl}/jam/{data.ItchJamId}/entries.json", "application/json");
            if (json is not { IsSuccess: true }) continue;
            try
            {
                entries += await ctx.Ingest.IngestJamEntriesAsync(id, JamParsers.ParseEntries(json.Body), ctx.Ct);
            }
            catch (JsonException ex)
            {
                ctx.ParseError($"/jam/{slug}/entries.json: {ex.Message}");
            }
        }
        ctx.Info($"Джемы: карточек {seen}, загружено участников {entries} из {targets.Count} джемов");
    }
}

/// <summary>Refreshes tracked games and finds their positions in "popular" lists of each of their tags.</summary>
public sealed class TrackedGamesJob : ICrawlJob
{
    public string Title => "Отслеживаемые игры: позиции в тегах";

    public async Task RunAsync(CrawlContext ctx)
    {
        List<TrackedGame> tracked;
        await using (var db = await ctx.Db.CreateDbContextAsync(ctx.Ct))
            tracked = await db.TrackedGames.AsNoTracking().ToListAsync(ctx.Ct);

        for (var i = 0; i < tracked.Count && ctx.HasBudget; i++)
        {
            var t = tracked[i];
            ctx.Step($"Отслеживание {i + 1} из {tracked.Count}: {t.Url}", i + 1, tracked.Count);
            await TrackOneAsync(ctx, t);
        }
    }

    public static async Task<long?> TrackOneAsync(CrawlContext ctx, TrackedGame t)
    {
        var r = await ctx.FetchAsync(t.Url);
        if (r is not { IsSuccess: true }) return null;
        var data = GamePageParser.Parse(r.Body);
        if (data.Id is not { } gameId)
        {
            ctx.ParseError($"{t.Url}: не найден id игры");
            return null;
        }
        await ctx.Ingest.IngestGameDetailsAsync(data, t.Url, ctx.Ct);

        List<string> slugs;
        await using (var db = await ctx.Db.CreateDbContextAsync(ctx.Ct))
        {
            slugs = await db.GameTags.AsNoTracking()
                .Where(gt => gt.GameId == gameId && (gt.Tag!.Kind == TagKind.Tag || gt.Tag.Kind == TagKind.Genre))
                .OrderByDescending(gt => gt.Tag!.ProjectsCount)
                .Select(gt => gt.Tag!.Slug)
                .Take(15)
                .ToListAsync(ctx.Ct);
            var row = await db.TrackedGames.FirstAsync(x => x.Id == t.Id, ctx.Ct);
            row.GameId = gameId;
            row.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ctx.Ct);
        }

        var lists = new List<ListSpec> { ListSpec.Of(ListSpec.Popular), ListSpec.Of(ListSpec.NewAndPopular) };
        lists.AddRange(slugs.Select(s => ListSpec.Of(ListSpec.Popular, s)));
        foreach (var spec in lists)
        {
            for (var page = 1; page <= Math.Max(1, t.MaxPages) && ctx.HasBudget; page++)
            {
                var first = (page - 1) * ItchCatalog.PageSize + 1;
                if (!await ctx.Ingest.HasPositionsAsync(ctx.Today, spec.Key, first, ctx.Ct))
                {
                    var res = await ctx.FetchAsync(spec.Url(page, json: true));
                    if (res is not { IsSuccess: true }) break;
                    ListingPage lp;
                    try
                    {
                        lp = ListingParser.ParseJson(res.Body);
                    }
                    catch (JsonException)
                    {
                        break;
                    }
                    ctx.CountGames(await ctx.Ingest.IngestCardsAsync(lp.Cards, new CardContext(spec.MembershipFilters, false, null, false), ctx.Ct));
                    await ctx.Ingest.SavePositionsAsync(ctx.Today, spec.Key, first, lp.Cards.Select(c => c.Id).ToList(), ctx.Ct);
                    if (lp.Cards.Any(c => c.Id == gameId) || lp.Cards.Count < ItchCatalog.PageSize) break;
                }
                else
                {
                    await using var db = await ctx.Db.CreateDbContextAsync(ctx.Ct);
                    var day = ctx.Today;
                    var key = spec.Key;
                    var found = await db.TagPositions.AnyAsync(p => p.Day == day && p.ListKey == key && p.GameId == gameId, ctx.Ct);
                    if (found) break;
                }
            }
        }
        return gameId;
    }
}
