using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

public interface ICrawlJob
{
    string Title { get; }
    Task RunAsync(CrawlContext ctx);
}

/// <summary>Everything a job needs during a run: HTTP, storage, budget, progress reporting and pause support.</summary>
public sealed class CrawlContext
{
    private readonly CrawlCoordinator _owner;

    internal CrawlContext(
        CrawlCoordinator owner,
        CrawlProfile profile,
        ItchHttpClient http,
        CatalogIngestor ingest,
        CursorStore cursors,
        IDbContextFactory<ItchDbContext> db,
        AppSettingsModel settings,
        int requestBudget,
        CancellationToken ct)
    {
        _owner = owner;
        Profile = profile;
        Http = http;
        Ingest = ingest;
        Cursors = cursors;
        Db = db;
        Settings = settings;
        RequestBudget = requestBudget;
        Ct = ct;
    }

    public CrawlProfile Profile { get; }
    public ItchHttpClient Http { get; }
    public CatalogIngestor Ingest { get; }
    public CursorStore Cursors { get; }
    public IDbContextFactory<ItchDbContext> Db { get; }
    public AppSettingsModel Settings { get; }
    public CancellationToken Ct { get; }
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public int RequestBudget { get; }
    public int RequestsUsed { get; private set; }
    private int _cap = int.MaxValue;
    public int RemainingBudget => Math.Max(0, Math.Min(RequestBudget, _cap) - RequestsUsed);
    public bool HasBudget => RequestsUsed < Math.Min(RequestBudget, _cap);

    /// <summary>Limits the requests of the current job; budget left unused by earlier jobs is not carried over.</summary>
    public IDisposable LimitBudget(int requests)
    {
        var previous = _cap;
        _cap = RequestsUsed + Math.Max(0, requests);
        return new Restore(() => _cap = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    public int Errors { get; private set; }
    public int ParseErrors { get; private set; }
    public int GamesUpserted { get; private set; }

    /// <summary>Fetches a URL; returns null when the request failed after retries (already logged and counted).</summary>
    public async Task<FetchResult?> FetchAsync(string url, string? accept = null)
    {
        await _owner.WaitIfPausedAsync(Ct);
        Ct.ThrowIfCancellationRequested();
        RequestsUsed++;
        _owner.OnRequest();
        var result = accept is null ? await Http.GetAsync(url, Ct) : await Http.GetAsync(url, Ct, accept);
        if (result.StatusCode == 0 || result.StatusCode >= 400 && !result.IsNotFound)
        {
            Errors++;
            Warn($"HTTP {result.StatusCode}: {url}");
            return result.StatusCode == 0 ? null : result;
        }
        return result;
    }

    public void CountGames(IngestResult r) => GamesUpserted += r.Inserted + r.Updated;

    public void Step(string text, int done = 0, int total = 0) => _owner.ReportStep(text, done, total);

    public void Info(string message) => _owner.Log(LogLevel.Information, message);

    public void Warn(string message) => _owner.Log(LogLevel.Warning, message);

    public void ParseError(string message)
    {
        ParseErrors++;
        _owner.Log(LogLevel.Warning, "Ошибка разбора: " + message);
    }

    public void Error(string message)
    {
        Errors++;
        _owner.Log(LogLevel.Error, message);
    }
}
