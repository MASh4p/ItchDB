using ItchDB.Infrastructure.Crawling;

namespace ItchDB.Tests.Crawling;

public class ScheduledCollectionTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 15, 49, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public void ShouldCollect_FollowsTheSwitchAndAnEmptyDatabase(bool enabled, bool hasGames, bool expected)
    {
        Assert.Equal(expected, ScheduledCollection.ShouldCollect(enabled, hasGames, nextDue: null, Now));
    }

    [Fact]
    public void ShouldCollect_RunsWhenTheIntervalHasElapsed()
    {
        Assert.True(ScheduledCollection.ShouldCollect(true, true, Now.AddMinutes(-1), Now));
        Assert.True(ScheduledCollection.ShouldCollect(true, true, Now, Now));
    }

    [Fact]
    public void ShouldCollect_WaitsWhenTheNextUpdateIsStillAhead()
    {
        Assert.False(ScheduledCollection.ShouldCollect(true, true, Now.AddHours(2), Now));
    }

    [Fact]
    public void ShouldCollect_RunsADatabaseThatHasNeverBeenScheduled()
    {
        Assert.True(ScheduledCollection.ShouldCollect(true, hasGames: true, nextDue: null, Now));
    }

    [Theory]
    [MemberData(nameof(LaunchArgs))]
    public void IsDailyLaunch_ReadsTheFlag(string[] args, bool expected)
    {
        Assert.Equal(expected, ScheduledCollection.IsDailyLaunch(args));
    }

    public static TheoryData<string[], bool> LaunchArgs { get; } = new()
    {
        { ["--daily"], true },
        { ["--proxy", "http://127.0.0.1:10809", "--daily"], true },
        { ["--DAILY"], true },
        { ["--urls", "http://localhost:5257"], false },
    };

    [Fact]
    public void ProfileFor_UsesQuickStartUntilTheDatabaseHasGames()
    {
        Assert.Equal(CrawlProfile.QuickStart, ScheduledCollection.ProfileFor(false));
        Assert.Equal(CrawlProfile.Daily, ScheduledCollection.ProfileFor(true));
    }

    [Fact]
    public void NextRun_IsTheFollowingFourAmAfterThatHourHasPassed()
    {
        var evening = new DateTime(2026, 10, 23, 20, 49, 0);
        var morning = new DateTime(2026, 10, 23, 3, 0, 0);
        var exactly = new DateTime(2026, 10, 23, 4, 0, 0);

        Assert.Equal(new DateTime(2026, 10, 24, 4, 0, 0), WindowsDailyTask.NextRun(evening));
        Assert.Equal(new DateTime(2026, 10, 23, 4, 0, 0), WindowsDailyTask.NextRun(morning));
        Assert.Equal(new DateTime(2026, 10, 24, 4, 0, 0), WindowsDailyTask.NextRun(exactly));
    }

    [Fact]
    public void BuildPowerShell_UsesDotnetRunWhenTheProjectFileIsPresent()
    {
        var script = WindowsDailyTask.BuildPowerShell(
            @"C:\Apps\ItchDB",
            @"C:\Apps\ItchDB\ItchDB.Web.exe",
            @"C:\Apps\ItchDB\ItchDB.Web.csproj");

        Assert.Contains("--daily", script);
        Assert.Contains("--project", script);
        Assert.Contains("ItchDB.Web.csproj", script);
        Assert.Contains("http://localhost:5257", script);
        Assert.Contains("-WindowStyle Hidden", script);
        Assert.Contains("exit $p.ExitCode", script);
        Assert.DoesNotContain("ItchDB.Web.exe", script);
    }

    [Fact]
    public void BuildPowerShell_LaunchesTheAppDirectlyWhenItIsPublished()
    {
        var script = WindowsDailyTask.BuildPowerShell(@"C:\Apps\ItchDB", @"C:\Apps\ItchDB\ItchDB.Web.exe");

        Assert.Contains(@"C:\Apps\ItchDB\ItchDB.Web.exe", script);
        Assert.Contains("@('--daily')", script);
        Assert.DoesNotContain("--project", script);
    }

    [Fact]
    public void BuildTaskXml_SchedulesADailyInteractiveTask()
    {
        var xml = WindowsDailyTask.BuildTaskXml(@"C:\Repos\ItchDB\src\ItchDB.Web\data\daily-launch.ps1", new DateTime(2026, 10, 24, 4, 0, 0));

        Assert.Contains("2026-10-24T04:00:00", xml);
        Assert.Contains("<DaysInterval>1</DaysInterval>", xml);
        Assert.Contains("<StartWhenAvailable>true</StartWhenAvailable>", xml);
        Assert.Contains("daily-launch.ps1", xml);
        Assert.Contains("PT18H", xml);
    }
}
