using System.Net;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ItchDB.Tests.Http;

public sealed class ItchHttpClientTests : IDisposable
{
    private const string Url = "https://itch.io/games/top-rated/tag-horror";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "itchdb-tests-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkStatus _status = new();
    private readonly StubHandler _handler = new();
    private readonly ItchHttpClient _client;

    public ItchHttpClientTests()
    {
        var paths = new DataPaths(_root);
        var factory = new FileDbFactory(paths.ConnectionString);
        using (var db = factory.CreateDbContext()) db.Database.EnsureCreated();
        var settings = new SettingsService(factory, new EphemeralDataProtectionProvider());
        settings.SaveAsync(new AppSettingsModel { RequestIntervalMs = 0, RequestJitterMs = 0, CloudflarePauseMinutes = [10, 20] }).GetAwaiter().GetResult();
        _client = new ItchHttpClient(new HttpClient(_handler), new RequestThrottle(settings), _status,
            new CookieStore(paths, NullLogger<CookieStore>.Instance), settings, NullLogger<ItchHttpClient>.Instance);
    }

    [Fact]
    public async Task GetNowAsync_FailsAtOnceOnChallengeWithoutPausingCollection()
    {
        _handler.Challenge = true;

        await Assert.ThrowsAsync<CloudflareBlockedException>(() => _client.GetNowAsync(Url, CancellationToken.None));

        Assert.Equal(1, _handler.Calls);
        Assert.False(_status.IsPaused);
        Assert.Equal(0, _status.ConsecutiveChallenges);
    }

    [Fact]
    public async Task SkipPause_ResetsTheChallengeStreak()
    {
        _handler.Challenge = true;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GetAsync(Url, cts.Token));
        Assert.True(_status.ConsecutiveChallenges > 0);

        _status.SkipPause();

        Assert.False(_status.IsPaused);
        Assert.False(_status.IsBlocked);
        Assert.Equal(0, _status.ConsecutiveChallenges);
    }

    [Fact]
    public async Task GetAsync_ChallengeStartsThePause_AndGetNowAsyncDoesNotSendDuringIt()
    {
        _handler.Challenge = true;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GetAsync(Url, cts.Token));
        Assert.True(_status.IsPaused);
        Assert.Equal(1, _handler.Calls);
        _handler.Challenge = false;

        await Assert.ThrowsAsync<CloudflareBlockedException>(() => _client.GetNowAsync(Url, CancellationToken.None));

        Assert.Equal(1, _handler.Calls);
    }

    [Fact]
    public async Task Http400_PausesForAFewSeconds_WithoutTheCloudflareEscalation()
    {
        _handler.Status = HttpStatusCode.BadRequest;

        var result = await _client.GetAsync(Url, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(1, _handler.Calls);
        Assert.True(_status.IsPaused);
        Assert.Equal(0, _status.ConsecutiveChallenges);
        Assert.Equal(1, _status.ConsecutiveHttp400);
        Assert.True(_status.PausedUntil <= DateTime.UtcNow.AddSeconds(6));
    }

    [Fact]
    public async Task ConnectionFailure_ContinuesWithoutAPause()
    {
        _handler.Fault = new HttpRequestException("Не удалось подключиться к nomnomnami.itch.io");

        var result = await _client.GetAsync(Url, CancellationToken.None);

        Assert.Equal(0, result.StatusCode);
        Assert.False(_status.IsPaused);
        Assert.Equal(0, _status.ConsecutiveChallenges);
        Assert.Equal(1, _status.ConsecutiveConnectionFailures);
    }

    [Fact]
    public void SixthConnectionFailure_PausesForAFewSeconds()
    {
        for (var i = 1; i <= 5; i++)
        {
            var step = _status.NoteConnectionFailure(5);
            Assert.Equal(i, step.Count);
            Assert.False(step.SlowDown);
        }

        var sixth = _status.NoteConnectionFailure(5);

        Assert.Equal(6, sixth.Count);
        Assert.True(sixth.SlowDown);
        Assert.Equal(6, _status.ConsecutiveConnectionFailures);
    }

    [Fact]
    public void SixthConsecutiveHttp400_AsksToResetTheSession()
    {
        for (var i = 1; i <= 5; i++)
        {
            var step = _status.NoteHttp400(5);
            Assert.Equal(i, step.Count);
            Assert.False(step.Reset);
        }

        var sixth = _status.NoteHttp400(5);

        Assert.Equal(6, sixth.Count);
        Assert.True(sixth.Reset);
        Assert.Equal(0, _status.ConsecutiveHttp400);
    }

    [Fact]
    public async Task SuccessAfterSkippedPause_ResetsTheEscalation()
    {
        _handler.Challenge = true;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GetAsync(Url, cts.Token));
        _status.SkipPause();
        _handler.Challenge = false;

        var result = await _client.GetNowAsync(Url, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _status.ConsecutiveChallenges);
        Assert.False(_status.IsPaused);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public bool Challenge { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Fault { get; set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Fault is not null) throw Fault;
            var response = Challenge
                ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<html><title>Just a moment...</title></html>") }
                : new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? "<html>ok</html>" : "") };
            if (Challenge) response.Headers.TryAddWithoutValidation("cf-mitigated", "challenge");
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FileDbFactory(string connectionString) : IDbContextFactory<ItchDbContext>
    {
        public ItchDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ItchDbContext>().UseSqlite(connectionString).Options);
    }
}
