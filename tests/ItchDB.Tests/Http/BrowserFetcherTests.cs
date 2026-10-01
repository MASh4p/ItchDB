using ItchDB.Infrastructure.Http;

namespace ItchDB.Tests.Http;

public sealed class BrowserFetcherTests
{
    [Fact]
    public void FindBrowser_DoesNotThrow()
    {
        var path = BrowserFetcher.FindBrowser();
        if (path is not null) Assert.True(File.Exists(path));
    }
}
