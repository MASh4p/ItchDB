namespace ItchDB.Core.Analytics;

public enum Quadrant
{
    None,
    BlueOcean,
    RedOcean,
    QuietNiche,
    Trap,
}

public sealed class SegmentStats
{
    public required SegmentDef Segment { get; init; }

    /// <summary>Games of the segment in the local database.</summary>
    public int Known { get; init; }

    /// <summary>Games of the segment on itch.io ("N results" of the filter or the /tags counter).</summary>
    public int? TotalItch { get; init; }

    /// <summary>Denominator of shares: TotalItch when known, otherwise the known games (biased toward rated games).</summary>
    public int Denominator { get; init; }
    public bool DenominatorEstimated { get; init; }

    public int Rated { get; init; }
    public int Qualified { get; init; }
    public double Median { get; init; } = double.NaN;
    public double P25 { get; init; } = double.NaN;
    public double P75 { get; init; } = double.NaN;
    public double P90 { get; init; } = double.NaN;
    public double Mean { get; init; } = double.NaN;
    public double AdjMedian { get; init; }
    public double? MedianStars { get; init; }
    public long SumRatings { get; init; }
    public double? MedianComments { get; init; }

    public int Successes { get; init; }
    public double SuccessShare { get; init; }
    public double SuccessLow { get; init; }
    public double SuccessHigh { get; init; }

    public int Bin0 { get; init; }
    public int Bin1To4 { get; init; }
    public int Bin5To49 { get; init; }
    public int Bin50Plus { get; init; }

    public int? ThisWeek { get; init; }
    public double? EntryThreshold { get; init; }
    public int EntrySample { get; init; }

    /// <summary>Relative change of the segment's share among successful games: recent cohorts vs two years earlier.</summary>
    public double? Trend { get; init; }
    public int RecentSuccesses { get; init; }
    public int EarlierSuccesses { get; init; }

    public bool RatedComplete { get; init; }
    public bool SmallSample { get; init; }

    public double? WebShare { get; init; }
    public double? PaidShare { get; init; }
    public double? MedianPaidPriceCents { get; init; }

    // Set by SegmentAnalyzer.Rank.
    public double DemandPct { get; set; } = 0.5;
    public double SuccessPct { get; set; } = 0.5;
    public double CompetitionPct { get; set; } = 0.5;
    public double TrendPct { get; set; } = 0.5;
    public double? Opportunity { get; set; }
    public Quadrant Quadrant { get; set; }

    public string Key => Segment.Key;
    public string Name => Segment.Name;
}

/// <summary>
/// Computes per-segment statistics under the global filters. Medians use only games with at least MinRatings ratings:
/// most itch.io games have no ratings at all, so the plain median would be 0 everywhere.
/// </summary>
public sealed class SegmentAnalyzer
{
    private readonly AnalyticsDataset _d;
    private readonly AnalyticsFilter _f;
    private readonly AnalyticsParams _p;
    private readonly int _recentFrom;
    private readonly int _recentTo;
    private readonly int _earlierFrom;
    private readonly int _earlierTo;

    public SegmentAnalyzer(AnalyticsDataset d, AnalyticsFilter f, AnalyticsParams p, DateTime now)
    {
        _d = d;
        _f = f;
        _p = p;
        Now = now;

        var included = d.Games.Where(f.Includes).ToList();
        var successesByYear = included.Where(g => g.Year is not null && g.Ratings >= p.SuccessThreshold)
            .GroupBy(g => g.Year!.Value).ToDictionary(x => x.Key, x => x.Count());
        var latest = successesByYear.Where(kv => kv.Value >= 100).Select(kv => kv.Key).DefaultIfEmpty(now.Year - 1).Max();
        _recentTo = latest;
        _recentFrom = latest - 1;
        _earlierTo = latest - 2;
        _earlierFrom = latest - 3;

        var qualified = Stats.QualifiedRatings(included, f.MinRatings);
        GlobalMedian = qualified.Length > 0 ? Stats.Median(qualified) : 0;
        GlobalQualified = qualified.Length;
        AllRecentSuccesses = included.Count(IsRecentSuccess);
        AllEarlierSuccesses = included.Count(IsEarlierSuccess);
    }

    /// <summary>Cohorts compared by the trend, e.g. "2023–2024 против 2021–2022".</summary>
    public string TrendCohorts => $"{_recentFrom}–{_recentTo} против {_earlierFrom}–{_earlierTo}";

    private bool IsRecentSuccess(GameRow g) => g.Ratings >= _p.SuccessThreshold && g.Year >= _recentFrom && g.Year <= _recentTo;

    private bool IsEarlierSuccess(GameRow g) => g.Ratings >= _p.SuccessThreshold && g.Year >= _earlierFrom && g.Year <= _earlierTo;

    public DateTime Now { get; }
    public double GlobalMedian { get; }
    public int GlobalQualified { get; }
    public int AllRecentSuccesses { get; }
    public int AllEarlierSuccesses { get; }
    public AnalyticsFilter Filter => _f;
    public AnalyticsParams Params => _p;

    public SegmentStats Compute(SegmentDef seg) => Compute(seg, Segments.Members(_d, seg, _f).ToList());

    public SegmentStats Compute(SegmentDef seg, IReadOnlyList<GameRow> games)
    {
        var min = _f.MinRatings;
        var qualifiedGames = games.Where(g => g.Ratings >= min).ToList();
        var q = Stats.Sorted(qualifiedGames.Select(g => (double)g.Ratings));
        var stars = Stats.Sorted(qualifiedGames.Where(g => g.RatingAvg is not null).Select(g => g.RatingAvg!.Value));
        var comments = Stats.Sorted(qualifiedGames.Where(g => g.Comments is not null).Select(g => (double)g.Comments!.Value));

        TagRow? tag = seg.TagId is { } tid && _d.Tags.TryGetValue(tid, out var t) ? t : null;
        int? totalItch = seg.FilterKey is { } fk ? _d.Total(fk) : null;
        totalItch ??= tag?.TotalItch;
        if (seg.Kind == SegmentKind.All) totalItch ??= _d.CatalogTotal;

        var rated = games.Count(g => g.Ratings > 0);
        var denominator = totalItch is { } ti && ti >= rated ? ti : games.Count;
        var successes = games.Count(g => g.Ratings >= _p.SuccessThreshold);
        var (low, high) = Stats.Wilson(successes, denominator);

        var recent = games.Count(IsRecentSuccess);
        var earlier = games.Count(IsEarlierSuccess);
        double? trend = null;
        if (recent + earlier >= 10 && AllRecentSuccesses > 0 && AllEarlierSuccesses > 0 && earlier > 0)
            trend = ((double)recent / AllRecentSuccesses) / ((double)earlier / AllEarlierSuccesses) - 1;

        double? entry = null;
        var entrySample = 0;
        if (tag is { PopularPage1.Length: > 0 })
        {
            var page = tag.PopularPage1.Select(id => _d.GamesById.TryGetValue(id, out var g) ? g : null)
                .Where(g => g is { RatingsKnown: true }).Select(g => (double)g!.Ratings).ToList();
            if (page.Count >= 5)
            {
                entry = Stats.Median(Stats.Sorted(page));
                entrySample = page.Count;
            }
        }

        var paid = games.Where(g => g.Price == Domain.PriceModel.Paid && g.PriceCents > 0).Select(g => (double)g.PriceCents!.Value);
        var paidSorted = Stats.Sorted(paid);
        var priced = games.Count(g => g.Price != Domain.PriceModel.Unknown);
        var withPlatforms = games.Count(g => g.Platforms != Domain.Platforms.None);

        return new SegmentStats
        {
            Segment = seg,
            Known = games.Count,
            TotalItch = totalItch,
            Denominator = denominator,
            DenominatorEstimated = totalItch is null,
            Rated = rated,
            Qualified = q.Length,
            Median = q.Length > 0 ? Stats.Median(q) : double.NaN,
            P25 = q.Length > 0 ? Stats.Quantile(q, 0.25) : double.NaN,
            P75 = q.Length > 0 ? Stats.Quantile(q, 0.75) : double.NaN,
            P90 = q.Length > 0 ? Stats.Quantile(q, 0.90) : double.NaN,
            Mean = q.Length > 0 ? q.Average() : double.NaN,
            AdjMedian = Stats.Shrink(q.Length > 0 ? Stats.Median(q) : double.NaN, q.Length, GlobalMedian, _p.ShrinkK),
            MedianStars = stars.Length > 0 ? Stats.Median(stars) : null,
            MedianComments = comments.Length > 0 ? Stats.Median(comments) : null,
            SumRatings = games.Sum(g => (long)g.Ratings),
            Successes = successes,
            SuccessShare = denominator > 0 ? (double)successes / denominator : 0,
            SuccessLow = low,
            SuccessHigh = high,
            Bin0 = Math.Max(0, denominator - rated),
            Bin1To4 = games.Count(g => g.Ratings is >= 1 and <= 4),
            Bin5To49 = games.Count(g => g.Ratings is >= 5 and <= 49),
            Bin50Plus = games.Count(g => g.Ratings >= 50),
            ThisWeek = tag?.ThisWeekCount,
            EntryThreshold = entry,
            EntrySample = entrySample,
            Trend = trend,
            RecentSuccesses = recent,
            EarlierSuccesses = earlier,
            RatedComplete = tag?.RatedComplete ?? false,
            SmallSample = q.Length < _p.MinSegmentSample,
            WebShare = withPlatforms > 0 ? (double)games.Count(g => (g.Platforms & Domain.Platforms.Web) != 0) / withPlatforms : null,
            PaidShare = priced > 0 ? (double)games.Count(g => g.Price == Domain.PriceModel.Paid) / priced : null,
            MedianPaidPriceCents = paidSorted.Length > 0 ? Stats.Median(paidSorted) : null,
        };
    }

    public List<SegmentStats> ComputeMany(IEnumerable<SegmentDef> segments, bool rank = true)
    {
        var list = segments.Select(Compute).ToList();
        if (rank) Rank(list, _p);
        return list;
    }

    /// <summary>
    /// Opportunity 0..100 = weighted percentiles among segments with enough data:
    /// demand (smoothed median ratings), success (Wilson lower bound of P(≥X)), low competition (fewer games), trend.
    /// </summary>
    public static void Rank(IReadOnlyList<SegmentStats> list, AnalyticsParams p)
    {
        var eligible = list.Where(s => !s.SmallSample).ToList();
        if (eligible.Count == 0) return;
        var demand = Stats.Sorted(eligible.Select(s => s.AdjMedian));
        var success = Stats.Sorted(eligible.Select(s => s.SuccessLow));
        var competition = Stats.Sorted(eligible.Select(s => Math.Log(1 + s.Denominator)));
        var trends = Stats.Sorted(eligible.Where(s => s.Trend is not null).Select(s => s.Trend!.Value));
        var wsum = p.WeightDemand + p.WeightSuccess + p.WeightCompetition + p.WeightTrend;
        if (wsum <= 0) wsum = 1;

        foreach (var s in eligible)
        {
            s.DemandPct = Stats.PercentRank(demand, s.AdjMedian);
            s.SuccessPct = Stats.PercentRank(success, s.SuccessLow);
            s.CompetitionPct = Stats.PercentRank(competition, Math.Log(1 + s.Denominator));
            s.TrendPct = s.Trend is { } t && trends.Length >= 3 ? Stats.PercentRank(trends, t) : 0.5;
            s.Opportunity = 100 * (p.WeightDemand * s.DemandPct + p.WeightSuccess * s.SuccessPct
                                   + p.WeightCompetition * (1 - s.CompetitionPct) + p.WeightTrend * s.TrendPct) / wsum;
            s.Quadrant = (s.DemandPct >= 0.5, s.CompetitionPct >= 0.5) switch
            {
                (true, false) => Quadrant.BlueOcean,
                (true, true) => Quadrant.RedOcean,
                (false, false) => Quadrant.QuietNiche,
                _ => Quadrant.Trap,
            };
        }
    }

    public static string QuadrantName(Quadrant q) => q switch
    {
        Quadrant.BlueOcean => "Голубой океан",
        Quadrant.RedOcean => "Красный океан",
        Quadrant.QuietNiche => "Тихая ниша",
        Quadrant.Trap => "Ловушка",
        _ => "мало данных",
    };

    public static string QuadrantHint(Quadrant q) => q switch
    {
        Quadrant.BlueOcean => "Спрос выше медианы, игр меньше медианы — лучшие условия для входа.",
        Quadrant.RedOcean => "Спрос высокий, но и игр очень много: нужна сильная игра или заметный крючок.",
        Quadrant.QuietNiche => "Игр мало и спрос скромный: легко выделиться, но потолок аудитории ниже.",
        Quadrant.Trap => "Игр много, а типичная игра получает мало оценок — сложнее всего выстрелить.",
        _ => "Меньше минимальной выборки игр с оценками — выводы ненадёжны.",
    };
}
