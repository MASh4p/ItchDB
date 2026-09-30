using ItchDB.Core.Analytics;

namespace ItchDB.Tests.Analytics;

public class ConceptCheckerTests
{
    private static AnalyticsDataset Data()
    {
        var games = new List<GameRow>();
        for (var i = 0; i < 40; i++) games.Add(TestData.Game(1000 + i, 20 + i, [1, 2]));
        for (var i = 0; i < 40; i++) games.Add(TestData.Game(2000 + i, 5 + i, [1]));
        for (var i = 0; i < 40; i++) games.Add(TestData.Game(3000 + i, i, [3]));
        return TestData.Dataset(games, [TestData.Tag(1, "tag-horror"), TestData.Tag(2, "tag-pixel-art"), TestData.Tag(3, "tag-cozy")]);
    }

    [Fact]
    public void Check_MatchesAllTagsAndUsesLiveTotal()
    {
        var r = ConceptChecker.Check(Data(), AnalyticsFilter.Default, new AnalyticsParams(), new ConceptQuery([1, 2]), TestData.Now, liveTotal: 500);

        Assert.Equal(40, r.Similar);
        Assert.False(r.Relaxed);
        Assert.Equal(40, r.Qualified);
        Assert.Equal(39.5, r.Median, 10);
        Assert.Equal(500, r.Denominator);
        Assert.False(r.DenominatorEstimated);
        var ten = Assert.Single(r.Probabilities, x => x.Threshold == 10);
        Assert.Equal(40, ten.Successes);
        Assert.Equal(0.08, ten.Share, 10);
        Assert.Equal(2, r.TagStats.Count);
    }

    [Fact]
    public void Check_RelaxesToAllButOneTagWhenTooFewMatches()
    {
        var r = ConceptChecker.Check(Data(), AnalyticsFilter.Default, new AnalyticsParams(), new ConceptQuery([1, 2, 3]), TestData.Now);

        Assert.True(r.Relaxed);
        Assert.Equal(40, r.Similar);
        Assert.True(r.DenominatorEstimated);
        Assert.Contains(r.Conclusions, c => c.Title == "Совпадение не по всем тегам");
    }

    [Fact]
    public void Check_WithoutTagsDescribesWholeCatalog()
    {
        var r = ConceptChecker.Check(Data(), AnalyticsFilter.Default, new AnalyticsParams(), new ConceptQuery([]), TestData.Now);

        Assert.Equal(120, r.Similar);
        Assert.Equal(r.GlobalMedian, r.Median, 10);
    }
}
