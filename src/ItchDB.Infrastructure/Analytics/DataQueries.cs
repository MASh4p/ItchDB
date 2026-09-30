using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Analytics;

public sealed record GameJamEntry(Jam Jam, JamEntry Entry);

/// <summary>Database reads that the in-memory dataset does not keep: histories, jams, tracked games.</summary>
public sealed class DataQueries(IDbContextFactory<ItchDbContext> dbFactory)
{
    public async Task<Game?> GameAsync(long id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<List<GameSnapshot>> GameHistoryAsync(long id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GameSnapshots.AsNoTracking().Where(s => s.GameId == id).OrderBy(s => s.Day).ToListAsync(ct);
    }

    public async Task<List<TagPositionSnapshot>> PositionHistoryAsync(long id, int days = 90, CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TagPositions.AsNoTracking().Where(p => p.GameId == id && p.Day >= from)
            .OrderBy(p => p.Day).ThenBy(p => p.ListKey).ToListAsync(ct);
    }

    public async Task<List<GameJamEntry>> GameJamsAsync(long id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.JamEntries.AsNoTracking().Include(e => e.Jam).Where(e => e.GameId == id).ToListAsync(ct);
        return rows.Where(e => e.Jam is not null).Select(e => new GameJamEntry(e.Jam!, e)).OrderByDescending(x => x.Jam.EndAt).ToList();
    }

    public async Task<List<DeveloperSnapshot>> DeveloperHistoryAsync(long developerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DeveloperSnapshots.AsNoTracking().Where(s => s.DeveloperId == developerId).OrderBy(s => s.Day).ToListAsync(ct);
    }

    public async Task<List<Jam>> JamsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Jams.AsNoTracking().OrderByDescending(j => j.EndAt).ToListAsync(ct);
    }

    public async Task<Dictionary<int, int>> JamEntryCountsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.JamEntries.AsNoTracking().GroupBy(e => e.JamId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public async Task<Jam?> JamAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Jams.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<List<JamEntry>> JamEntriesAsync(int jamId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.JamEntries.AsNoTracking().Where(e => e.JamId == jamId).ToListAsync(ct);
    }

    public async Task<List<FeaturedEvent>> FeaturedAsync(int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FeaturedEvents.AsNoTracking().OrderByDescending(e => e.FeaturedAt).Take(take).ToListAsync(ct);
    }

    public async Task<List<TrackedGame>> TrackedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TrackedGames.AsNoTracking().OrderByDescending(t => t.IsOwn).ThenBy(t => t.AddedAt).ToListAsync(ct);
    }

    public async Task<TrackedGame> AddTrackedAsync(string url, bool isOwn, int maxPages, string? note, CancellationToken ct = default)
    {
        url = NormalizeGameUrl(url);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.TrackedGames.FirstOrDefaultAsync(t => t.Url == url, ct);
        if (existing is not null)
        {
            existing.IsOwn |= isOwn;
            existing.MaxPages = maxPages;
            existing.Note = note ?? existing.Note;
            await db.SaveChangesAsync(ct);
            return existing;
        }
        var gameId = await db.Games.Where(g => g.Url == url).Select(g => (long?)g.Id).FirstOrDefaultAsync(ct);
        var tracked = new TrackedGame { Url = url, GameId = gameId, IsOwn = isOwn, MaxPages = maxPages, Note = note, AddedAt = DateTime.UtcNow };
        db.TrackedGames.Add(tracked);
        await db.SaveChangesAsync(ct);
        return tracked;
    }

    public async Task RemoveTrackedAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TrackedGames.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<List<OwnGameSnapshot>> OwnSnapshotsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OwnGameSnapshots.AsNoTracking().OrderBy(s => s.Day).ToListAsync(ct);
    }

    public async Task SaveOwnSnapshotsAsync(IEnumerable<OwnGameSnapshot> rows, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var row in rows)
        {
            var existing = await db.OwnGameSnapshots.FirstOrDefaultAsync(s => s.GameId == row.GameId && s.Day == row.Day, ct);
            if (existing is null)
            {
                db.OwnGameSnapshots.Add(row);
            }
            else
            {
                existing.Title = row.Title;
                existing.Views = row.Views;
                existing.Downloads = row.Downloads;
                existing.Purchases = row.Purchases;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>"https://dev.itch.io/game?x#y" → "https://dev.itch.io/game", the form game URLs are stored in.</summary>
    public static string NormalizeGameUrl(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        return $"https://{u.Host.ToLowerInvariant()}{u.AbsolutePath.TrimEnd('/')}";
    }
}
