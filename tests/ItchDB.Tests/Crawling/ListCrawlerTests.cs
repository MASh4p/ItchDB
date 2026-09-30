using ItchDB.Core.Catalog;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Infrastructure.Crawling;
using ItchDB.Infrastructure.Http;

namespace ItchDB.Tests.Crawling;

public class ListCrawlerTests
{
    private static FetchResult Final(string url) => new(200, "", url);

    [Fact]
    public void IsRedirectedAway_DetectsUnknownYearFilter()
    {
        var spec = ListSpec.Of(ListSpec.TopRated, "year-2025");

        Assert.True(ListCrawler.IsRedirectedAway(Final("https://itch.io/games/top-rated?page=2"), spec));
        Assert.False(ListCrawler.IsRedirectedAway(Final("https://itch.io/games/top-rated/year-2025?page=2&format=json"), spec));
    }

    [Fact]
    public void IsRedirectedAway_IgnoresFilterOrderAndCase()
    {
        var spec = ListSpec.Of(ListSpec.TopRated, "free", "genre-action");

        Assert.False(ListCrawler.IsRedirectedAway(Final("https://itch.io/games/top-rated/Genre-Action/free"), spec));
        Assert.True(ListCrawler.IsRedirectedAway(Final("https://itch.io/games/top-rated/free"), spec));
    }

    [Fact]
    public void SplitSpec_GoesPriceThenGenreThenPlatform()
    {
        var root = ListSpec.Of(ListSpec.TopRated, "tag-2d");

        var byPrice = ListCrawler.SplitSpec(root);
        Assert.Equal(new[] { "top-rated/free/tag-2d", "top-rated/store/tag-2d" }, byPrice.Select(s => s.Key));

        var byGenre = ListCrawler.SplitSpec(byPrice[0]);
        Assert.Equal(ItchCatalog.Genres.Count, byGenre.Count);
        Assert.All(byGenre, s => Assert.True(s.HasGenreFilter && s.HasPriceFilter));

        var byPlatform = ListCrawler.SplitSpec(byGenre[0]);
        Assert.NotEmpty(byPlatform);
        Assert.All(byPlatform, s => Assert.True(s.HasPlatformFilter));
        Assert.DoesNotContain(byPlatform, s => s.Key.Contains("platform-ios"));

        Assert.Empty(ListCrawler.SplitSpec(byPlatform[0]));
    }

    [Fact]
    public void SplitSpec_NeverSplitsByYear()
    {
        var parts = ListCrawler.SplitSpec(ListSpec.Of(ListSpec.TopRated));

        Assert.All(parts, s => Assert.Null(s.Year));
    }

    [Fact]
    public void IsRatedComplete_RequiresEveryPartitionOfTruncatedList()
    {
        var root = ListSpec.Of(ListSpec.TopRated, "tag-2d");
        var at = DateTime.UtcNow;
        var cursors = new Dictionary<string, ListCursor>
        {
            ["list:top-rated/tag-2d"] = new(200, ListStatus.Truncated, 20_000, at),
            ["list:top-rated/free/tag-2d"] = new(150, ListStatus.Complete, 6_000, at),
            ["list:top-rated/store/tag-2d"] = new(80, ListStatus.InProgress, 3_000, at),
        };

        Assert.False(DatasetLoader.IsRatedComplete(root, cursors));

        cursors["list:top-rated/store/tag-2d"] = new(90, ListStatus.Complete, 3_000, at);
        Assert.True(DatasetLoader.IsRatedComplete(root, cursors));
        Assert.False(DatasetLoader.IsRatedComplete(ListSpec.Of(ListSpec.TopRated, "tag-3d"), cursors));
    }
}
