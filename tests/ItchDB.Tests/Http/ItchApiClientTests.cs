using ItchDB.Infrastructure.Http;

namespace ItchDB.Tests.Http;

public sealed class ItchApiClientTests
{
    [Fact]
    public void ParseMyGames_ReadsStatsOfEachGame()
    {
        const string json = """
            {"games":[
              {"id":123,"title":"Tiny Game","url":"https://me.itch.io/tiny","views_count":5400,"downloads_count":310,"purchases_count":4,"published":true},
              {"id":456,"title":"Draft","url":"https://me.itch.io/draft","views_count":0,"downloads_count":0,"purchases_count":0,"published":false}
            ]}
            """;

        var games = ItchApiClient.ParseMyGames(json);

        Assert.Equal(2, games.Count);
        Assert.Equal(new ApiGame(123, "Tiny Game", "https://me.itch.io/tiny", 5400, 310, 4, true), games[0]);
        Assert.False(games[1].Published);
    }

    [Fact]
    public void ParseMyGames_ThrowsOnApiErrors()
    {
        var ex = Assert.Throws<ItchApiException>(() => ItchApiClient.ParseMyGames("""{"errors":["invalid key"]}"""));
        Assert.Contains("invalid key", ex.Message);
    }

    [Fact]
    public void ParseMyGames_EmptyWhenNoGames()
    {
        Assert.Empty(ItchApiClient.ParseMyGames("""{"games":[]}"""));
    }
}
