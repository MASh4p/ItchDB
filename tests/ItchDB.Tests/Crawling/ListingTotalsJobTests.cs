using ItchDB.Infrastructure.Crawling;

namespace ItchDB.Tests.Crawling;

public class ListingTotalsJobTests
{
    [Theory]
    [InlineData(186, 380, 566)]
    [InlineData(718, 65, 783)]
    [InlineData(656, 338, 994)]
    [InlineData(66, 726, 792)]
    [InlineData(2252, 2251, 2252)]
    [InlineData(null, 396, 396)]
    [InlineData(3515, null, 3515)]
    public void CombineBrowseTotals_AddsTheHiddenAdultSlice(int? plain, int? adult, int expected) =>
        Assert.Equal(expected, ListingTotalsJob.CombineBrowseTotals(plain, adult));

    [Fact]
    public void AdultListMayBeLarger_WhenThePlainCountMissesRatedGamesAlreadyStored()
    {
        Assert.True(ListingTotalsJob.AdultListMayBeLarger(148, 282));
        Assert.True(ListingTotalsJob.AdultListMayBeLarger(null, 299));
        Assert.False(ListingTotalsJob.AdultListMayBeLarger(2252, 986));
        Assert.False(ListingTotalsJob.AdultListMayBeLarger(100, 0));
    }
}
