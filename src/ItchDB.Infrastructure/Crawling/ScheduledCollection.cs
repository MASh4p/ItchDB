namespace ItchDB.Infrastructure.Crawling;

/// <summary>One-shot launch from the OS scheduler: collect if the daily update is due, then leave.</summary>
public static class ScheduledCollection
{
    public const string Argument = "--daily";

    public static bool IsDailyLaunch(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// An empty database gets the first collection. A database that already has games waits until the usual interval.
    /// </summary>
    public static bool ShouldCollect(bool autoDailyEnabled, bool hasGames, DateTime? nextDue, DateTime utcNow)
    {
        if (!autoDailyEnabled) return false;
        if (!hasGames) return true;
        return nextDue is null || nextDue <= utcNow;
    }

    public static CrawlProfile ProfileFor(bool hasGames) =>
        hasGames ? CrawlProfile.Daily : CrawlProfile.QuickStart;
}

/// <summary>Set from the process arguments before the host is built.</summary>
public sealed class ScheduledCollectionMode
{
    public ScheduledCollectionMode(bool exitAfterRun) => ExitAfterRun = exitAfterRun;

    public bool ExitAfterRun { get; }
}
