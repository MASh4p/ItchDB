namespace ItchDB.Infrastructure.Settings;

/// <summary>User-editable settings persisted as JSON in the AppSettings table.</summary>
public sealed class AppSettingsModel
{
    // Network
    /// <summary>Gap between catalog requests: tags, lists and similar pages.</summary>
    public int RequestIntervalMs { get; set; } = 1300;
    /// <summary>Gap between individual game pages. Slower than lists, so a burst does not earn HTTP 400.</summary>
    public int GamePageIntervalMs { get; set; } = 12_000;
    /// <summary>How many tag and list pages may be in flight together. Game pages stay one at a time.</summary>
    public int ParallelRequests { get; set; } = 4;
    public int RequestJitterMs { get; set; } = 900;
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    public int[] CloudflarePauseMinutes { get; set; } = [10, 20, 40];

    // Scheduler
    public bool AutoDailyEnabled { get; set; } = true;
    public int DailyIntervalHours { get; set; } = 24;

    // Global filters
    public int DefaultMinRatings { get; set; } = 4;
    public bool DefaultShowNsfw { get; set; }
    /// <summary>Success and competition use the itch.io list counter, including games with zero ratings.</summary>
    public bool DefaultIncludeUnrated { get; set; } = true;

    // Analytics
    public int SuccessThreshold { get; set; } = 10;
    public double ShrinkK { get; set; } = 20;
    public int MinSegmentSample { get; set; } = 15;
    public double WeightDemand { get; set; } = 0.35;
    public double WeightSuccess { get; set; } = 0.25;
    public double WeightCompetition { get; set; } = 0.25;
    public double WeightTrend { get; set; } = 0.15;
    public double HotA { get; set; } = 0.5;
    public double HotB { get; set; } = 0.25;
    public double HotG { get; set; } = 0.25;
    public int BayesMinVotes { get; set; } = 20;

    // Audience estimate
    public double RaterShare { get; set; } = 0.01;
    public double RaterShareLow { get; set; } = 0.005;
    public double RaterShareHigh { get; set; } = 0.02;
    public double WebPlayShare { get; set; } = 0.37;
    public double DownloadShare { get; set; } = 0.08;
    public double? CalibratedViewsPerRating { get; set; }
    public double? CalibratedDownloadsPerRating { get; set; }

    // Crawl budgets
    public int QuickTopTags { get; set; } = 25;
    public int QuickPagesPerList { get; set; } = 12;
    public int QuickDetails { get; set; } = 120;
    public int DailyPopularTags { get; set; } = 60;
    public int DailyPopularPages { get; set; } = 2;
    public int DailyTopRatedPages { get; set; } = 5;
    public int DailyNewestMaxPages { get; set; } = 30;
    public int DailyDetails { get; set; } = 600;
    public int DailyProfiles { get; set; } = 120;
    public int DailyBacklogPages { get; set; } = 800;
    /// <summary>0 (and the previous default 6000) means the deep catalog runs until its lists are finished.</summary>
    public int DeepListPages { get; set; }
    public int DeepMinTagProjects { get; set; } = 300;
    public int DeepRefreshDays { get; set; } = 30;
    public int DeepDetails { get; set; } = 3000;
    public int DeepProfiles { get; set; } = 600;
    /// <summary>0 means the game-page profile keeps going until every game without a page has one.</summary>
    public int GamePagesPerRun { get; set; }
    public int JamEntriesPerRun { get; set; } = 6;
}
