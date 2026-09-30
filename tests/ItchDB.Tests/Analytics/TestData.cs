using ItchDB.Core.Analytics;
using ItchDB.Core.Domain;

namespace ItchDB.Tests.Analytics;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    public static TagRow Tag(int id, string slug, TagKind kind = TagKind.Tag) => new() { Id = id, Slug = slug, Name = slug, Kind = kind };

    public static GameRow Game(long id, int ratings, int[]? tags = null, int ageDays = 400, Platforms platforms = Platforms.Web,
        int screenshots = 3, int? deltaRatings = null, int deltaDays = 0) => new()
    {
        Id = id,
        Title = $"Game {id}",
        Url = $"https://dev.itch.io/game-{id}",
        Ratings = ratings,
        RatingsKnown = true,
        RatingAvg = ratings > 0 ? 4.2 : null,
        PublishedAt = Now.AddDays(-ageDays),
        Year = Now.AddDays(-ageDays).Year,
        Platforms = platforms,
        Price = PriceModel.Free,
        Screenshots = screenshots,
        HasDetails = true,
        FirstSeenAt = Now.AddDays(-ageDays),
        TagIds = (tags ?? []).Order().ToArray(),
        DeltaRatings = deltaRatings,
        DeltaDays = deltaDays,
    };

    public static AnalyticsDataset Dataset(IEnumerable<GameRow> games, IEnumerable<TagRow> tags, Dictionary<string, int>? totals = null)
    {
        var list = games.ToList();
        var tagMap = tags.ToDictionary(t => t.Id);
        return new AnalyticsDataset
        {
            BuiltAt = Now,
            Games = list,
            GamesById = list.ToDictionary(g => g.Id),
            Tags = tagMap,
            TagsBySlug = tagMap.Values.ToDictionary(t => t.Slug),
            GamesByTag = tagMap.Keys.ToDictionary(id => id, id => list.Where(g => g.HasTag(id)).ToArray()),
            ListingTotals = totals ?? [],
        };
    }
}
