namespace ItchDB.Core.Domain;

public class TrackedGame
{
    public int Id { get; set; }
    public long? GameId { get; set; }
    public string Url { get; set; } = "";
    public bool IsOwn { get; set; }
    public string? Note { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    /// <summary>How many pages of each "popular" tag list to scan when looking for the game.</summary>
    public int MaxPages { get; set; } = 5;
}

/// <summary>Stats of the user's own games from the official itch.io API.</summary>
public class OwnGameSnapshot
{
    public long GameId { get; set; }
    public DateOnly Day { get; set; }
    public string? Title { get; set; }
    public int Views { get; set; }
    public int Downloads { get; set; }
    public int Purchases { get; set; }
}

public class FeaturedEvent
{
    public int Id { get; set; }
    public long? GameId { get; set; }
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime FeaturedAt { get; set; }
    public DateTime SeenAt { get; set; }
}

public enum CrawlRunStatus
{
    Running = 0,
    Completed = 1,
    Stopped = 2,
    Failed = 3,
    Interrupted = 4,
}

public class CrawlCursor
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public class CrawlRun
{
    public int Id { get; set; }
    public string Profile { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public CrawlRunStatus Status { get; set; }
    public int Requests { get; set; }
    public int Errors { get; set; }
    public int ParseErrors { get; set; }
    public int GamesUpserted { get; set; }
    public string? Message { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
