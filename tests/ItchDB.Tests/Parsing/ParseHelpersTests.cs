using ItchDB.Infrastructure.Parsing;

namespace ItchDB.Tests.Parsing;

public class ParseHelpersTests
{
    [Theory]
    [InlineData("1,794", 1794)]
    [InlineData("(4,387 total ratings)", 4387)]
    [InlineData("14.4k", 14_400)]
    [InlineData("516k projects", 516_000)]
    [InlineData("1.2m", 1_200_000)]
    [InlineData("4,540 this week", 4540)]
    [InlineData(" (329,556 results)", 329_556)]
    [InlineData("12 months", 12)]
    public void ParseCount(string text, int expected) => Assert.Equal(expected, ParseHelpers.ParseCount(text));

    [Fact]
    public void ParseCount_ReturnsNullWithoutDigits() => Assert.Null(ParseHelpers.ParseCount("none"));

    [Theory]
    [InlineData("$7.99 USD", 799, "USD")]
    [InlineData("Pay $19.99 or more for this game", 1999, "USD")]
    [InlineData("$1", 100, "USD")]
    [InlineData("€5", 500, "EUR")]
    public void ParseMoney(string text, int cents, string currency)
    {
        var (c, cur) = ParseHelpers.ParseMoney(text);
        Assert.Equal(cents, c);
        Assert.Equal(currency, cur);
    }

    [Fact]
    public void ParseItchDate() =>
        Assert.Equal(new DateTime(2026, 9, 16, 6, 44, 0, DateTimeKind.Utc), ParseHelpers.ParseItchDate("16 September 2026 @ 06:44 UTC"));

    [Theory]
    [InlineData("https://itch.io/games/tag-2d", "tag-2d")]
    [InlineData("https://itch.io/games/made-with-unity", "made-with-unity")]
    [InlineData("https://itch.io/games/lang-pt-BR", "lang-pt-BR")]
    [InlineData("https://store.steampowered.com/app/1", null)]
    public void FilterSlugFromHref(string href, string? expected) => Assert.Equal(expected, ParseHelpers.FilterSlugFromHref(href));
}
