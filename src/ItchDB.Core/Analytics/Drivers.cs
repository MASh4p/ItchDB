using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using MathNet.Numerics.LinearAlgebra;

namespace ItchDB.Core.Analytics;

public sealed record DriverFeature(string Key, string Name, string Group, Func<GameRow, bool> Has);

public sealed class DriverResult
{
    public required DriverFeature Feature { get; init; }
    public int With { get; init; }
    public int Without { get; init; }
    public double MedianWith { get; init; } = double.NaN;
    public double MedianWithout { get; init; } = double.NaN;
    public double SuccessWith { get; init; }
    public double SuccessWithout { get; init; }

    /// <summary>P(success | feature) / P(success | no feature) with a 95% CI.</summary>
    public (double Ratio, double Low, double High)? Uplift { get; init; }

    /// <summary>exp(β) of the OLS model on log(1 + ratings) with age and other features held fixed.</summary>
    public double? Multiplier { get; set; }
    public double? MultiplierLow { get; set; }
    public double? MultiplierHigh { get; set; }
    public double? PValue { get; set; }

    public bool Significant => MultiplierLow > 1 || MultiplierHigh < 1;
    public string Name => Feature.Name;
    public string Group => Feature.Group;
}

public sealed record CorrelationResult(string Name, double Rho, int N, string Note);

public sealed class DriversReport
{
    public static readonly DriversReport Empty = new();

    public List<DriverResult> Results { get; init; } = [];
    public List<CorrelationResult> Correlations { get; init; } = [];
    public int Sample { get; init; }
    public int ModelFeatures { get; init; }
    public double? RSquared { get; init; }

    /// <summary>How ratings change when a game is twice as old, other things equal.</summary>
    public double? AgeDoublingMultiplier { get; init; }
    public double? ScreenshotsDoublingMultiplier { get; init; }
}

/// <summary>
/// What goes along with more ratings: uplift of the success share for each feature and an OLS model on log(1 + ratings)
/// controlling for age. Only games with loaded pages are used because only they have complete tags. Correlation ≠ causation.
/// </summary>
public static class Drivers
{
    public const int MinGroupSize = 20;

    public static DriversReport Analyze(AnalyticsDataset d, AnalyticsFilter f, AnalyticsParams p, DateTime now, int topTags = 40)
    {
        var sample = d.Games.Where(g => f.Includes(g) && g.HasDetails && g.RatingsKnown).ToList();
        if (sample.Count < 2 * MinGroupSize) return new DriversReport { Sample = sample.Count };

        var features = BuildFeatures(d, sample, topTags);
        var results = new List<DriverResult>();
        foreach (var feat in features)
        {
            var with = sample.Where(feat.Has).ToList();
            var withoutCount = sample.Count - with.Count;
            if (with.Count < MinGroupSize || withoutCount < MinGroupSize) continue;
            var without = sample.Where(g => !feat.Has(g)).ToList();
            var sw = with.Count(g => g.Ratings >= p.SuccessThreshold);
            var so = without.Count(g => g.Ratings >= p.SuccessThreshold);
            var qw = Stats.QualifiedRatings(with, f.MinRatings);
            var qo = Stats.QualifiedRatings(without, f.MinRatings);
            results.Add(new DriverResult
            {
                Feature = feat,
                With = with.Count,
                Without = without.Count,
                MedianWith = qw.Length > 0 ? Stats.Median(qw) : double.NaN,
                MedianWithout = qo.Length > 0 ? Stats.Median(qo) : double.NaN,
                SuccessWith = (double)sw / with.Count,
                SuccessWithout = (double)so / without.Count,
                Uplift = Stats.RiskRatio(sw, with.Count, so, without.Count),
            });
        }

        var (r2, ageMult, shotsMult, used) = FitOls(sample, results, now);
        return new DriversReport
        {
            Results = results,
            Correlations = Correlate(sample, now),
            Sample = sample.Count,
            ModelFeatures = used,
            RSquared = r2,
            AgeDoublingMultiplier = ageMult,
            ScreenshotsDoublingMultiplier = shotsMult,
        };
    }

    public static List<DriverFeature> BuildFeatures(AnalyticsDataset d, IReadOnlyList<GameRow> sample, int topTags)
    {
        var list = new List<DriverFeature>
        {
            new("platform-web", "Играется в браузере", "Платформа", g => (g.Platforms & Platforms.Web) != 0),
            new("platform-windows", "Windows", "Платформа", g => (g.Platforms & Platforms.Windows) != 0),
            new("platform-osx", "macOS", "Платформа", g => (g.Platforms & Platforms.MacOS) != 0),
            new("platform-linux", "Linux", "Платформа", g => (g.Platforms & Platforms.Linux) != 0),
            new("platform-android", "Android", "Платформа", g => (g.Platforms & Platforms.Android) != 0),
            new("price-paid", "Платная", "Цена", g => g.Price == PriceModel.Paid),
            new("price-nyp", "Своя цена", "Цена", g => g.Price == PriceModel.NameYourPrice),
            new("status-released", "Статус «выпущена»", "Статус", g => g.Status == GameStatus.Released),
            new("status-dev", "Статус «в разработке»", "Статус", g => g.Status == GameStatus.InDevelopment),
            new("ai-none", "Заявлено «без генеративного ИИ»", "ИИ", g => g.Ai == AiUsage.NoAi),
            new("ai-used", "Использует генеративный ИИ", "ИИ", g => g.Ai == AiUsage.UsesAi),
            new("jam", "Сделана на джем", "Прочее", g => g.IsJamEntry),
            new("sale", "Сейчас со скидкой", "Цена", g => g.SalePercent > 0),
            new("featured", "Была в featured", "Витрина", g => g.IsFeatured),
        };

        foreach (var genre in ItchCatalog.Genres.Where(x => x.Slug != "genre-other"))
        {
            var slug = genre.Slug;
            list.Add(new DriverFeature("genre:" + slug, genre.RussianName, "Основной жанр", g => g.Genre == slug));
        }

        var counts = new Dictionary<int, int>();
        foreach (var g in sample)
            foreach (var t in g.TagIds)
                counts[t] = counts.GetValueOrDefault(t) + 1;

        IEnumerable<TagRow> TopOf(TagKind kind, int n) => counts
            .Where(kv => d.Tags.TryGetValue(kv.Key, out var t) && t.Kind == kind && !ItchCatalog.IsGenreSlug(t.Slug) && kv.Value >= MinGroupSize)
            .OrderByDescending(kv => kv.Value).Take(n).Select(kv => d.Tags[kv.Key]);

        void AddTags(TagKind kind, int n, string group)
        {
            foreach (var t in TopOf(kind, n))
            {
                var id = t.Id;
                list.Add(new DriverFeature(t.Slug, Segments.DisplayName(t), group, g => g.HasTag(id)));
            }
        }

        AddTags(TagKind.Tag, topTags, "Тег");
        AddTags(TagKind.Engine, 10, "Движок");
        AddTags(TagKind.Duration, 6, "Длительность");
        AddTags(TagKind.Multiplayer, 5, "Мультиплеер");
        AddTags(TagKind.Input, 6, "Управление");
        AddTags(TagKind.Accessibility, 4, "Доступность");
        return list;
    }

    private static (double? R2, double? AgeMult, double? ShotsMult, int Used) FitOls(IReadOnlyList<GameRow> sample, List<DriverResult> results, DateTime now)
    {
        var usable = results.Where(r => r.With >= MinGroupSize && r.Without >= MinGroupSize).ToList();
        var n = sample.Count;
        var k = 3 + usable.Count;
        if (n < Math.Max(200, 5 * k)) return (null, null, null, 0);

        var x = Matrix<double>.Build.Dense(n, k);
        var y = Vector<double>.Build.Dense(n);
        for (var i = 0; i < n; i++)
        {
            var g = sample[i];
            y[i] = Math.Log(1 + g.Ratings);
            x[i, 0] = 1;
            x[i, 1] = Math.Log(1 + g.AgeDays(now));
            x[i, 2] = Math.Log(1 + g.Screenshots);
            for (var j = 0; j < usable.Count; j++)
                x[i, 3 + j] = usable[j].Feature.Has(g) ? 1 : 0;
        }

        try
        {
            var xtx = x.TransposeThisAndMultiply(x);
            // A small ridge keeps nearly collinear features (e.g. Windows/macOS/Linux) from exploding.
            for (var j = 1; j < k; j++) xtx[j, j] += 1e-6 * n;
            var inv = xtx.Inverse();
            var beta = inv * x.TransposeThisAndMultiply(y);
            var residuals = y - x * beta;
            var rss = residuals.DotProduct(residuals);
            var mean = y.Average();
            var tss = y.Sum(v => (v - mean) * (v - mean));
            var sigma2 = rss / Math.Max(1, n - k);
            for (var j = 0; j < usable.Count; j++)
            {
                var b = beta[3 + j];
                var se = Math.Sqrt(Math.Max(0, sigma2 * inv[3 + j, 3 + j]));
                if (double.IsNaN(b) || double.IsInfinity(b) || double.IsNaN(se)) continue;
                usable[j].Multiplier = Math.Exp(b);
                usable[j].MultiplierLow = Math.Exp(b - 1.96 * se);
                usable[j].MultiplierHigh = Math.Exp(b + 1.96 * se);
                usable[j].PValue = se > 0 ? Stats.NormalPValue(b / se) : null;
            }
            return (tss > 0 ? 1 - rss / tss : null, Math.Exp(beta[1] * Math.Log(2)), Math.Exp(beta[2] * Math.Log(2)), usable.Count);
        }
        catch (Exception)
        {
            return (null, null, null, 0);
        }
    }

    private static List<CorrelationResult> Correlate(IReadOnlyList<GameRow> sample, DateTime now)
    {
        var result = new List<CorrelationResult>();
        void Add(string name, IEnumerable<GameRow> games, Func<GameRow, double> value, string note)
        {
            var list = games.ToList();
            if (list.Count < 30) return;
            var rho = Stats.Spearman(list.Select(value).ToList(), list.Select(g => (double)g.Ratings).ToList());
            if (!double.IsNaN(rho)) result.Add(new CorrelationResult(name, rho, list.Count, note));
        }

        Add("Возраст игры", sample, g => g.AgeDays(now), "Старые игры успели накопить оценки");
        Add("Число скриншотов", sample, g => g.Screenshots, "Оформление страницы");
        Add("Число тегов", sample, g => g.TagIds.Length, "Сколько тегов и фильтров указано");
        Add("Длина короткого описания", sample.Where(g => g.ShortText is not null), g => g.ShortText!.Length, "Подзаголовок в карточке");
        Add("Цена (платные)", sample.Where(g => g.Price == PriceModel.Paid && g.PriceCents > 0), g => g.PriceCents!.Value, "Только платные игры");
        Add("Комментарии", sample.Where(g => g.Comments is not null), g => g.Comments!.Value, "Обсуждаемость почти всегда растёт вместе с оценками");
        return result.OrderByDescending(r => Math.Abs(r.Rho)).ToList();
    }
}
