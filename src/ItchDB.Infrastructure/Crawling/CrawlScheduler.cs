using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>
/// Starts the daily update while the app is open. A launch with <c>--daily</c> runs one collection
/// and then shuts the process down, which is how the Windows task leaves the machine.
/// </summary>
public sealed class CrawlScheduler(
    CrawlCoordinator coordinator,
    SettingsService settings,
    IDbContextFactory<ItchDbContext> dbFactory,
    CookieStore cookies,
    ScheduledCollectionMode mode,
    IHostApplicationLifetime lifetime,
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
            if (mode.ExitAfterRun)
            {
                try
                {
                    await CollectOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Запуск по планировщику не удался");
                }
                return;
            }

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
        finally
        {
            if (mode.ExitAfterRun) lifetime.StopApplication();
        }
    }

    private async Task CollectOnceAsync(CancellationToken ct)
    {
        if (!settings.Current.AutoDailyEnabled)
        {
            logger.LogInformation("Автообновление выключено. Запуск планировщика закрывается");
            return;
        }

        bool hasGames;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            hasGames = await db.Games.AnyAsync(ct);
        var (_, next) = await coordinator.GetScheduleAsync(ct);
        if (!ScheduledCollection.ShouldCollect(true, hasGames, next, DateTime.UtcNow))
        {
            logger.LogInformation("Ежедневный сбор ещё не требуется. Приложение закрывается");
            return;
        }

        var profile = ScheduledCollection.ProfileFor(hasGames);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(CrawlProfile finishedProfile, CrawlRunStatus _)
        {
            if (finishedProfile == profile) done.TrySetResult();
        }

        coordinator.RunFinished += Finished;
        try
        {
            logger.LogInformation("Запуск сбора по планировщику заданий. После завершения приложение закроется");
            if (!coordinator.TryStart(profile, "планировщик заданий"))
            {
                logger.LogInformation("Сбор уже идёт. Приложение закрывается");
                return;
            }
            await done.Task.WaitAsync(ct);
            logger.LogInformation("Сбор по планировщику завершён. Приложение закрывается");
        }
        finally
        {
            coordinator.RunFinished -= Finished;
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
