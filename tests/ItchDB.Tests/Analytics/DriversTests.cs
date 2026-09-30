using ItchDB.Core.Analytics;
using ItchDB.Core.Domain;

namespace ItchDB.Tests.Analytics;

public class DriversTests
{
    /// <summary>ln(1 + ratings) = 1 + 0.5·ln(1 + age) + ln 3·[tag-x] + noise; Windows has no effect.</summary>
    private static AnalyticsDataset Synthetic()
    {
        var rng = new Random(42);
        var games = new List<GameRow>();
        for (var i = 0; i < 800; i++)
        {
            var age = 30 + rng.Next(2000);
            var hasX = i % 3 == 0;
            var windows = rng.Next(2) == 0;
            var noise = 0.3 * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            var y = 1 + 0.5 * Math.Log(1 + age) + (hasX ? Math.Log(3) : 0) + noise;
            var ratings = (int)Math.Round(Math.Exp(y) - 1);
            games.Add(TestData.Game(i + 1, ratings, hasX ? [1] : [], age,
                windows ? Platforms.Web | Platforms.Windows : Platforms.Web, screenshots: 0));
        }
        return TestData.Dataset(games, [TestData.Tag(1, "tag-x")]);
    }

    [Fact]
    public void Analyze_RecoversMultiplierWithAgeControl()
    {
        var report = Drivers.Analyze(Synthetic(), AnalyticsFilter.Default, new AnalyticsParams(), TestData.Now);

        var x = Assert.Single(report.Results, r => r.Feature.Key == "tag-x");
        Assert.InRange(x.Multiplier!.Value, 2.6, 3.4);
        Assert.True(x.Significant);
        Assert.True(x.MultiplierLow < 3 && x.MultiplierHigh > 3);

        var windows = Assert.Single(report.Results, r => r.Feature.Key == "platform-windows");
        Assert.InRange(windows.Multiplier!.Value, 0.87, 1.15);

        Assert.InRange(report.AgeDoublingMultiplier!.Value, 1.3, 1.55);
        Assert.True(report.RSquared > 0.5);
        Assert.Equal(800, report.Sample);
    }

    [Fact]
    public void Analyze_SkipsFeaturesWithoutContrast()
    {
        var report = Drivers.Analyze(Synthetic(), AnalyticsFilter.Default, new AnalyticsParams(), TestData.Now);

        Assert.DoesNotContain(report.Results, r => r.Feature.Key == "platform-web");
    }

    [Fact]
    public void Analyze_ReturnsEmptyForTinySamples()
    {
        var d = TestData.Dataset([TestData.Game(1, 5), TestData.Game(2, 7)], []);

        var report = Drivers.Analyze(d, AnalyticsFilter.Default, new AnalyticsParams(), TestData.Now);

        Assert.Empty(report.Results);
        Assert.Equal(2, report.Sample);
    }
}
