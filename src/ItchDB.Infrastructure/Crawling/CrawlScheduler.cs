using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>
/// Starts the daily update while the app is open. If the app was closed when an update was due,
/// the update starts shortly after the next launch.
/// </summary>
public sealed class CrawlScheduler(
    CrawlCoordinator coordinator,
    SettingsService settings,
    IDbContextFactory<ItchDbContext> dbFactory,
    CookieStore cookies,
    ILogger<CrawlScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(2);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var cf = await settings.GetCfClearanceAsync();
            if (!string.IsNullOrEmpty(cf)) cookies.ImportCfClearance(cf);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "cf_clearance import failed");
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Scheduler tick failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!settings.Current.AutoDailyEnabled || coordinator.IsBusy) return;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            if (!await db.Games.AnyAsync(ct)) return;
        }
        var (_, next) = await coordinator.GetScheduleAsync(ct);
        if (next is null || next > DateTime.UtcNow) return;
        coordinator.TryStart(CrawlProfile.Daily, "по расписанию");
    }
}
