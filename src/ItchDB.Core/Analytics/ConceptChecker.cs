using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Core.Analytics;

public sealed record ConceptQuery(IReadOnlyList<int> TagIds, string? GenreSlug = null, Platforms Platforms = Platforms.None, PriceModel? Price = null)
{
    /// <summary>itch.io filter slugs for the exact "(N results)" of this combination.</summary>
    public IReadOnlyList<string> FilterSlugs(AnalyticsDataset d)
    {
        var list = TagIds.Select(id => d.Tags.TryGetValue(id, out var t) ? t.Slug : null).Where(s => s is not null).Select(s => s!).ToList();
        if (GenreSlug is not null && !list.Contains(GenreSlug)) list.Add(GenreSlug);
        var platforms = ItchCatalog.PlatformFilters.Where(pf => (Platforms & pf.Flag) != 0).ToList();
        if (platforms.Count == 1) list.Add(platforms[0].Slug);
        if (Price == PriceModel.Free) list.Add("free");
        else if (Price == PriceModel.Paid) list.Add("store");
        return list;
    }
}

public sealed record ProbabilityRow(int Threshold, int Successes, int Denominator, double Share, double Low, double High, double? GlobalShare);

public sealed record TagBoost(TagRow Tag, int With, int Without, double MedianWith, double MedianWithout, double Ratio);

public sealed class ConceptResult
{
    public required ConceptQuery Query { get; init; }
    public int Similar { get; init; }
    public int Qualified { get; init; }
    public bool Relaxed { get; init; }
    public int? LiveTotal { get; init; }
    public int Denominator { get; init; }
    public bool DenominatorEstimated { get; init; }
    public double P25 { get; init; } = double.NaN;
    public double Median { get; init; } = double.NaN;
    public double P75 { get; init; } = double.NaN;
    public double P90 { get; init; } = double.NaN;
    public double GlobalMedian { get; init; }
    public List<ProbabilityRow> Probabilities { get; init; } = [];
    public List<GameRow> Competitors { get; init; } = [];
    public List<GameRow> RecentHits { get; init; } = [];
    public List<SegmentStats> TagStats { get; init; } = [];
    public List<TagBoost> Boosters { get; init; } = [];
    public List<TagBoost> Draggers { get; init; } = [];
    public List<Insight> Conclusions { get; set; } = [];
    public AudienceEstimate? MedianAudience { get; init; }
    public AudienceEstimate? P90Audience { get; init; }
    public double[] QualifiedRatings { get; init; } = [];
}

/// <summary>"Проверка идеи": how games with the chosen tags (and optional genre, platform, price) perform.</summary>
public static class ConceptChecker
{
    public static readonly int[] Thresholds = [10, 50, 100];

    public static ConceptResult Check(
        AnalyticsDataset d,
        AnalyticsFilter f,
        AnalyticsParams p,
        ConceptQuery q,
        DateTime now,
        int? liveTotal = null,
        Func<string, SegmentStats?>? rankedStats = null)
    {
        var analyzer = new SegmentAnalyzer(d, f, p, now);
        var genreTag = q.GenreSlug is null ? null : d.Tag(q.GenreSlug);
        bool Base(GameRow g) =>
            f.Includes(g)
            && (q.GenreSlug is null || g.Genre == q.GenreSlug || (genreTag is not null && g.HasTag(genreTag.Id)))
            && (q.Platforms == Platforms.None || (g.Platforms & q.Platforms) != 0)
            && q.Price switch
            {
                PriceModel.Free => g.Price is PriceModel.Free or PriceModel.NameYourPrice,
                PriceModel.Paid => g.Price == PriceModel.Paid,
                PriceModel.NameYourPrice => g.Price == PriceModel.NameYourPrice,
                _ => true,
            };

        var required = q.TagIds.Distinct().Where(d.Tags.ContainsKey).ToList();
        List<GameRow> similar;
        var relaxed = false;
        if (required.Count == 0)
        {
            similar = (genreTag is not null ? d.Members(genreTag.Id) : d.Games).Where(Base).ToList();
        }
        else
        {
            var overlap = new Dictionary<long, int>();
            foreach (var id in required)
                foreach (var g in d.Members(id))
                    overlap[g.Id] = overlap.GetValueOrDefault(g.Id) + 1;
            similar = overlap.Where(kv => kv.Value == required.Count).Select(kv => d.GamesById[kv.Key]).Where(Base).ToList();
            if (required.Count >= 2 && similar.Count(g => g.Ratings >= f.MinRatings) < p.MinSegmentSample)
            {
                similar = overlap.Where(kv => kv.Value >= required.Count - 1).Select(kv => d.GamesById[kv.Key]).Where(Base).ToList();
                relaxed = true;
            }
        }

        var qualified = Stats.QualifiedRatings(similar, f.MinRatings);
        var rated = similar.Count(g => g.Ratings > 0);
        var denominator = !relaxed && liveTotal is { } live && live >= rated ? live : similar.Count;
        var estimated = denominator == similar.Count && (relaxed || liveTotal is null);

        var allIncluded = d.Games.Where(f.Includes).ToList();
        var catalogDen = d.CatalogTotal is { } ct && ct >= allIncluded.Count(g => g.Ratings > 0) ? ct : allIncluded.Count;
        var probabilities = Thresholds.Union([p.SuccessThreshold]).OrderBy(t => t).Select(t =>
        {
            var s = similar.Count(g => g.Ratings >= t);
            var (lo, hi) = Stats.Wilson(s, denominator);
            double? global = catalogDen > 0 ? (double)allIncluded.Count(g => g.Ratings >= t) / catalogDen : null;
            return new ProbabilityRow(t, s, denominator, denominator > 0 ? (double)s / denominator : 0, lo, hi, global);
        }).ToList();

        var tagStats = required.Select(id => d.Tags[id]).Concat(genreTag is null ? [] : [genreTag])
            .Select(t => rankedStats?.Invoke(t.Slug) ?? analyzer.Compute(Segments.FromTag(t)))
            .ToList();

        var (boosters, draggers) = Boosts(d, f, p, similar, required.Concat(genreTag is null ? [] : [genreTag.Id]).ToHashSet());

        var yearAgo = now.AddDays(-365);
        var recent = similar.Where(g => g.PublishedAt >= yearAgo || g.PublishedAt is null && g.Year >= now.Year)
            .OrderByDescending(g => g.Ratings).ThenByDescending(g => g.Hot).Take(8).ToList();

        var result = new ConceptResult
        {
            Query = q,
            Similar = similar.Count,
            Qualified = qualified.Length,
            Relaxed = relaxed,
            LiveTotal = liveTotal,
            Denominator = denominator,
            DenominatorEstimated = estimated,
            P25 = qualified.Length > 0 ? Stats.Quantile(qualified, 0.25) : double.NaN,
            Median = qualified.Length > 0 ? Stats.Median(qualified) : double.NaN,
            P75 = qualified.Length > 0 ? Stats.Quantile(qualified, 0.75) : double.NaN,
            P90 = qualified.Length > 0 ? Stats.Quantile(qualified, 0.90) : double.NaN,
            GlobalMedian = analyzer.GlobalMedian,
            Probabilities = probabilities,
            Competitors = similar.OrderByDescending(g => g.Ratings).ThenByDescending(g => g.WeightedRating).Take(12).ToList(),
            RecentHits = recent,
            TagStats = tagStats,
            Boosters = boosters,
            Draggers = draggers,
            MedianAudience = qualified.Length > 0 ? Audience.Estimate(Stats.Median(qualified), q.Platforms, p) : null,
            P90Audience = qualified.Length > 0 ? Audience.Estimate(Stats.Quantile(qualified, 0.9), q.Platforms, p) : null,
            QualifiedRatings = qualified,
        };
        result.Conclusions = Conclude(result, p, f);
        return result;
    }

    private static (List<TagBoost> Up, List<TagBoost> Down) Boosts(AnalyticsDataset d, AnalyticsFilter f, AnalyticsParams p, List<GameRow> similar, HashSet<int> exclude)
    {
        var qualifiedGames = similar.Where(g => g.Ratings >= f.MinRatings).ToList();
        var minGroup = Math.Max(5, p.MinSegmentSample / 2);
        if (qualifiedGames.Count < 2 * minGroup) return ([], []);
        var counts = new Dictionary<int, int>();
        foreach (var g in qualifiedGames)
            foreach (var t in g.TagIds)
                if (!exclude.Contains(t)) counts[t] = counts.GetValueOrDefault(t) + 1;

        var boosts = new List<TagBoost>();
        foreach (var (tagId, n) in counts)
        {
            if (n < minGroup || qualifiedGames.Count - n < minGroup || !d.Tags.TryGetValue(tagId, out var tag)) continue;
            if (tag.Kind is TagKind.Language or TagKind.Platform or TagKind.Other) continue;
            if (!f.ShowNsfw && ItchCatalog.NsfwTags.Contains(tag.Slug)) continue;
            var with = Stats.Sorted(qualifiedGames.Where(g => g.HasTag(tagId)).Select(g => (double)g.Ratings));
            var without = Stats.Sorted(qualifiedGames.Where(g => !g.HasTag(tagId)).Select(g => (double)g.Ratings));
            var mw = Stats.Median(with);
            var mo = Stats.Median(without);
            if (mo <= 0) continue;
            boosts.Add(new TagBoost(tag, with.Length, without.Length, mw, mo, mw / mo));
        }
        return (boosts.Where(b => b.Ratio >= 1.15).OrderByDescending(b => b.Ratio).Take(8).ToList(),
                boosts.Where(b => b.Ratio <= 0.87).OrderBy(b => b.Ratio).Take(5).ToList());
    }

    private static List<Insight> Conclude(ConceptResult r, AnalyticsParams p, AnalyticsFilter f)
    {
        var list = new List<Insight>();
        if (r.Qualified < p.MinSegmentSample)
        {
            list.Add(new Insight("Мало данных",
                $"Похожих игр с ≥ {f.MinRatings} оценками всего {r.Qualified}. Выводы ориентировочные: уберите один из тегов или дождитесь глубокого сбора.",
                InsightTone.Warn));
        }
        if (r.Relaxed)
        {
            list.Add(new Insight("Совпадение не по всем тегам",
                "Игр со всеми выбранными тегами слишком мало, поэтому учтены игры, где совпадают все теги, кроме одного.", InsightTone.Info));
        }

        if (!double.IsNaN(r.Median) && r.GlobalMedian > 0)
        {
            var ratio = r.Median / r.GlobalMedian;
            var tone = ratio >= 1.25 ? InsightTone.Good : ratio <= 0.8 ? InsightTone.Warn : InsightTone.Info;
            var verdict = ratio >= 1.25 ? "получают больше оценок, чем типичная игра itch.io"
                : ratio <= 0.8 ? "получают меньше оценок, чем типичная игра itch.io"
                : "получают примерно столько же оценок, сколько типичная игра itch.io";
            list.Add(new Insight("Спрос", $"Похожие игры {verdict}: медиана {RuFormat.N(r.Median)} против {RuFormat.N(r.GlobalMedian)} ({RuFormat.X(ratio)}). " +
                $"Четверть похожих игр набирает больше {RuFormat.N(r.P75)}, лучшие 10% — больше {RuFormat.N(r.P90)}.", tone));
        }

        var main = r.Probabilities.FirstOrDefault(x => x.Threshold == p.SuccessThreshold);
        if (main is { GlobalShare: > 0 } && main.Denominator > 0)
        {
            var ratio = main.Share / main.GlobalShare!.Value;
            list.Add(new Insight("Шанс успеха",
                $"{p.SuccessThreshold}+ оценок набирают {RuFormat.Pct(main.Share, 1)} похожих игр (95%: {RuFormat.Pct(main.Low, 1)}–{RuFormat.Pct(main.High, 1)}) " +
                $"против {RuFormat.Pct(main.GlobalShare.Value, 1)} по всему itch.io ({RuFormat.X(ratio)})" +
                (r.DenominatorEstimated ? ". Знаменатель оценён по известным играм, поэтому доля завышена." : "."),
                ratio >= 1.25 ? InsightTone.Good : ratio <= 0.8 ? InsightTone.Warn : InsightTone.Info));
        }

        if (r.LiveTotal is { } live)
        {
            var tone = live < 300 ? InsightTone.Good : live > 5000 ? InsightTone.Warn : InsightTone.Info;
            var text = live < 300 ? "ниша почти свободна — проверьте, что на неё есть спрос"
                : live > 5000 ? "плотная конкуренция, выделяться придётся визуалом и крючком"
                : "умеренная конкуренция";
            list.Add(new Insight("Конкуренция", $"На itch.io {RuFormat.N(live)} игр с такой комбинацией фильтров: {text}.", tone));
        }

        var entry = r.TagStats.Where(s => s.EntryThreshold is not null).OrderByDescending(s => s.EntryThreshold).FirstOrDefault();
        if (entry is not null)
        {
            list.Add(new Insight("Порог входа",
                $"Чтобы попасть на первую страницу «Популярного» по тегу «{entry.Name}», сейчас нужно около {RuFormat.N(entry.EntryThreshold!.Value)} оценок.",
                InsightTone.Info));
        }

        foreach (var s in r.TagStats.Where(s => s.Quadrant is Quadrant.BlueOcean or Quadrant.Trap))
        {
            list.Add(new Insight(SegmentAnalyzer.QuadrantName(s.Quadrant) + $": «{s.Name}»", SegmentAnalyzer.QuadrantHint(s.Quadrant),
                s.Quadrant == Quadrant.BlueOcean ? InsightTone.Good : InsightTone.Warn));
        }
        var rising = r.TagStats.Where(s => s.Trend >= 0.3).ToList();
        if (rising.Count > 0)
            list.Add(new Insight("Тренд", $"Растут: {RuFormat.Join(rising.Select(s => $"«{s.Name}» ({RuFormat.Signed(s.Trend!.Value)})"))} доли среди успешных игр.", InsightTone.Good));
        var falling = r.TagStats.Where(s => s.Trend <= -0.3).ToList();
        if (falling.Count > 0)
            list.Add(new Insight("Тренд", $"Остывают: {RuFormat.Join(falling.Select(s => $"«{s.Name}» ({RuFormat.Signed(s.Trend!.Value)})"))}.", InsightTone.Warn));

        if (r.Boosters.Count > 0)
        {
            list.Add(new Insight("Что добавить",
                $"Среди похожих игр выше медиана у тех, где есть {RuFormat.Join(r.Boosters.Take(3).Select(b => $"«{Segments.DisplayName(b.Tag)}» ({RuFormat.X(b.Ratio)})"))}.",
                InsightTone.Good));
        }

        if (r.MedianAudience is { BelowDetection: false } a)
        {
            list.Add(new Insight("Аудитория типичной похожей игры",
                $"≈ {RuFormat.N(a.Views)} просмотров страницы (диапазон {RuFormat.N(a.ViewsLow)}–{RuFormat.N(a.ViewsHigh)})" +
                (a.Calibrated ? ", по вашим реальным соотношениям." : ", по отраслевым допущениям; точнее — с API-ключом в «Моя игра»."),
                InsightTone.Info));
        }
        return list;
    }
}
