using System.Text.Json;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>Resumable crawl progress stored as JSON rows in CrawlCursors.</summary>
public sealed class CursorStore(IDbContextFactory<ItchDbContext> dbFactory)
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.CrawlCursors.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
        if (row is null) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(row.Value);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(value);
        var row = await db.CrawlCursors.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (row is null)
            db.CrawlCursors.Add(new CrawlCursor { Key = key, Value = json, UpdatedAt = DateTime.UtcNow });
        else
        {
            row.Value = json;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteAsync(string prefix, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CrawlCursors.Where(c => c.Key.StartsWith(prefix)).ExecuteDeleteAsync(ct);
    }

    public async Task<Dictionary<string, T>> GetAllAsync<T>(string prefix, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.CrawlCursors.AsNoTracking().Where(c => c.Key.StartsWith(prefix)).ToListAsync(ct);
        var result = new Dictionary<string, T>();
        foreach (var row in rows)
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(row.Value) is { } v) result[row.Key] = v;
            }
            catch (JsonException)
            {
            }
        }
        return result;
    }
}
