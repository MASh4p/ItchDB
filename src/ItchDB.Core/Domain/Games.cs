namespace ItchDB.Core.Domain;

[Flags]
public enum Platforms
{
    None = 0,
    Web = 1,
    Windows = 2,
    MacOS = 4,
    Linux = 8,
    Android = 16,
    IOS = 32,
}

public enum PriceModel
{
    Unknown = 0,
    Free = 1,
    NameYourPrice = 2,
    Paid = 3,
}

public enum GameStatus
{
    Unknown = 0,
    Released = 1,
    InDevelopment = 2,
    Prototype = 3,
    OnHold = 4,
    Canceled = 5,
}

public enum AiUsage
{
    Unknown = 0,
    NoAi = 1,
    UsesAi = 2,
}

public enum TagKind
{
    Tag = 0,
    Genre = 1,
    Engine = 2,
    Duration = 3,
    Language = 4,
    Input = 5,
    Accessibility = 6,
    Multiplayer = 7,
    Platform = 8,
    Other = 9,
}

[Flags]
public enum TagSource : byte
{
    None = 0,
    Listing = 1,
    GamePage = 2,
    Heuristic = 4,
}

public class Game
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ShortText { get; set; }
    public string? CoverUrl { get; set; }
    public bool CoverIsGif { get; set; }

    public long? DeveloperId { get; set; }
    public Developer? Developer { get; set; }
    public string? AuthorName { get; set; }

    /// <summary>Filter slug of the primary genre: "genre-action", "tag-card-game", ...</summary>
    public string? Genre { get; set; }

    public PriceModel PriceModel { get; set; }
    /// <summary>Minimum price in cents; 0 for free and name-your-price with $0 minimum.</summary>
    public int? PriceCents { get; set; }
    public string? Currency { get; set; }
    public int? SalePercent { get; set; }
    public Platforms Platforms { get; set; }

    public int? RatingCount { get; set; }
    public double? RatingAvg { get; set; }
    public int? CommentsCount { get; set; }

    public DateTime? PublishedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public int? PublishedYear { get; set; }

    public GameStatus Status { get; set; }
    public AiUsage Ai { get; set; }
    public int ScreenshotsCount { get; set; }
    public bool IsNsfw { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime? FeaturedAt { get; set; }
    public bool IsJamEntry { get; set; }
    public int? PopularRank { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? RatingsCheckedAt { get; set; }
    public DateTime? DetailsFetchedAt { get; set; }
    public int DetailsFailures { get; set; }
    public bool IsRemoved { get; set; }

    public List<GameTag> Tags { get; set; } = [];
}

public class Developer
{
    public long Id { get; set; }
    /// <summary>Profile slug, the subdomain of the creator page (user.itch.io).</summary>
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Url { get; set; }
    public int? Followers { get; set; }
    public int? Following { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime? ProfileFetchedAt { get; set; }
    public int ProfileFailures { get; set; }

    public List<Game> Games { get; set; } = [];
}

public class Tag
{
    public int Id { get; set; }
    /// <summary>itch filter slug: "tag-2d", "genre-action", "made-with-godot", "duration-minutes", ...</summary>
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public TagKind Kind { get; set; }
    public int? ProjectsCount { get; set; }
    public int? ThisWeekCount { get; set; }
    public string? Description { get; set; }
    /// <summary>Tags that /tags lists as frequent companions, space separated slugs.</summary>
    public string? RelatedTags { get; set; }
    public DateTime? CatalogSeenAt { get; set; }

    public List<GameTag> Games { get; set; } = [];
}

public class GameTag
{
    public long GameId { get; set; }
    public int TagId { get; set; }
    public TagSource Source { get; set; }

    public Game? Game { get; set; }
    public Tag? Tag { get; set; }
}
