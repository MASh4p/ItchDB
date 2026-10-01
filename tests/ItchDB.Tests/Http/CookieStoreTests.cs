using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ItchDB.Tests.Http;

public sealed class CookieStoreTests : IDisposable
{
    private const string Clearance = "cf-clearance-value-long-enough";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "itchdb-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CookieStore _store;

    public CookieStoreTests() => _store = new CookieStore(new DataPaths(_root), NullLogger<CookieStore>.Instance);

    [Fact]
    public void Parse_BareValue_IsCfClearance()
    {
        var pairs = CookieStore.Parse("  " + Clearance + " \n");
        Assert.Equal([("cf_clearance", Clearance)], pairs);
    }

    [Fact]
    public void Parse_CookieHeader_KeepsClearanceAndBotCookie()
    {
        var pairs = CookieStore.Parse($"itchio_token=abc; cf_clearance={Clearance}; __cf_bm=bm-value");
        Assert.Contains(("cf_clearance", Clearance), pairs);
        Assert.Contains(("__cf_bm", "bm-value"), pairs);
    }

    [Fact]
    public void Parse_RejectsConsoleNoise()
    {
        var ex = Assert.Throws<FormatException>(() => CookieStore.Parse("Failed to load resource: net::ERR_BLOCKED_BY_CLIENT"));
        Assert.Contains("cf_clearance", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedClearance_IsSentToItchAndGameSubdomains()
    {
        _store.ImportCfClearance($"cf_clearance={Clearance}; __cf_bm=bm-value");

        var root = _store.Container.GetCookieHeader(new Uri("https://itch.io/games"));
        var sub = _store.Container.GetCookieHeader(new Uri("https://ninja-muffin24.itch.io/funkin"));

        Assert.Contains($"cf_clearance={Clearance}", root, StringComparison.Ordinal);
        Assert.Contains($"cf_clearance={Clearance}", sub, StringComparison.Ordinal);
        Assert.Contains("__cf_bm=bm-value", root, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
