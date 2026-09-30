using ItchDB.Core.Catalog;

namespace ItchDB.Core.Analytics;

public sealed record YearPoint(int Year, int? TotalItch, int Known, int Rated, int Qualified, double Median, int Successes, double? SuccessShare);

public sealed record MonthPoint(DateOnly Month, int Releases, int Qualified, double Median, int Successes);

public sealed record ShareRow(string Key, string Name, IReadOnlyList<(int Year, double Share, int Count)> Points);

public static class Trends
{
    /// <summary>Publication-year cohorts. Older cohorts had more time to collect ratings, so compare shapes, not levels.</summary>
    public static List<YearPoint> ByYear(AnalyticsDataset d, AnalyticsFilter f, AnalyticsParams p, IEnumerable<GameRow>? games = null, string? filterPrefix = null)
    {
        var source = (games ?? d.Games).Where(f.Includes).Where(g => g.Year is not null).GroupBy(g => g.Year!.Value).ToDictionary(x => x.Key, x => x.ToList());
        var result = new List<YearPoint>();
        for (var y = ItchCatalog.FirstYear; y <= DateTime.UtcNow.Year; y++)
        {
            var list = source.GetValueOrDefault(y) ?? [];
            var key = filterPrefix is null ? $"year-{y}" : string.Join('/', new[] { filterPrefix, $"year-{y}" }.OrderBy(s => s, StringComparer.Ordinal));
            var total = d.Total(key);
            if (list.Count == 0 && total is null) continue;
            var q = Stats.QualifiedRatings(list, f.MinRatings);
            var successes = list.Count(g => g.Ratings >= p.SuccessThreshold);
            var rated = list.Count(g => g.Ratings > 0);
            var denominator = total is { } t && t >= rated ? t : (int?)null;
            result.Add(new YearPoint(y, total, list.Count, rated, q.Length, q.Length > 0 ? Stats.Median(q) : double.NaN,
                successes, denominator is > 0 ? (double)successes / denominator.Value : null));
        }
        return result;
    }

    /// <summary>Monthly releases among games with a known publication date (game pages and RSS).</summary>
    public static List<MonthPoint> ByMonth(IEnumerable<GameRow> games, AnalyticsFilter f, AnalyticsParams p, int months, DateTime now)
    {
        var from = new DateOnly(now.Year, now.Month, 1).AddMonths(-(months - 1));
        var groups = games.Where(f.Includes).Where(g => g.PublishedAt is not null)
            .GroupBy(g => new DateOnly(g.PublishedAt!.Value.Year, g.PublishedAt.Value.Month, 1))
            .Where(x => x.Key >= from)
            .ToDictionary(x => x.Key, x => x.ToList());
        var result = new List<MonthPoint>();
        for (var m = from; m <= new DateOnly(now.Year, now.Month, 1); m = m.AddMonths(1))
        {
            var list = groups.GetValueOrDefault(m) ?? [];
            var q = Stats.QualifiedRatings(list, f.MinRatings);
            result.Add(new MonthPoint(m, list.Count, q.Length, q.Length > 0 ? Stats.Median(q) : double.NaN, list.Count(g => g.Ratings >= p.SuccessThreshold)));
        }
        return result;
    }

    /// <summary>Share of each segment among successful games of every publication year.</summary>
    public static List<ShareRow> SuccessShareByYear(AnalyticsDataset d, AnalyticsFilter f, AnalyticsParams p, IEnumerable<SegmentDef> segments, int fromYear)
    {
        var successful = d.Games.Where(g => f.Includes(g) && g.Ratings >= p.SuccessThreshold && g.Year >= fromYear).ToList();
        var byYear = successful.GroupBy(g => g.Year!.Value).ToDictionary(x => x.Key, x => x.Count());
        var rows = new List<ShareRow>();
        foreach (var s in segments)
        {
            var members = Segments.Members(d, s, f).Where(g => g.Ratings >= p.SuccessThreshold && g.Year >= fromYear)
                .GroupBy(g => g.Year!.Value).ToDictionary(x => x.Key, x => x.Count());
            var points = byYear.OrderBy(kv => kv.Key)
                .Select(kv => (kv.Key, kv.Value > 0 ? (double)members.GetValueOrDefault(kv.Key) / kv.Value : 0, members.GetValueOrDefault(kv.Key)))
                .ToList();
            rows.Add(new ShareRow(s.Key, s.Name, points));
        }
        return rows;
    }

    /// <summary>Weekly growth of a tag on itch.io from /tags snapshots: projects added per day between the first and last snapshot.</summary>
    public static double? ProjectsPerDay(TagRow t)
    {
        if (t.History.Count < 2) return null;
        var first = t.History[0];
        var last = t.History[^1];
        var days = last.Day.DayNumber - first.Day.DayNumber;
        return days > 0 ? (double)(last.Projects - first.Projects) / days : null;
    }
}
