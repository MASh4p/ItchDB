using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Http;
using ItchDB.Infrastructure.Parsing;
using ItchDB.Tests.Fixtures;

namespace ItchDB.Tests.Parsing;

public class CatalogPagesParserTests
{
    [Fact]
    public void TagsPage()
    {
        var page = TagsPageParser.Parse(Fixture.Read("tags_p1.html"));

        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(16, page.TotalPages);
        Assert.Equal(40, page.Entries.Count);

        var twoD = Assert.Single(page.Entries, e => e.Slug == "tag-2d");
        Assert.Equal("2D", twoD.Name);
        Assert.Equal(436_000, twoD.ProjectsCount);
        Assert.Equal(2442, twoD.ThisWeekCount);
        Assert.Contains("tag-no-ai", twoD.RelatedSlugs);
        Assert.Contains("genre-rpg", twoD.RelatedSlugs);
        Assert.DoesNotContain("tag-sprites", twoD.RelatedSlugs);
        Assert.NotNull(twoD.Description);

        Assert.True(page.AllTags.Count > 300);
        Assert.Contains(page.AllTags, t => t.Slug == "tag-3d-platformer" && t.Name == "3D Platformer");
        Assert.DoesNotContain(page.AllTags, t => t.Slug == "tag-16x16");
    }

    [Fact]
    public void ProfilePage()
    {
        var p = ProfileParser.Parse(Fixture.Read("profile_adamgryu.html"));

        Assert.Equal(14_453, p.Followers);
        Assert.Equal(21, p.Following);
        Assert.Equal("adamgryu", p.DisplayName);
        Assert.Equal(new DateTime(2014, 4, 27, 23, 43, 0, DateTimeKind.Utc), p.RegisteredAt);
    }

    [Fact]
    public void PastJamsList()
    {
        var page = JamParsers.ParseList(Fixture.Read("jams_past.html"));

        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(429, page.TotalPages);
        Assert.NotEmpty(page.Jams);

        var gmtk = page.Jams[0];
        Assert.Equal("gmtk-jam-2026", gmtk.Slug);
        Assert.Equal("GMTK Game Jam 2026", gmtk.Title);
        Assert.Equal("Game Maker's Toolkit", gmtk.Hosts);
        Assert.Equal(JamPhase.Ended, gmtk.Phase);
        Assert.Equal(new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc), gmtk.Date);
        Assert.Equal(37_400, gmtk.Joined);
        Assert.Equal(10_500, gmtk.Submissions);
        Assert.True(gmtk.IsRanked);
        Assert.True(gmtk.IsFeatured);
        Assert.Equal(9515, page.Jams[1].Submissions);
    }

    [Fact]
    public void JamPage()
    {
        var jam = JamParsers.ParseJamPage(Fixture.Read("jam_page.html"));

        Assert.Equal(385727, jam.ItchJamId);
        Assert.Equal(new DateTime(2025, 7, 30, 17, 0, 0, DateTimeKind.Utc), jam.StartAt);
        Assert.Equal(new DateTime(2025, 8, 3, 18, 0, 0, DateTimeKind.Utc), jam.EndAt);
        Assert.Equal(new DateTime(2025, 8, 8, 17, 0, 0, DateTimeKind.Utc), jam.VotingEndAt);
        Assert.Equal(9515, jam.Entries);
        Assert.Equal(197_073, jam.Ratings);
    }

    [Fact]
    public void JamEntriesJson()
    {
        var entries = JamParsers.ParseEntries(Fixture.Read("jam_entries.json"));

        Assert.Equal(3, entries.Count);
        var first = entries[0];
        Assert.Equal(3779622, first.GameId);
        Assert.Equal("HOW MANY DUDES? Pre-Alpha Demo", first.Title);
        Assert.Equal(102, first.RatingCount);
        Assert.Equal(47, first.Coolness);
        Assert.Equal(82098, first.AuthorId);
        Assert.Equal(Platforms.Web, first.Platforms);
        Assert.Equal(Platforms.Windows | Platforms.MacOS | Platforms.Web, entries[1].Platforms);
    }

    [Fact]
    public void RssFeed()
    {
        var items = RssParser.Parse(Fixture.Read("rss_toprated.xml"));

        Assert.NotEmpty(items);
        var circus = Assert.Single(items, i => i.Url == "https://garula.itch.io/the-freak-circus");
        Assert.Equal("The Freak Circus", circus.Title);
        Assert.Equal(0, circus.PriceCents);
        Assert.Equal("USD", circus.Currency);
        Assert.Equal("Visual Novel", circus.GenreName);
        Assert.Equal(new DateTime(2025, 6, 10, 2, 36, 13, DateTimeKind.Utc), circus.PubDate);
        Assert.True(circus.Platforms.HasFlag(Platforms.Web));
        Assert.True(circus.Platforms.HasFlag(Platforms.Windows));

        Assert.NotEmpty(RssParser.Parse(Fixture.Read("featured.xml")));
    }

    [Theory]
    [InlineData("<html><head><title>Just a moment...</title></head></html>", true)]
    [InlineData("<div>Performing security verification</div>", true)]
    [InlineData("<html><title>Top rated games - itch.io</title></html>", false)]
    public void CloudflareChallengeBody(string body, bool expected) =>
        Assert.Equal(expected, ItchHttpClient.IsChallengeBody(body));
}
