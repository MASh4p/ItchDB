using ItchDB.Core.Analytics;

namespace ItchDB.Tests.Analytics;

public class SegmentAnalyzerTests
{
    private static readonly AnalyticsParams P = new();

    private static AnalyticsDataset SegmentData(Dictionary<string, int>? totals = null)
    {
        var tag = TestData.Tag(1, "tag-a");
        var games = new[] { 0, 0, 0, 1, 5, 10, 20 }.Select((r, i) => TestData.Game(i + 1, r, [1]));
        return TestData.Dataset(games, [tag], totals);
    }

    [Fact]
    public void Compute_UsesOnlyQualifiedGamesForMedians()
    {
        var d = SegmentData(new() { ["tag-a"] = 100 });
        var analyzer = new SegmentAnalyzer(d, new AnalyticsFilter(false, 4), P, TestData.Now);

        var s = analyzer.Compute(Segments.FromTag(d.Tags[1]));

        Assert.Equal(7, s.Known);
        Assert.Equal(4, s.Rated);
        Assert.Equal(3, s.Qualified);
        Assert.Equal(10, s.Median);
        Assert.Equal(100, s.Denominator);
        Assert.False(s.DenominatorEstimated);
        Assert.Equal(2, s.Successes);
        Assert.Equal(0.02, s.SuccessShare, 10);
        Assert.Equal(96, s.Bin0);
        Assert.Equal(1, s.Bin1To4);
        Assert.Equal(3, s.Bin5To49);
        Assert.True(s.SmallSample);
    }

    [Fact]
    public void Compute_FallsBackToKnownGamesWithoutItchTotal()
    {
        var d = SegmentData();
        var s = new SegmentAnalyzer(d, new AnalyticsFilter(false, 4), P, TestData.Now).Compute(Segments.FromTag(d.Tags[1]));

        Assert.Equal(7, s.Denominator);
        Assert.True(s.DenominatorEstimated);
    }

    [Fact]
    public void Compute_HidesNsfwUnlessEnabled()
    {
        var tag = TestData.Tag(1, "tag-a");
        var safe = TestData.Game(1, 10, [1]);
        var adult = new GameRow { Id = 2, Title = "Adult", Ratings = 50, RatingsKnown = true, IsNsfw = true, TagIds = [1] };
        var d = TestData.Dataset([safe, adult], [tag]);

        var hidden = new SegmentAnalyzer(d, new AnalyticsFilter(false, 4), P, TestData.Now).Compute(Segments.FromTag(tag));
        var shown = new SegmentAnalyzer(d, new AnalyticsFilter(true, 4), P, TestData.Now).Compute(Segments.FromTag(tag));

        Assert.Equal(1, hidden.Known);
        Assert.Equal(2, shown.Known);
    }

    [Fact]
    public void Rank_AssignsQuadrantsByDemandAndCompetition()
    {
        SegmentStats Stat(string key, double demand, int games) => new()
        {
            Segment = new SegmentDef { Key = key, Name = key, Kind = SegmentKind.Tag },
            AdjMedian = demand,
            Denominator = games,
            SuccessLow = demand / 1000,
        };
        var blue = Stat("blue", 100, 10);
        var red = Stat("red", 90, 10_000);
        var quiet = Stat("quiet", 10, 20);
        var trap = Stat("trap", 5, 5_000);

        SegmentAnalyzer.Rank([blue, red, quiet, trap], P);

        Assert.Equal(Quadrant.BlueOcean, blue.Quadrant);
        Assert.Equal(Quadrant.RedOcean, red.Quadrant);
        Assert.Equal(Quadrant.QuietNiche, quiet.Quadrant);
        Assert.Equal(Quadrant.Trap, trap.Quadrant);
        Assert.True(blue.Opportunity > red.Opportunity);
        Assert.True(red.Opportunity > trap.Opportunity);
        Assert.InRange(blue.Opportunity!.Value, 0, 100);
    }

    [Fact]
    public void Rank_SkipsSmallSamples()
    {
        var small = new SegmentStats { Segment = Segments.All, AdjMedian = 1000, Denominator = 1, SmallSample = true };
        var normal = new SegmentStats { Segment = new SegmentDef { Key = "x", Name = "x", Kind = SegmentKind.Tag }, AdjMedian = 5, Denominator = 100 };

        SegmentAnalyzer.Rank([small, normal], P);

        Assert.Null(small.Opportunity);
        Assert.Equal(Quadrant.None, small.Quadrant);
        Assert.NotNull(normal.Opportunity);
    }

    [Fact]
    public void Trend_ComparesShareAmongSuccessfulGames()
    {
        var rising = TestData.Tag(1, "tag-rising");
        var games = new List<GameRow>();
        var id = 1;
        for (var year = 2021; year <= 2024; year++)
        {
            var recent = year >= 2023;
            for (var i = 0; i < 100; i++)
            {
                var tagged = recent ? i < 40 : i < 10;
                games.Add(new GameRow
                {
                    Id = id++, Title = "g", Ratings = 20, RatingsKnown = true, Year = year,
                    TagIds = tagged ? [1] : [],
                });
            }
        }
        var d = TestData.Dataset(games, [rising]);
        var analyzer = new SegmentAnalyzer(d, AnalyticsFilter.Default, P, TestData.Now);

        var s = analyzer.Compute(Segments.FromTag(rising));

        Assert.Equal("2023–2024 против 2021–2022", analyzer.TrendCohorts);
        Assert.Equal(3, s.Trend!.Value, 10);
    }
}
