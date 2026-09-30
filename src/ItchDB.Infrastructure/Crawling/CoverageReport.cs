using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Crawling;

public sealed record YearCoverage(int Year, int? TotalResults, int KnownGames, int RatedGames, string Status, int PartsDone, int PartsTotal);

public sealed record DatabaseStats(
    int Games,
    int RatedGames,
    int GamesWithDetails,
    int NsfwGames,
    int Developers,
    int DevelopersWithProfile,
    int Tags,
    int Jams,
    int JamsWithEntries,
    int JamEntries,
    int SnapshotDays,
    DateOnly? FirstSnapshotDay,
    long DatabaseBytes,
    int? CatalogTotal,
    int ListsComplete,
    int ListsTruncated,
    int ListsInProgress,
    IReadOnlyList<YearCoverage> Years);

/// <summary>What is in the database and how much of the itch.io catalog the crawl has covered.</summary>
public sealed class CoverageReport(IDbContextFactory<ItchDbContext> dbFactory, CursorStore cursors, DataPaths paths)
{
    public async Task<DatabaseStats> LoadAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var games = await db.Games.CountAsync(ct);
        var rated = await db.Games.CountAsync(g => g.RatingCount > 0, ct);
        var details = await db.Games.CountAsync(g => g.DetailsFetchedAt != null && g.DetailsFailures == 0, ct);
        var nsfw = await db.Games.CountAsync(g => g.IsNsfw, ct);
        var devs = await db.Developers.CountAsync(ct);
        var devsProfile = await db.Developers.CountAsync(d => d.Followers != null, ct);
        var tags = await db.Tags.CountAsync(ct);
        var jams = await db.Jams.CountAsync(ct);
        var jamsEntries = await db.Jams.CountAsync(j => j.EntriesFetchedAt != null, ct);
        var entries = await db.JamEntries.CountAsync(ct);
        var days = await db.GameSnapshots.Select(s => s.Day).Distinct().CountAsync(ct);
        var firstDay = await db.GameSnapshots.OrderBy(s => s.Day).Select(s => (DateOnly?)s.Day).FirstOrDefaultAsync(ct);
        var catalogTotal = await db.ListingTotals.Where(t => t.FilterKey == "").OrderByDescending(t => t.Day).Select(t => (int?)t.Results).FirstOrDefaultAsync(ct);

        var byYear = await db.Games.Where(g => g.PublishedYear != null && !g.IsNsfw)
            .GroupBy(g => g.PublishedYear!.Value)
            .Select(x => new { Year = x.Key, Known = x.Count(), Rated = x.Count(g => g.RatingCount > 0) })
            .ToDictionaryAsync(x => x.Year, ct);
        var yearKeys = Enumerable.Range(ItchCatalog.FirstYear, DateTime.UtcNow.Year - ItchCatalog.FirstYear + 1).Select(y => $"year-{y}").ToList();
        var totals = (await db.ListingTotals.Where(t => yearKeys.Contains(t.FilterKey)).ToListAsync(ct))
            .GroupBy(t => t.FilterKey).ToDictionary(x => x.Key, x => x.OrderByDescending(t => t.Day).First().Results);
        if (catalogTotal is { } catalog && totals.Count > 0 && catalog < totals.Values.Max()) catalogTotal = null;

        var all = await cursors.GetAllAsync<ListCursor>("list:", ct);
        var available = await CrawlMeta.AvailableYearsAsync(cursors, ct);
        var years = new List<YearCoverage>();
        for (var y = DateTime.UtcNow.Year; y >= ItchCatalog.FirstYear; y--)
        {
            var yearSlug = $"year-{y}";
            all.TryGetValue($"list:top-rated/{yearSlug}", out var main);
            var parts = all.Where(kv => IsYearPartition(kv.Key, yearSlug)).Select(kv => kv.Value).ToList();
            var leafDone = parts.Count(p => p.Status is ListStatus.Complete or ListStatus.Unsupported);
            var status = !available.Contains(y) ? "нет фильтра года — через жанры и теги"
                : main switch
                {
                    null => "не начат",
                    { Status: ListStatus.Complete } => "полностью",
                    { Status: ListStatus.InProgress } => $"в процессе, стр. {main.Page}",
                    { Status: ListStatus.Truncated } => parts.Count == 0 ? "нужно дробление" : $"дробится: {leafDone} частей готово",
                    { Status: ListStatus.Unsupported } => "фильтр не поддерживается",
                    _ => main.Status.ToString(),
                };
            byYear.TryGetValue(y, out var known);
            years.Add(new YearCoverage(y, totals.GetValueOrDefault(yearSlug), known?.Known ?? 0, known?.Rated ?? 0, status, leafDone, parts.Count));
        }

        long bytes = 0;
        foreach (var f in new[] { paths.DatabaseFile, paths.DatabaseFile + "-wal" })
            if (File.Exists(f)) bytes += new FileInfo(f).Length;

        return new DatabaseStats(games, rated, details, nsfw, devs, devsProfile, tags, jams, jamsEntries, entries, days, firstDay, bytes,
            catalogTotal,
            all.Values.Count(c => c.Status == ListStatus.Complete),
            all.Values.Count(c => c.Status == ListStatus.Truncated),
            all.Values.Count(c => c.Status == ListStatus.InProgress),
            years);
    }

    public async Task<List<CrawlRun>> LoadRunsAsync(int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CrawlRuns.AsNoTracking().OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
    }

    private static bool IsYearPartition(string key, string yearSlug)
    {
        if (!key.StartsWith("list:top-rated/", StringComparison.Ordinal)) return false;
        var filters = key["list:top-rated/".Length..].Split('/');
        return filters.Length > 1 && filters.Contains(yearSlug)
            && filters.All(f => f == yearSlug || f is "free" or "store" || ItchCatalog.IsGenreSlug(f) || f.StartsWith("platform-", StringComparison.Ordinal));
    }
}
