using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>Installs or removes the Windows daily task when the app is opened and when the setting changes.</summary>
public sealed class WindowsDailyTaskHost(
    SettingsService settings,
    DataPaths paths,
    IHostEnvironment environment,
    ScheduledCollectionMode mode,
    ILogger<WindowsDailyTaskHost> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (mode.ExitAfterRun || !OperatingSystem.IsWindows()) return Task.CompletedTask;
        settings.Changed += OnSettingsChanged;
        Sync();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged() => Sync();

    private void Sync()
    {
        try
        {
            var result = WindowsDailyTask.Apply(
                settings.Current.AutoDailyEnabled,
                environment.ContentRootPath,
                paths.Root,
                Environment.ProcessPath);
            if (result == DailyTaskResult.Installed)
            {
                logger.LogInformation(
                    "Планировщик заданий: сбор каждый день в {Time}. После сбора приложение закрывается",
                    WindowsDailyTask.LocalTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (result == DailyTaskResult.Removed)
            {
                logger.LogInformation("Задача планировщика ежедневного сбора удалена");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось обновить задачу планировщика ежедневного сбора");
        }
    }
}
