using System.Diagnostics;
using ItchDB.Core.Analytics;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Crawling;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Analytics;

/// <summary>
/// Holds the current <see cref="AnalyticsSnapshot"/>. Reloads it after every crawl run (and periodically during long runs)
/// and stores daily per-segment statistics, which become the history charts of genres and tags.
/// </summary>
public sealed class AnalyticsService : IDisposable
{
    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly DatasetLoader _loader;
    private readonly SettingsService _settings;
    private readonly CrawlCoordinator _crawl;
    private readonly IDbContextFactory<ItchDbContext> _dbFactory;
    private readonly ILogger<AnalyticsService> _log;
    private readonly Lock _sync = new();
    private AnalyticsSnapshot _current = AnalyticsSnapshot.Empty;
    private Task? _loading;
    private bool _pendingSnapshot;
    private DateTime _lastLoadAt = DateTime.MinValue;

    public AnalyticsService(DatasetLoader loader, SettingsService settings, CrawlCoordinator crawl,
        IDbContextFactory<ItchDbContext> dbFactory, ILogger<AnalyticsService> log)
    {
        _loader = loader;
        _settings = settings;
        _crawl = crawl;
        _dbFactory = dbFactory;
        _log = log;
        _crawl.RunFinished += OnRunFinished;
        _crawl.Changed += OnCrawlChanged;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised after a reload finished (or started, to show the loading state).</summary>
    public event Action? Updated;

    public AnalyticsSnapshot Current => _current;
    public bool IsLoaded { get; private set; }
    public bool IsLoading => _loading is { IsCompleted: false };
    public TimeSpan? LastLoadDuration { get; private set; }
    public string? LastError { get; private set; }

    public Task EnsureLoadedAsync() => IsLoaded ? Task.CompletedTask : RefreshAsync();

    /// <summary>Reloads the dataset; concurrent calls share one load.</summary>
    public Task RefreshAsync(bool writeSegmentHistory = false)
    {
        lock (_sync)
        {
            _pendingSnapshot |= writeSegmentHistory;
            if (_loading is { IsCompleted: false }) return _loading;
            _loading = Task.Run(LoadAsync);
            return _loading;
        }
    }

    private async Task LoadAsync()
    {
        Updated?.Invoke();
        var sw = Stopwatch.StartNew();
        try
        {
            var p = _settings.Current.ToAnalyticsParams();
            var data = await _loader.LoadAsync(p);
            var snapshot = new AnalyticsSnapshot(data, p, DateTime.UtcNow);
            _current = snapshot;
            IsLoaded = true;
            LastError = null;
            LastLoadDuration = sw.Elapsed;
            _log.LogInformation("Analytics dataset loaded: {Games} games, {Tags} tags in {Elapsed:0.0} s",
                data.Games.Count, data.Tags.Count, sw.Elapsed.TotalSeconds);

            bool write;
            lock (_sync)
            {
                write = _pendingSnapshot;
                _pendingSnapshot = false;
            }
            if (write && !data.IsEmpty) await WriteSegmentHistoryAsync(snapshot);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log.LogError(ex, "Analytics dataset load failed");
        }
        finally
        {
            _lastLoadAt = DateTime.UtcNow;
            Updated?.Invoke();
        }
    }

    /// <summary>One row per segment and day with the unfiltered defaults, so history is comparable across days.</summary>
    private async Task WriteSegmentHistoryAsync(AnalyticsSnapshot s)
    {
        var f = new AnalyticsFilter(false, _settings.Current.DefaultMinRatings);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = new List<SegmentStats> { s.All(f) };
        rows.AddRange(s.Genres(f));
        rows.AddRange(s.Tags(f));
        rows.AddRange(s.Engines(f));
        rows.AddRange(s.Formats(f));

        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.SegmentStats.Where(x => x.Day == today).ExecuteDeleteAsync();
        db.SegmentStats.AddRange(rows.GroupBy(r => r.Key).Select(g => g.First()).Select(r => new SegmentStatsSnapshot
        {
            Day = today,
            SegmentKey = r.Key,
            TotalItch = r.TotalItch,
            KnownGames = r.Known,
            RatedGames = r.Rated,
            MedianRatings = double.IsNaN(r.Median) ? 0 : r.Median,
            P75Ratings = double.IsNaN(r.P75) ? 0 : r.P75,
            P90Ratings = double.IsNaN(r.P90) ? 0 : r.P90,
            SumRatings = r.SumRatings,
            SuccessShare = r.SuccessShare,
            Opportunity = r.Opportunity,
        }));
        await db.SaveChangesAsync();
    }

    public async Task<List<SegmentStatsSnapshot>> SegmentHistoryAsync(string segmentKey, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SegmentStats.AsNoTracking().Where(x => x.SegmentKey == segmentKey).OrderBy(x => x.Day).ToListAsync(ct);
    }

    private void OnRunFinished(CrawlProfile profile, CrawlRunStatus status) =>
        _ = RefreshAsync(writeSegmentHistory: status == CrawlRunStatus.Completed && profile != CrawlProfile.Tracking);

    private void OnCrawlChanged()
    {
        if (_crawl.IsBusy && DateTime.UtcNow - _lastLoadAt > LiveRefreshInterval && !IsLoading) _ = RefreshAsync();
    }

    private void OnSettingsChanged() => _ = RefreshAsync();

    public void Dispose()
    {
        _crawl.RunFinished -= OnRunFinished;
        _crawl.Changed -= OnCrawlChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}
