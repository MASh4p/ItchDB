using System.Text.Json;
using ItchDB.Core.Catalog;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Parsing;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>
/// Walks a browse list page by page. For top-rated lists it stops once unrated games appear (the rated part is complete).
/// A list that still has rated games on page 200 is truncated; with Split enabled it is re-crawled as smaller
/// sub-lists (price, then genre, then platform), which is how the 200-page limit is bypassed.
/// </summary>
public static class ListCrawler
{
    private const int MaxSplitDepth = 3;

    public static async Task<ListOutcome> CrawlAsync(CrawlContext ctx, ListTask task, int depth = 0)
    {
        var spec = task.Spec;
        var cursorKey = "list:" + spec.Key;
        var cursor = await ctx.Cursors.GetAsync<ListCursor>(cursorKey, ctx.Ct);
        var fresh = cursor is not null && cursor.UpdatedAt > DateTime.UtcNow.AddDays(-Math.Max(1, ctx.Settings.DeepRefreshDays));
        if (fresh && cursor!.Status == ListStatus.Unsupported)
            return new ListOutcome(0, true, false, null, Skipped: true, Unsupported: true);
        if (task.UseCursor && fresh)
        {
            if (cursor!.Status == ListStatus.Complete)
                return new ListOutcome(0, true, false, cursor.Total, Skipped: true);
            if (cursor.Status == ListStatus.Truncated)
                return task.Split ? await SplitAsync(ctx, task, cursor.Total, depth) : new ListOutcome(0, false, true, cursor.Total, Skipped: true);
        }
        if (!task.UseCursor || !fresh) cursor = null;

        var startPage = cursor is { Status: ListStatus.InProgress } ? cursor.Page + 1 : 1;
        var total = cursor?.Total;
        var cardCtx = new CardContext(spec.MembershipFilters, spec.IsTopRated, spec.Year, spec.IsNsfw, spec.Sort == ListSpec.Featured);
        var maxPages = Math.Min(task.MaxPages, ItchCatalog.MaxPages);
        int pages = 0;
        bool reachedEnd = false, truncated = false;

        for (var page = startPage; page <= maxPages;)
        {
            if (!ctx.HasBudget) break;
            // Page 1 is the HTML counter. Later pages are JSON and are fetched together.
            var batch = page == 1 ? 1 : Math.Min(4, Math.Min(maxPages - page + 1, Math.Max(1, ctx.RemainingBudget)));
            ctx.Step($"{spec} — стр. {page}{(batch > 1 ? $"–{page + batch - 1}" : "")}{(total is { } t ? $" из ~{Math.Min(ItchCatalog.MaxPages, (t + 35) / 36)}" : "")}",
                page, Math.Min(maxPages, total is { } tt ? (tt + 35) / 36 : maxPages));

            var fetched = new FetchResult?[batch];
            await Task.WhenAll(Enumerable.Range(0, batch).Select(async n =>
            {
                var p = page + n;
                fetched[n] = await ctx.FetchAsync(spec.Url(p, json: p != 1));
            }));

            var stop = false;
            for (var n = 0; n < batch && !stop; n++)
            {
                var current = page + n;
                var html = current == 1;
                var url = spec.Url(current, json: !html);
                var r = fetched[n];
            if (r is null) { stop = true; break; }
            if (r.IsNotFound || r.IsSuccess && IsRedirectedAway(r, spec))
            {
                if (current == 1)
                {
                    ctx.Warn($"itch.io не поддерживает такой список: {spec}{(r.IsNotFound ? "" : $" (перенаправил на {new Uri(r.Url).AbsolutePath})")}");
                    await ctx.Cursors.SetAsync(cursorKey, new ListCursor(0, ListStatus.Unsupported, null, DateTime.UtcNow), ctx.Ct);
                    return new ListOutcome(0, true, false, null, Unsupported: true);
                }
                reachedEnd = true;
                stop = true;
                break;
            }
            if (!r.IsSuccess) { stop = true; break; }

            ListingPage lp;
            var jsonBecameHtml = !html && r.Body.AsSpan().TrimStart().StartsWith("<");
            try
            {
                lp = ListingParser.ParseRequested(r.Body, json: !html);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                ctx.ParseError($"{url}: {ex.Message}");
                stop = true;
                break;
            }
            if (jsonBecameHtml && lp.Cards.Count == 0)
            {
                ctx.ParseError($"{url}: вместо JSON пришла HTML-страница без списка игр");
                stop = true;
                break;
            }

            if (html)
            {
                if (lp.TotalResults is { } results)
                {
                    total = results;
                    // Featured and new-and-popular are curated subsets: their counter is not the size of the filter.
                    if (spec.Sort is not (ListSpec.Featured or ListSpec.NewAndPopular))
                        await ctx.Ingest.SaveListingTotalAsync(ctx.Today, spec.FilterKey, results, ctx.Ct);
                }
                else if (lp.Cards.Count > 0)
                {
                    ctx.ParseError($"{url}: не найден счётчик results");
                }
                if (spec.Year is { } year) await CrawlMeta.ConfirmYearAsync(ctx.Cursors, year, ctx.Ct);
            }

            if (lp.Cards.Count > 0)
            {
                ctx.CountGames(await ctx.Ingest.IngestCardsAsync(lp.Cards, cardCtx, ctx.Ct));
                if (task.RecordPositions)
                    await ctx.Ingest.SavePositionsAsync(ctx.Today, spec.Key, (current - 1) * ItchCatalog.PageSize + 1, lp.Cards.Select(c => c.Id).ToList(), ctx.Ct);
            }
            pages = current;
            if (task.UseCursor) await ctx.Cursors.SetAsync(cursorKey, new ListCursor(current, ListStatus.InProgress, total, DateTime.UtcNow), ctx.Ct);

            var rawCount = html ? lp.Cards.Count : Math.Max(lp.NumItems, lp.Cards.Count);
            if (rawCount < ItchCatalog.PageSize)
            {
                reachedEnd = true;
                stop = true;
                break;
            }
            if (task.StopAtUnrated && spec.IsTopRated && lp.Cards.Any(c => !c.HasRating))
            {
                reachedEnd = true;
                stop = true;
                break;
            }
            if (current == ItchCatalog.MaxPages) truncated = !spec.IsTopRated || lp.Cards.All(c => c.HasRating);
            }
            if (stop) break;
            page += batch;
        }

        if (task.UseCursor && (reachedEnd || truncated))
            await ctx.Cursors.SetAsync(cursorKey, new ListCursor(pages, reachedEnd ? ListStatus.Complete : ListStatus.Truncated, total, DateTime.UtcNow), ctx.Ct);

        if (truncated && task.Split) return await SplitAsync(ctx, task, total, depth);
        return new ListOutcome(pages, reachedEnd, truncated, total);
    }

    /// <summary>
    /// itch.io silently redirects unknown filters (e.g. a year it has no filter for) to a broader list.
    /// Ingesting that page would attach the wrong year or tag to every game on it.
    /// </summary>
    public static bool IsRedirectedAway(FetchResult r, ListSpec spec)
    {
        if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var final)) return false;
        static string[] Segments(string path) =>
            path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return !Segments(final.AbsolutePath).SequenceEqual(Segments(spec.Path), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<ListOutcome> SplitAsync(CrawlContext ctx, ListTask task, int? parentTotal, int depth)
    {
        var subs = SplitSpec(task.Spec);
        if (subs.Count == 0 || depth >= MaxSplitDepth)
        {
            ctx.Warn($"{task.Spec}: упёрлись в лимит 200 страниц, дальнейшее дробление невозможно");
            return new ListOutcome(0, false, true, parentTotal);
        }
        ctx.Info($"{task.Spec}: больше 200 страниц — дробим на {subs.Count} частей");
        var pages = 0;
        foreach (var sub in subs)
        {
            if (!ctx.HasBudget) break;
            var outcome = await CrawlAsync(ctx, task with { Spec = sub, MaxPages = ItchCatalog.MaxPages }, depth + 1);
            pages += outcome.Pages;
            if (!outcome.Skipped && parentTotal is not null && outcome.Total == parentTotal)
            {
                ctx.Warn($"{sub}: фильтр не сузил список, дробление остановлено");
                break;
            }
        }
        return new ListOutcome(pages, false, true, parentTotal);
    }

    /// <summary>
    /// Next partition level for a truncated list: price, then genre, then platform.
    /// Years are not a split level: the year filter exists only for some years, so splitting by it would lose the rest.
    /// </summary>
    public static IReadOnlyList<ListSpec> SplitSpec(ListSpec spec)
    {
        if (!spec.HasPriceFilter) return [spec.With("free"), spec.With("store")];
        if (!spec.HasGenreFilter) return ItchCatalog.Genres.Select(g => spec.With(g.Slug)).ToList();
        if (!spec.HasPlatformFilter)
            return ItchCatalog.PlatformFilters.Where(p => p.Flag != Core.Domain.Platforms.IOS).Select(p => spec.With(p.Slug)).ToList();
        return [];
    }
}
