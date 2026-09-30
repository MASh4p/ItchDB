namespace ItchDB.Core.Domain;

public class GameSnapshot
{
    public long GameId { get; set; }
    public DateOnly Day { get; set; }
    public int? RatingCount { get; set; }
    public double? RatingAvg { get; set; }
    public int? CommentsCount { get; set; }
}

/// <summary>Position of a game in a "popular" list on a given day.</summary>
public class TagPositionSnapshot
{
    public DateOnly Day { get; set; }
    /// <summary>Normalized list key: "popular", "popular/tag-2d", "new-and-popular", ...</summary>
    public string ListKey { get; set; } = "";
    public int Position { get; set; }
    public long GameId { get; set; }
}

/// <summary>The "(N results)" counter of a browse page for a filter combination.</summary>
public class ListingTotalSnapshot
{
    public DateOnly Day { get; set; }
    /// <summary>Sorted filter slugs joined by '/', empty string for the whole catalog.</summary>
    public string FilterKey { get; set; } = "";
    public int Results { get; set; }
}

public class TagCatalogSnapshot
{
    public DateOnly Day { get; set; }
    public int TagId { get; set; }
    public int ProjectsCount { get; set; }
    public int ThisWeekCount { get; set; }
}

public class SegmentStatsSnapshot
{
    public DateOnly Day { get; set; }
    public string SegmentKey { get; set; } = "";
    public int? TotalItch { get; set; }
    public int KnownGames { get; set; }
    public int RatedGames { get; set; }
    public double MedianRatings { get; set; }
    public double P75Ratings { get; set; }
    public double P90Ratings { get; set; }
    public long SumRatings { get; set; }
    public double? SuccessShare { get; set; }
    public double? Opportunity { get; set; }
}

public class DeveloperSnapshot
{
    public long DeveloperId { get; set; }
    public DateOnly Day { get; set; }
    public int Followers { get; set; }
}
