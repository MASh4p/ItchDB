using ItchDB.Core.Analytics;
using ItchDB.Core.Domain;

namespace ItchDB.Tests.Analytics;

public class ScoresTests
{
    [Fact]
    public void Hot_ScalesDeltasToWeeklyPace()
    {
        var p = new AnalyticsParams();
        var week = TestData.Game(1, 50, ageDays: 20, deltaRatings: 10, deltaDays: 7);
        var day = TestData.Game(2, 50, ageDays: 20, deltaRatings: 10, deltaDays: 1);

        Assert.Equal(7 * Scores.Hot(week, p, TestData.Now), Scores.Hot(day, p, TestData.Now), 8);
    }

    [Fact]
    public void Hot_PrefersYoungerGamesWithSameGrowth()
    {
        var p = new AnalyticsParams();
        var young = TestData.Game(1, 50, ageDays: 5, deltaRatings: 10, deltaDays: 7);
        var old = TestData.Game(2, 50, ageDays: 900, deltaRatings: 10, deltaDays: 7);

        Assert.True(Scores.Hot(young, p, TestData.Now) > Scores.Hot(old, p, TestData.Now));
        Assert.Equal(0, Scores.Hot(TestData.Game(3, 50), p, TestData.Now));
    }

    [Fact]
    public void Audience_UsesRaterShareRange()
    {
        var a = Audience.Estimate(100, Platforms.Web, new AnalyticsParams());

        Assert.Equal(10_000, a.Views, 6);
        Assert.Equal(5_000, a.ViewsLow, 6);
        Assert.Equal(20_000, a.ViewsHigh, 6);
        Assert.Equal(3_700, a.BrowserPlays!.Value, 6);
        Assert.Null(a.Downloads);
        Assert.False(a.Calibrated);
    }

    [Fact]
    public void Audience_PrefersCalibrationFromOwnGames()
    {
        var p = new AnalyticsParams { CalibratedViewsPerRating = 50, CalibratedDownloadsPerRating = 6 };

        var a = Audience.Estimate(100, Platforms.Windows, p);

        Assert.True(a.Calibrated);
        Assert.Equal(5_000, a.Views, 6);
        Assert.Equal(600, a.Downloads!.Value, 6);
        Assert.Null(a.BrowserPlays);
    }

    [Fact]
    public void WeightedRating_RanksManyVotesAboveFewPerfectOnes()
    {
        var few = new GameRow { Id = 1, Ratings = 3, RatingsKnown = true, RatingAvg = 5 };
        var many = new GameRow { Id = 2, Ratings = 900, RatingsKnown = true, RatingAvg = 4.7 };
        var baseline = Enumerable.Range(10, 50).Select(i => new GameRow { Id = i, Ratings = 40, RatingsKnown = true, RatingAvg = 3.8 });

        Scores.ApplyWeightedRating([few, many, .. baseline], 20);

        Assert.True(many.WeightedRating > few.WeightedRating);
    }
}
