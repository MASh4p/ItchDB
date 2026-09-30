using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Core.Analytics;

public sealed record TagComboResult(
    TagRow A,
    TagRow B,
    int Games,
    int Qualified,
    double Median,
    double MedianA,
    double MedianB,
    double Lift,
    int Successes,
    double SuccessShare);

public static class TagCombos
{
    /// <summary>
    /// Pairs of tags that often appear together. Lift = median ratings of games with both tags divided by
    /// the better of the two single-tag medians: above 1 means the combination works better than either tag alone.
    /// </summary>
    public static List<TagComboResult> Compute(AnalyticsDataset d, AnalyticsFilter f, AnalyticsParams p, int maxTags = 160, int? onlyWithTag = null)
    {
        var minSample = Math.Max(5, p.MinSegmentSample);
        var singles = d.Tags.Values
            .Where(t => (t.Kind is TagKind.Tag or TagKind.Genre && (f.ShowNsfw || !ItchCatalog.NsfwTags.Contains(t.Slug))) || onlyWithTag == t.Id)
            .Select(t => (Tag: t, Games: d.Members(t.Id).Where(f.Includes).ToList()))
            .Where(x => x.Games.Count(g => g.Ratings >= f.MinRatings) >= minSample)
            .OrderByDescending(x => x.Games.Count)
            .Take(maxTags)
            .ToList();
        if (onlyWithTag is { } forced && singles.All(s => s.Tag.Id != forced) && d.Tags.TryGetValue(forced, out var ft))
            singles.Add((ft, d.Members(forced).Where(f.Includes).ToList()));

        var ids = singles.Select(s => s.Tag.Id).ToHashSet();
        var singleMedian = singles.ToDictionary(s => s.Tag.Id, s =>
        {
            var q = Stats.QualifiedRatings(s.Games, f.MinRatings);
            return q.Length > 0 ? Stats.Median(q) : double.NaN;
        });

        var pairs = new Dictionary<(int, int), List<int>>();
        foreach (var g in d.Games)
        {
            if (!f.Includes(g) || g.TagIds.Length < 2) continue;
            var tags = g.TagIds.Where(ids.Contains).ToArray();
            for (var i = 0; i < tags.Length; i++)
            {
                for (var j = i + 1; j < tags.Length; j++)
                {
                    if (onlyWithTag is { } only && tags[i] != only && tags[j] != only) continue;
                    var key = (tags[i], tags[j]);
                    if (!pairs.TryGetValue(key, out var list)) pairs[key] = list = [];
                    list.Add(g.Ratings);
                }
            }
        }

        var result = new List<TagComboResult>();
        foreach (var ((a, b), ratings) in pairs)
        {
            var qualified = Stats.Sorted(ratings.Where(r => r >= f.MinRatings).Select(r => (double)r));
            if (qualified.Length < minSample) continue;
            var median = Stats.Median(qualified);
            var ma = singleMedian[a];
            var mb = singleMedian[b];
            var best = Math.Max(double.IsNaN(ma) ? 0 : ma, double.IsNaN(mb) ? 0 : mb);
            var successes = ratings.Count(r => r >= p.SuccessThreshold);
            result.Add(new TagComboResult(d.Tags[a], d.Tags[b], ratings.Count, qualified.Length, median, ma, mb,
                best > 0 ? median / best : double.NaN, successes, (double)successes / ratings.Count));
        }
        return result;
    }
}
