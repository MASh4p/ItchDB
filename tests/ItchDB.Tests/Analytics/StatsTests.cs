using ItchDB.Core.Analytics;

namespace ItchDB.Tests.Analytics;

public class StatsTests
{
    [Fact]
    public void Quantile_InterpolatesBetweenOrderStatistics()
    {
        double[] sorted = [1, 2, 3, 4];

        Assert.Equal(2.5, Stats.Median(sorted), 10);
        Assert.Equal(1.75, Stats.Quantile(sorted, 0.25), 10);
        Assert.Equal(3.25, Stats.Quantile(sorted, 0.75), 10);
        Assert.Equal(4, Stats.Quantile(sorted, 1), 10);
        Assert.True(double.IsNaN(Stats.Quantile([], 0.5)));
    }

    [Fact]
    public void QualifiedRatings_DropsGamesBelowMinimum()
    {
        var games = new[] { 0, 0, 3, 4, 10, 25 }.Select((r, i) => TestData.Game(i, r));

        Assert.Equal(new double[] { 4, 10, 25 }, Stats.QualifiedRatings(games, 4));
    }

    [Fact]
    public void Wilson_MatchesReferenceInterval()
    {
        var (low, high) = Stats.Wilson(10, 100);

        Assert.Equal(0.0552, low, 3);
        Assert.Equal(0.1744, high, 3);
        Assert.Equal((0.0, 0.0), Stats.Wilson(0, 0));
    }

    [Fact]
    public void GeometricMean_SpreadsValuesThatShareAMedian()
    {
        Assert.Equal(2, Stats.GeometricMean([2, 2, 2, 2, 2]), 10);
        Assert.True(Stats.GeometricMean([1, 1, 2, 4, 8]) > 2);
        Assert.Equal(2, Stats.Median([1, 1, 2, 4, 8]));
        Assert.True(double.IsNaN(Stats.GeometricMean([0, 0])));
    }

    [Fact]
    public void Shrink_PullsSmallSegmentsTowardGlobal()
    {
        Assert.Equal(3.6, Stats.Shrink(10, 5, 2, 20), 10);
        Assert.Equal(9.68, Stats.Shrink(10, 480, 2, 20), 10);
        Assert.Equal(2, Stats.Shrink(double.NaN, 0, 2, 20));
    }

    [Fact]
    public void WeightedRating_BlendsWithGlobalMean()
    {
        Assert.Equal(11.0 / 3, Stats.WeightedRating(10, 5, 20, 3), 10);
        Assert.Equal(3, Stats.WeightedRating(0, 5, 20, 3));
    }

    [Fact]
    public void Spearman_DetectsMonotonicRelations()
    {
        var x = Enumerable.Range(1, 10).Select(i => (double)i).ToList();

        Assert.Equal(1, Stats.Spearman(x, x.Select(v => v * v * v).ToList()), 10);
        Assert.Equal(-1, Stats.Spearman(x, x.Select(v => -Math.Exp(v)).ToList()), 10);
    }

    [Fact]
    public void Ranks_ShareMeanRankOnTies()
    {
        Assert.Equal(new[] { 1, 2.5, 2.5, 4 }, Stats.Ranks([1, 5, 5, 9]));
    }

    [Fact]
    public void PercentRank_CountsHalfOfTies()
    {
        double[] sorted = [1, 2, 2, 3];

        Assert.Equal(0.5, Stats.PercentRank(sorted, 2), 10);
        Assert.Equal(0, Stats.PercentRank(sorted, 0), 10);
        Assert.Equal(1, Stats.PercentRank(sorted, 5), 10);
    }

    [Fact]
    public void RiskRatio_HasLogConfidenceInterval()
    {
        var rr = Stats.RiskRatio(20, 100, 10, 100);

        Assert.NotNull(rr);
        Assert.Equal(2, rr.Value.Ratio, 10);
        Assert.InRange(rr.Value.Low, 0.9865, 0.9867);
        Assert.InRange(rr.Value.High, 4.0544, 4.0546);
        Assert.Null(Stats.RiskRatio(0, 100, 10, 100));
    }
}
