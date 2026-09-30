using ItchDB.Infrastructure.Analytics;

namespace ItchDB.Tests.Analytics;

public class DatasetLoaderTests
{
    [Fact]
    public void DropInconsistentTotals_RemovesTotalsSmallerThanANarrowerFilter()
    {
        var totals = new Dictionary<string, int>
        {
            [""] = 3252,
            ["year-2024"] = 39739,
            ["tag-2d"] = 50000,
            ["tag-2d/year-2024"] = 9000,
            ["free/tag-2d"] = 30000,
        };

        DatasetLoader.DropInconsistentTotals(totals);

        Assert.False(totals.ContainsKey(""));
        Assert.Equal(50000, totals["tag-2d"]);
        Assert.Equal(9000, totals["tag-2d/year-2024"]);
        Assert.Equal(30000, totals["free/tag-2d"]);
        Assert.Equal(39739, totals["year-2024"]);
    }

    [Fact]
    public void DropInconsistentTotals_ToleratesSmallDriftBetweenDays()
    {
        var totals = new Dictionary<string, int> { ["tag-2d"] = 10000, ["tag-2d/year-2024"] = 10100 };

        DatasetLoader.DropInconsistentTotals(totals);

        Assert.Equal(2, totals.Count);
    }
}
