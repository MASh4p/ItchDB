using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>Runs one crawl profile at a time in the background and exposes live progress to the UI.</summary>
public sealed class CrawlCoordinator
{
    private const int MaxLog = 500;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDbContextFactory<ItchDbContext> _db;
    private readonly SettingsService _settings;
    private readonly CatalogIngestor _ingest;
    private readonly CursorStore _cursors;
    private readonly ILogger<CrawlCoordinator> _logger;
    private readonly Lock _gate = new();
    private readonly LinkedList<CrawlLogEntry> _log = new();
    private CancellationTokenSource? _cts;
    private TaskCompletionSource _resume = CompletedSource();
    private CrawlContext? _ctx;
    private long _lastNotifyTicks;

    public CrawlCoordinator(
        IServiceScopeFactory scopes,
        IDbContextFactory<ItchDbContext> db,
        SettingsService settings,
        CatalogIngestor ingest,
        CursorStore cursors,
        IHostApplicationLifetime lifetime,
        ILogger<CrawlCoordinator> logger)
    {
        _scopes = scopes;
        _db = db;
        _settings = settings;
        _ingest = ingest;
        _cursors = cursors;
        _logger = logger;
        lifetime.ApplicationStopping.Register(Stop);
    }

    public CrawlState State { get; private set; }
    public CrawlProfile? Profile { get; private set; }
    public string? Trigger { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public string? CurrentJob { get; private set; }
    public int JobIndex { get; private set; }
    public int JobCount { get; private set; }
    public string? StepText { get; private set; }
    public int StepDone { get; private set; }
    public int StepTotal { get; private set; }
    public int Requests => _ctx?.RequestsUsed ?? 0;
    public int RequestBudget => _ctx?.RequestBudget ?? 0;
    public int Errors => _ctx?.Errors ?? 0;
    public int ParseErrors => _ctx?.ParseErrors ?? 0;
    public int GamesUpserted => _ctx?.GamesUpserted ?? 0;
    public CrawlRunStatus? LastStatus { get; private set; }
    public DateTime? LastFinishedAt { get; private set; }
    public string? LastMessage { get; private set; }
    public bool IsBusy => State != CrawlState.Idle;

    /// <summary>Raised on progress (throttled to a few times per second) and on every state change.</summary>
    public event Action? Changed;

    /// <summary>Raised after a run ends; analytics caches subscribe to rebuild.</summary>
    public event Action<CrawlProfile, CrawlRunStatus>? RunFinished;

    public IReadOnlyList<CrawlLogEntry> GetLog()
    {
        lock (_gate) return _log.ToList();
    }

    public bool TryStart(CrawlProfile profile, string trigger)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (State != CrawlState.Idle) return false;
            State = CrawlState.Running;
            Profile = profile;
            Trigger = trigger;
            StartedAt = DateTime.UtcNow;
            CurrentJob = null;
            StepText = null;
            JobIndex = 0;
            JobCount = 0;
            StepDone = StepTotal = 0;
            _ctx = null;
            _resume = CompletedSource();
            _cts = cts = new CancellationTokenSource();
        }
        Log(LogLevel.Information, $"Старт: {CrawlProfiles.Name(profile)} ({trigger})");
        _ = Task.Run(() => RunAsync(profile, cts.Token));
        Notify(force: true);
        return true;
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State != CrawlState.Running) return;
            State = CrawlState.Paused;
            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Log(LogLevel.Information, "Пауза");
        Notify(force: true);
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (State != CrawlState.Paused) return;
            State = CrawlState.Running;
            _resume.TrySetResult();
        }
        Log(LogLevel.Information, "Продолжение");
        Notify(force: true);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (State is CrawlState.Idle or CrawlState.Stopping) return;
            State = CrawlState.Stopping;
            _cts?.Cancel();
            _resume.TrySetResult();
        }
        Notify(force: true);
    }

    internal Task WaitIfPausedAsync(CancellationToken ct)
    {
        Task wait;
        lock (_gate) wait = _resume.Task;
        return wait.IsCompleted ? Task.CompletedTask : wait.WaitAsync(ct);
    }

    internal void OnRequest() => Notify();

    internal void ReportStep(string text, int done, int total)
    {
        StepText = text;
        StepDone = done;
        StepTotal = total;
        Notify();
    }

    internal void Log(LogLevel level, string message)
    {
        lock (_gate)
        {
            _log.AddLast(new CrawlLogEntry(DateTime.UtcNow, level, message));
            while (_log.Count > MaxLog) _log.RemoveFirst();
        }
        _logger.Log(level, "[Сбор] {Message}", message);
        Notify(force: level >= LogLevel.Warning);
    }

    private async Task RunAsync(CrawlProfile profile, CancellationToken ct)
    {
        var status = CrawlRunStatus.Completed;
        string? message = null;
        int runId = 0;
        try
        {
            await using (var db = await _db.CreateDbContextAsync(ct))
            {
                var run = new CrawlRun { Profile = profile.ToString(), StartedAt = StartedAt ?? DateTime.UtcNow, Status = CrawlRunStatus.Running, Message = Trigger };
                db.CrawlRuns.Add(run);
                await db.SaveChangesAsync(ct);
                runId = run.Id;
            }

            using var scope = _scopes.CreateScope();
            var http = scope.ServiceProvider.GetRequiredService<ItchHttpClient>();
            var settings = _settings.Clone();
            var plan = CrawlPlanner.Build(profile, settings);
            JobCount = plan.Count;
            var ctx = new CrawlContext(this, profile, http, _ingest, _cursors, _db, settings, plan.Sum(p => p.Budget), ct);
            _ctx = ctx;

            foreach (var planned in plan)
            {
                ct.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(ct);
                JobIndex++;
                CurrentJob = planned.Job.Title;
                StepText = null;
                StepDone = StepTotal = 0;
                Log(LogLevel.Information, $"▶ {planned.Job.Title}");
                using (ctx.LimitBudget(planned.Budget))
                {
                    try
                    {
                        await planned.Job.RunAsync(ctx);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (CloudflareBlockedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ctx.Error($"{planned.Job.Title}: {ex.Message}");
                        _logger.LogError(ex, "Crawl job {Job} failed", planned.Job.Title);
                    }
                }
                await UpdateRunAsync(runId, CrawlRunStatus.Running, Trigger, CancellationToken.None);
            }
            message = $"Запросов {ctx.RequestsUsed}, игр обновлено {ctx.GamesUpserted}, ошибок {ctx.Errors}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            status = CrawlRunStatus.Stopped;
            message = "Остановлено. Прогресс сохранён, следующий запуск продолжит с того же места.";
        }
        catch (CloudflareBlockedException ex)
        {
            status = CrawlRunStatus.Failed;
            message = ex.Message;
        }
        catch (Exception ex)
        {
            status = CrawlRunStatus.Failed;
            message = ex.Message;
            _logger.LogError(ex, "Crawl run failed");
        }

        try
        {
            if (runId > 0) await UpdateRunAsync(runId, status, message, CancellationToken.None, finished: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save crawl run");
        }

        Log(status == CrawlRunStatus.Failed ? LogLevel.Error : LogLevel.Information, $"■ {CrawlProfiles.Name(profile)}: {message}");
        lock (_gate)
        {
            State = CrawlState.Idle;
            LastStatus = status;
            LastFinishedAt = DateTime.UtcNow;
            LastMessage = message;
            CurrentJob = null;
            StepText = null;
            _cts?.Dispose();
            _cts = null;
        }
        Notify(force: true);
        try
        {
            RunFinished?.Invoke(profile, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunFinished handler failed");
        }
    }

    private async Task UpdateRunAsync(int runId, CrawlRunStatus status, string? message, CancellationToken ct, bool finished = false)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var run = await db.CrawlRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;
        run.Status = status;
        run.Message = message;
        run.Requests = Requests;
        run.Errors = Errors;
        run.ParseErrors = ParseErrors;
        run.GamesUpserted = GamesUpserted;
        if (finished) run.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>When the last successful regular update finished and when the scheduler will start the next one.</summary>
    public async Task<(DateTime? LastSuccess, DateTime? NextDue)> GetScheduleAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        await using var db = await _db.CreateDbContextAsync(ct);
        string[] regular = [nameof(CrawlProfile.QuickStart), nameof(CrawlProfile.Daily)];
        var lastSuccess = await db.CrawlRuns.AsNoTracking()
            .Where(r => r.Status == CrawlRunStatus.Completed && regular.Contains(r.Profile))
            .MaxAsync(r => r.FinishedAt, ct);
        var lastAttempt = await db.CrawlRuns.AsNoTracking()
            .Where(r => regular.Contains(r.Profile))
            .MaxAsync(r => (DateTime?)r.StartedAt, ct);
        if (lastSuccess is null && lastAttempt is null) return (null, null);

        var next = lastSuccess?.AddHours(Math.Max(1, s.DailyIntervalHours)) ?? DateTime.UtcNow;
        if (lastAttempt is not null && (lastSuccess is null || lastAttempt > lastSuccess))
        {
            var retry = lastAttempt.Value.Add(CrawlScheduler.RetryAfterFailure);
            if (retry > next) next = retry;
        }
        return (lastSuccess, next);
    }

    private void Notify(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastNotifyTicks) < 300) return;
        Interlocked.Exchange(ref _lastNotifyTicks, now);
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Crawl Changed handler failed");
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }
}
