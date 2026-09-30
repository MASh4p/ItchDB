using ItchDB.Core.Domain;

namespace ItchDB.Core.Analytics;

public static class Scores
{
    /// <summary>Mean star rating over games with at least <paramref name="minVotes"/> ratings (C in the weighted rating).</summary>
    public static double GlobalMeanStars(IEnumerable<GameRow> games, int minVotes)
    {
        var list = games.Where(g => g.RatingAvg is not null && g.Ratings >= Math.Max(1, minVotes)).Select(g => g.RatingAvg!.Value).ToList();
        return list.Count > 0 ? list.Average() : 3.8;
    }

    public static void ApplyWeightedRating(IReadOnlyList<GameRow> games, int minVotes)
    {
        var c = GlobalMeanStars(games, minVotes);
        foreach (var g in games)
            g.WeightedRating = g.RatingAvg is { } r && g.Ratings > 0 ? Stats.WeightedRating(g.Ratings, r, minVotes, c) : 0;
    }

    /// <summary>
    /// Hot = (Δratings + a·Δcomments + b·rank gain) · 7 / window days / (age in days + 2)^g.
    /// Deltas come from the snapshot window (up to 7 days) and are scaled to a weekly pace.
    /// </summary>
    public static double Hot(GameRow g, AnalyticsParams p, DateTime now)
    {
        if (g.DeltaRatings is null && g.RankGain is null && g.DeltaComments is null) return 0;
        var numerator = Math.Max(0, g.DeltaRatings ?? 0) + p.HotA * Math.Max(0, g.DeltaComments ?? 0) + p.HotB * Math.Max(0, g.RankGain ?? 0);
        if (numerator <= 0) return 0;
        var weekly = numerator * 7.0 / Math.Max(1, g.DeltaDays);
        return weekly / Math.Pow(g.AgeDays(now) + 2, p.HotG);
    }

    public static void ApplyHot(IEnumerable<GameRow> games, AnalyticsParams p, DateTime now)
    {
        foreach (var g in games) g.Hot = Hot(g, p, now);
    }
}

public sealed record AudienceEstimate(
    double Views,
    double ViewsLow,
    double ViewsHigh,
    double? BrowserPlays,
    double? Downloads,
    bool Calibrated,
    bool BelowDetection);

/// <summary>
/// itch.io hides views and downloads, but ratings are public. Only a small share of visitors rate a game,
/// so views ≈ ratings / rater share; with the API key the shares come from the user's own games.
/// </summary>
public static class Audience
{
    public static AudienceEstimate Estimate(GameRow g, AnalyticsParams p) => Estimate(g.Ratings, g.Platforms, p);

    public static AudienceEstimate Estimate(double ratings, Platforms platforms, AnalyticsParams p)
    {
        var r = Math.Max(0, ratings);
        var calibrated = p.CalibratedViewsPerRating is > 0;
        double views, low, high;
        if (r == 0)
        {
            views = 0;
            low = 0;
            high = calibrated ? p.CalibratedViewsPerRating!.Value : 1 / Math.Max(1e-6, p.RaterShareLow);
        }
        else if (calibrated)
        {
            views = r * p.CalibratedViewsPerRating!.Value;
            low = views * 0.6;
            high = views * 1.6;
        }
        else
        {
            views = r / Math.Max(1e-6, p.RaterShare);
            low = r / Math.Max(1e-6, p.RaterShareHigh);
            high = r / Math.Max(1e-6, p.RaterShareLow);
        }

        double? plays = (platforms & Platforms.Web) != 0 ? views * p.WebPlayShare : null;
        var downloadable = (platforms & (Platforms.Windows | Platforms.MacOS | Platforms.Linux | Platforms.Android)) != 0 || platforms == Platforms.None;
        double? downloads = downloadable
            ? p.CalibratedDownloadsPerRating is > 0 ? r * p.CalibratedDownloadsPerRating.Value : views * p.DownloadShare
            : null;
        return new AudienceEstimate(views, low, high, plays, downloads, calibrated, r == 0);
    }
}

public sealed record ListPosition(string ListKey, int Position, IReadOnlyList<(int Position, GameRow Game)> Neighbors);

public static class Positions
{
    /// <summary>Where a game stands in every crawled "popular" list, with the games right above and below it.</summary>
    public static List<ListPosition> For(AnalyticsDataset d, long gameId, int neighbors = 3)
    {
        var result = new List<ListPosition>();
        foreach (var (key, ids) in d.LatestPositions)
        {
            var idx = Array.IndexOf(ids, gameId);
            if (idx < 0) continue;
            var list = new List<(int, GameRow)>();
            for (var i = Math.Max(0, idx - neighbors); i <= Math.Min(ids.Length - 1, idx + neighbors); i++)
                if (d.GamesById.TryGetValue(ids[i], out var g)) list.Add((i + 1, g));
            result.Add(new ListPosition(key, idx + 1, list));
        }
        return result.OrderBy(r => r.Position).ToList();
    }

    public static string ListName(AnalyticsDataset d, string listKey)
    {
        if (listKey == "popular") return "Популярное (весь itch.io)";
        if (listKey == "new-and-popular") return "Новое и популярное";
        var slash = listKey.IndexOf('/');
        if (slash < 0) return listKey;
        var filter = listKey[(slash + 1)..];
        var prefix = listKey[..slash] switch
        {
            "popular" => "Популярное",
            "new-and-popular" => "Новое и популярное",
            "top-rated" => "Лучшие по оценкам",
            _ => listKey[..slash],
        };
        var names = filter.Split('/').Select(f => d.Tag(f) is { } t ? Segments.DisplayName(t) : Catalog.ItchCatalog.NameFromSlug(f));
        return $"{prefix}: {string.Join(" + ", names)}";
    }
}
