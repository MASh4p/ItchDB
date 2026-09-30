using ItchDB.Core.Domain;

namespace ItchDB.Infrastructure.Parsing;

public sealed class ParsedGameCard
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string? ShortText { get; init; }
    public string? CoverUrl { get; init; }
    public bool CoverIsGif { get; init; }
    public long? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorUrl { get; init; }
    /// <summary>Genre label as shown on the card ("Puzzle", "Role Playing").</summary>
    public string? GenreName { get; init; }
    public int? PriceCents { get; init; }
    public string? Currency { get; init; }
    public int? SalePercent { get; init; }
    public Platforms Platforms { get; init; }
    public bool HasRating { get; init; }
    public double? RatingAvg { get; init; }
    public int? RatingCount { get; init; }
}

public sealed class ListingPage
{
    public List<ParsedGameCard> Cards { get; init; } = [];
    public int? TotalResults { get; init; }
    public int? Page { get; init; }
    public int NumItems { get; init; }
    public bool? HasNextPage { get; init; }
}

public sealed class RssGameItem
{
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string? ImageUrl { get; init; }
    public int? PriceCents { get; init; }
    public string? Currency { get; init; }
    public DateTime? PubDate { get; init; }
    public DateTime? CreateDate { get; init; }
    public DateTime? UpdateDate { get; init; }
    public Platforms Platforms { get; init; }
    public string? GenreName { get; init; }
    public string? Description { get; init; }
}

public sealed record LinkRef(string Slug, string Name);

public sealed class GamePageData
{
    public long? Id { get; set; }
    public string Title { get; set; } = "";
    public string? CoverUrl { get; set; }
    public string? ShortText { get; set; }
    public long? UploaderUserId { get; set; }
    public List<(string Name, string Url)> Authors { get; } = [];
    public List<LinkRef> Genres { get; } = [];
    public List<LinkRef> Tags { get; } = [];
    public List<LinkRef> Engines { get; } = [];
    public List<LinkRef> Durations { get; } = [];
    public List<LinkRef> Languages { get; } = [];
    public List<LinkRef> Inputs { get; } = [];
    public List<LinkRef> Accessibility { get; } = [];
    public List<LinkRef> Multiplayer { get; } = [];
    public Platforms Platforms { get; set; }
    public GameStatus Status { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public double? RatingAvg { get; set; }
    public int? RatingCount { get; set; }
    public int? CommentsCount { get; set; }
    public PriceModel PriceModel { get; set; }
    public int? PriceCents { get; set; }
    public int? ActualPriceCents { get; set; }
    public string? Currency { get; set; }
    public AiUsage Ai { get; set; }
    public bool IsNsfw { get; set; }
    public bool IsFeatured { get; set; }
    public int MentionsCount { get; set; }
    public int ScreenshotsCount { get; set; }
    public List<string> JamSlugs { get; } = [];

    public IEnumerable<LinkRef> AllFacets =>
        Genres.Concat(Tags).Concat(Engines).Concat(Durations).Concat(Languages)
            .Concat(Inputs).Concat(Accessibility).Concat(Multiplayer);
}

public sealed class TagCatalogEntry
{
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    public int? ProjectsCount { get; init; }
    public int? ThisWeekCount { get; init; }
    public string? Description { get; init; }
    public List<string> RelatedSlugs { get; init; } = [];
}

public sealed class TagCatalogPage
{
    public List<TagCatalogEntry> Entries { get; init; } = [];
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    /// <summary>All game tags from the tag picker, slug and display name.</summary>
    public List<LinkRef> AllTags { get; init; } = [];
}

public sealed class ProfileData
{
    public string? DisplayName { get; init; }
    public int? Followers { get; init; }
    public int? Following { get; init; }
    public DateTime? RegisteredAt { get; init; }
    public int CreatorGames { get; init; }
}

public enum JamPhase
{
    Unknown = 0,
    Upcoming = 1,
    Running = 2,
    Voting = 3,
    Ended = 4,
}

public sealed class JamCell
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Hosts { get; init; }
    public string? CoverUrl { get; init; }
    public JamPhase Phase { get; init; }
    public DateTime? Date { get; init; }
    public int? Joined { get; init; }
    public int? Submissions { get; init; }
    public bool IsRanked { get; init; }
    public bool IsFeatured { get; init; }
}

public sealed class JamListPage
{
    public List<JamCell> Jams { get; init; } = [];
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
}

public sealed class JamPageData
{
    public long? ItchJamId { get; init; }
    public string? Title { get; init; }
    public string? Hosts { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public DateTime? VotingEndAt { get; init; }
    public int? Entries { get; init; }
    public int? Ratings { get; init; }
    public int? Joined { get; init; }
}

public sealed class JamEntryData
{
    public long GameId { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? ShortText { get; init; }
    public Platforms Platforms { get; init; }
    public long? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorUrl { get; init; }
    public int? RatingCount { get; init; }
    public double? Coolness { get; init; }
    public DateTime? SubmittedAt { get; init; }
}
