using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Parsing;
using ItchDB.Tests.Fixtures;

namespace ItchDB.Tests.Parsing;

public class ListingParserTests
{
    [Fact]
    public void TopRatedJson_ParsesCardsWithRatings()
    {
        var page = ListingParser.ParseJson(Fixture.Read("listing_toprated_p2.json"));

        Assert.Equal(36, page.NumItems);
        Assert.Equal(2, page.Page);
        Assert.Equal(36, page.Cards.Count);
        Assert.True(page.HasNextPage);
        Assert.All(page.Cards, c => Assert.True(c.HasRating));

        var ds = Assert.Single(page.Cards, c => c.Id == 3245484);
        Assert.Equal("Dragonsweeper", ds.Title);
        Assert.Equal("https://danielben.itch.io/dragonsweeper", ds.Url);
        Assert.Equal("Daniel Benmergui", ds.AuthorName);
        Assert.Equal(32229, ds.AuthorId);
        Assert.Equal("Puzzle", ds.GenreName);
        Assert.Equal(Platforms.Web, ds.Platforms);
        Assert.Equal(4.86, ds.RatingAvg);
        Assert.Equal(2177, ds.RatingCount);
        Assert.Null(ds.PriceCents);
        Assert.Equal("A roguelike minesweeper adventure", ds.ShortText);
        Assert.StartsWith("https://img.itch.zone/", ds.CoverUrl);
    }

    [Fact]
    public void TopRatedJson_ParsesPrice()
    {
        var page = ListingParser.ParseJson(Fixture.Read("listing_toprated_p2.json"));
        var paid = page.Cards.First(c => c.PriceCents == 1999);
        Assert.Equal("USD", paid.Currency);
    }

    [Fact]
    public void BrowseHtml_ParsesTotalResultsAndCards()
    {
        var page = ListingParser.ParseHtml(Fixture.Read("listing_toprated_page1.html"));

        Assert.Equal(329_556, page.TotalResults);
        Assert.Equal(36, page.Cards.Count);
        Assert.True(page.HasNextPage);
        Assert.All(page.Cards, c => Assert.True(c.RatingCount > 0));
    }

    [Fact]
    public void PopularJson_ParsesSaleTagAndMissingRatings()
    {
        var page = ListingParser.ParseJson(Fixture.Read("listing_popular_horror_p2.json"));

        Assert.Equal(36, page.Cards.Count);
        Assert.All(page.Cards, c => Assert.False(c.HasRating));
        var sale = Assert.Single(page.Cards, c => c.SalePercent == 75);
        Assert.Equal(4401531, sale.Id);
        Assert.Equal(124, sale.PriceCents);
        Assert.Contains(page.Cards, c => c.PriceCents == 295 && c.SalePercent is null);
    }

    [Fact]
    public void NewestJson_ParsesDesktopPlatformsAndGenre()
    {
        var page = ListingParser.ParseJson(Fixture.Read("listing_newest_p1.json"));

        var first = page.Cards[0];
        Assert.Equal(5079449, first.Id);
        Assert.Equal("Shooter", first.GenreName);
        Assert.Equal(Platforms.Web, first.Platforms);
        Assert.Equal(19443799, first.AuthorId);
        Assert.Contains(page.Cards, c => c.Platforms.HasFlag(Platforms.Windows));
        Assert.Contains(page.Cards, c => c.PriceCents == 100);
    }

    [Fact]
    public void ParseRequested_ReadsHtmlWhenJsonWasExpected()
    {
        const string html = """
            <div class="game_cell" data-game_id="42"><a class="title" href="https://author.itch.io/game">Name</a></div>
            """;

        var page = ListingParser.ParseRequested(html, json: true);

        var card = Assert.Single(page.Cards);
        Assert.Equal(42, card.Id);
        Assert.Equal("Name", card.Title);
    }
}
