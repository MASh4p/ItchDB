using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Parsing;
using ItchDB.Tests.Fixtures;

namespace ItchDB.Tests.Parsing;

public class GamePageParserTests
{
    [Fact]
    public void PaidDownloadableGame()
    {
        var g = GamePageParser.Parse(Fixture.Read("game_short_hike.html"));

        Assert.Equal(424895, g.Id);
        Assert.Equal("A Short Hike", g.Title);
        Assert.Equal(PriceModel.Paid, g.PriceModel);
        Assert.Equal(799, g.PriceCents);
        Assert.Equal("USD", g.Currency);
        Assert.Equal(4.89, g.RatingAvg);
        Assert.Equal(4387, g.RatingCount);
        Assert.Equal(GameStatus.Released, g.Status);
        Assert.Equal(Platforms.Windows | Platforms.MacOS | Platforms.Linux, g.Platforms);
        Assert.Equal(new DateTime(2026, 9, 16, 6, 44, 0, DateTimeKind.Utc), g.UpdatedAt);
        Assert.Equal(new DateTime(2019, 7, 30, 12, 40, 0, DateTimeKind.Utc), g.ReleaseDate);
        Assert.Null(g.PublishedAt);

        Assert.Equal(["genre-adventure", "genre-platformer"], g.Genres.Select(x => x.Slug));
        Assert.Equal(["made-with-unity"], g.Engines.Select(x => x.Slug));
        Assert.Equal(8, g.Tags.Count);
        Assert.Contains(g.Tags, t => t.Slug == "tag-walking-simulator" && t.Name == "Walking simulator");
        Assert.Equal(["duration-hour"], g.Durations.Select(x => x.Slug));
        Assert.Equal(5, g.Languages.Count);
        Assert.Equal(4, g.Inputs.Count);
        Assert.Equal(2, g.Accessibility.Count);
        Assert.Equal(4, g.MentionsCount);
        Assert.Equal(["palestine-bundle", "game-industry-hardship-fund"], g.JamSlugs);
        Assert.Single(g.Authors);
        Assert.Equal(25793, g.UploaderUserId);
        Assert.Equal(AiUsage.Unknown, g.Ai);
    }

    [Fact]
    public void NameYourPriceBrowserGameWithSeveralAuthors()
    {
        var g = GamePageParser.Parse(Fixture.Read("game_funkin.html"));

        Assert.Equal(792778, g.Id);
        Assert.Equal(PriceModel.NameYourPrice, g.PriceModel);
        Assert.Equal(0, g.PriceCents);
        Assert.Equal(GameStatus.InDevelopment, g.Status);
        Assert.True(g.Platforms.HasFlag(Platforms.Web));
        Assert.True(g.Platforms.HasFlag(Platforms.Windows));
        Assert.Equal(3, g.Authors.Count);
        Assert.Equal(["tag-rhythm"], g.Genres.Select(x => x.Slug));
        Assert.Equal(12405, g.RatingCount);
        Assert.Equal(AiUsage.NoAi, g.Ai);
        Assert.Equal(2, g.Engines.Count);
        Assert.All(g.Engines, e => Assert.StartsWith("made-with-", e.Slug));
    }

    [Fact]
    public void CommentsCountFromPager()
    {
        var g = GamePageParser.Parse(Fixture.Read("game_dragonsweeper.html"));

        Assert.Equal(3245484, g.Id);
        Assert.Equal(1060, g.CommentsCount);
        Assert.Equal(2177, g.RatingCount);
        Assert.Equal(PriceModel.NameYourPrice, g.PriceModel);
        Assert.Null(g.PublishedAt);
        Assert.Equal(AiUsage.NoAi, g.Ai);
        Assert.Equal(Platforms.Web, g.Platforms);
    }

    [Fact]
    public void IdFromAgeGateWhenInitTokenContainsABrace()
    {
        var html = """
            <html><head></head><body>
            <h1 class="game_title">Academy</h1>
            <form action="https://itch.io/content-warning" method="POST">
              <input value="446379" type="hidden" name="game_id"/>
            </form>
            <script>init_ViewGame('#view_game_1', {"game":{"hit_url":"https://author.itch.io/game/rh/token}tail","type":1,"id":446379}});</script>
            </body></html>
            """;

        Assert.Equal(446379, GamePageParser.Parse(html).Id);
    }

    [Fact]
    public void IdFromAgeGateInputAlone()
    {
        var html = """
            <html><body>
            <form action="https://itch.io/content-warning">
              <input type="hidden" name="game_id" value="3792001"/>
            </form>
            </body></html>
            """;

        Assert.Equal(3792001, GamePageParser.Parse(html).Id);
    }

    [Fact]
    public void FreshReleaseWithAiDisclosure()
    {
        var g = GamePageParser.Parse(Fixture.Read("game_new_release.html"));

        Assert.Equal(5079449, g.Id);
        Assert.NotNull(g.PublishedAt);
        Assert.Equal(AiUsage.UsesAi, g.Ai);
        Assert.Equal(PriceModel.Free, g.PriceModel);
        Assert.Equal(Platforms.Web, g.Platforms);
        Assert.Null(g.RatingCount);
        Assert.Equal(["genre-shooter"], g.Genres.Select(x => x.Slug));
        Assert.Equal(9, g.Tags.Count);
        Assert.Equal(5, g.ScreenshotsCount);
    }
}
