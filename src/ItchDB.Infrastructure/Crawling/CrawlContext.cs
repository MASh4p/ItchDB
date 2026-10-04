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
        CancellationToken ct,
        CrawlContext? parent = null,
        bool quiet = false)
    {
        _owner = owner;
        _parent = parent;
        _quiet = quiet;
        Profile = profile;
        Http = http;
        Ingest = ingest;
        Cursors = cursors;
        Db = db;
        Settings = settings;
        RequestBudget = requestBudget;
        Ct = ct;
        if (parent is not null) _cap = requestBudget;
    }

    /// <summary>
    /// A separate request budget that does not consume the foreground job's cap.
    /// Progress text stays with the foreground job; counters are added to the root run.
    /// </summary>
    public CrawlContext Branch(int budget, CancellationToken ct) =>
        new(_owner, Profile, Http, Ingest, Cursors, Db, Settings, budget, ct, Root, quiet: true);

    public CrawlProfile Profile { get; }
    public ItchHttpClient Http { get; }
    public CatalogIngestor Ingest { get; }
    public CursorStore Cursors { get; }
    public IDbContextFactory<ItchDbContext> Db { get; }
    public AppSettingsModel Settings { get; }
    public CancellationToken Ct { get; }
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public int RequestBudget { get; }
    private readonly CrawlContext? _parent;
    private readonly bool _quiet;
    private CrawlContext Root => _parent ?? this;
    private int _requests;
    private int _spent;
    /// <summary>Requests of this run, including background game pages.</summary>
    public int RequestsUsed => Volatile.Read(ref _requests);
    private int _cap = int.MaxValue;
    public int RemainingBudget => Math.Max(0, Math.Min(RequestBudget, _cap) - Volatile.Read(ref _spent));
    public bool HasBudget => Volatile.Read(ref _spent) < Math.Min(RequestBudget, _cap);

    /// <summary>Limits the requests of the current job; budget left unused by earlier jobs is not carried over.</summary>
    public IDisposable LimitBudget(int requests)
    {
        var previous = _cap;
        _cap = Volatile.Read(ref _spent) + Math.Max(0, requests);
        return new Restore(() => _cap = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private int _errors;
    private int _parseErrors;
    private int _gamesUpserted;
    public int Errors => Volatile.Read(ref Root._errors);
    public int ParseErrors => Volatile.Read(ref Root._parseErrors);
    public int GamesUpserted => Volatile.Read(ref Root._gamesUpserted);

    /// <summary>Fetches a URL; returns null when the request failed after retries (already logged and counted).</summary>
    public async Task<FetchResult?> FetchAsync(string url, string? accept = null, bool gamePage = false)
    {
        await _owner.WaitIfPausedAsync(Ct);
        Ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _spent);
        Interlocked.Increment(ref Root._requests);
        _owner.OnRequest();
        var result = accept is null
            ? await Http.GetAsync(url, Ct, gamePage: gamePage)
            : await Http.GetAsync(url, Ct, accept, gamePage);
        if (result.StatusCode == 0 || result.StatusCode >= 400 && !result.IsNotFound)
        {
            Interlocked.Increment(ref Root._errors);
            Warn(result.StatusCode == 0
                ? $"нет ответа, соединение оборвалось: {url}"
                : $"HTTP {result.StatusCode}: {url}");
            return result.StatusCode == 0 ? null : result;
        }
        return result;
    }

    public void CountGames(IngestResult r) => Interlocked.Add(ref Root._gamesUpserted, r.Inserted + r.Updated);

    /// <summary>
    /// Runs independent items together, up to the parallel-request setting.
    /// One item waits only for its own fetch; the others are not held behind it.
    /// </summary>
    public async Task ForEachParallelAsync<T>(IReadOnlyList<T> items, Func<T, int, Task> body)
    {
        if (items.Count == 0) return;
        var degree = Math.Min(Math.Clamp(Settings.ParallelRequests, 1, 8), items.Count);
        var next = 0;
        var stop = 0;
        var workers = new Task[degree];
        for (var w = 0; w < degree; w++) workers[w] = Work();
        try
        {
            await Task.WhenAll(workers);
        }
        catch (Exception ex)
        {
            var inner = ex is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : (IReadOnlyList<Exception>)[ex];
            if (Ct.IsCancellationRequested && inner.FirstOrDefault(e => e is OperationCanceledException) is { } cancel)
                throw cancel;
            throw inner.Count == 1 ? inner[0] : ex;
        }

        async Task Work()
        {
            while (Volatile.Read(ref stop) == 0)
            {
                if (!HasBudget) return;
                Ct.ThrowIfCancellationRequested();
                var i = Interlocked.Increment(ref next) - 1;
                if (i >= items.Count) return;
                try
                {
                    await body(items[i], i);
                }
                catch
                {
                    Interlocked.Exchange(ref stop, 1);
                    throw;
                }
            }
        }
    }

    public void Step(string text, int done = 0, int total = 0)
    {
        if (!_quiet) _owner.ReportStep(text, done, total);
    }

    public void Info(string message) => _owner.Log(LogLevel.Information, message);

    public void Warn(string message) => _owner.Log(LogLevel.Warning, message);

    public void ParseError(string message)
    {
        Interlocked.Increment(ref Root._parseErrors);
        _owner.Log(LogLevel.Warning, "Ошибка разбора: " + message);
    }

    public void Error(string message)
    {
        Interlocked.Increment(ref Root._errors);
        _owner.Log(LogLevel.Error, message);
    }
}
