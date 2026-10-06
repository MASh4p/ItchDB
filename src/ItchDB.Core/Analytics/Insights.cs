using System.Globalization;

namespace ItchDB.Core.Analytics;

public enum InsightTone
{
    Info,
    Good,
    Warn,
    Bad,
}

public sealed record Insight(string Title, string Text, InsightTone Tone, string? Link = null);

public static class RuFormat
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string N(double v, int digits = 0) => double.IsNaN(v) ? "—" : v.ToString("N" + digits, Ru);

    public static string Pct(double share, int digits = 0) => double.IsNaN(share) ? "—" : (share * 100).ToString("N" + digits, Ru) + "%";

    public static string X(double ratio) => double.IsNaN(ratio) ? "—" : "×" + ratio.ToString(ratio >= 10 ? "0" : "0.0", Ru);

    public static string Signed(double share) => double.IsNaN(share) ? "—" : (share > 0 ? "+" : "") + (share * 100).ToString("N0", Ru) + "%";

    public static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " и " + list[^1],
        };
    }
}

public sealed class InsightInput
{
    public required AnalyticsDataset Data { get; init; }
    public required AnalyticsFilter Filter { get; init; }
    public required AnalyticsParams Params { get; init; }
    public required SegmentStats All { get; init; }
    public IReadOnlyList<SegmentStats> Genres { get; init; } = [];
    public IReadOnlyList<SegmentStats> Tags { get; init; } = [];
    public IReadOnlyList<SegmentStats> Formats { get; init; } = [];
    public DriversReport? Drivers { get; init; }
    public IReadOnlyList<TagComboResult>? Combos { get; init; }
    public string? TrendCohorts { get; init; }
}

/// <summary>Turns computed statistics into short conclusions for the overview and developer pages.</summary>
public static class InsightGenerator
{
    public static List<Insight> Build(InsightInput x)
    {
        var list = new List<Insight>();
        var p = x.Params;
        var min = x.Filter.MinRatings;
        var all = x.All;

        if (x.Data.YearsTotal > 0 && x.Data.YearsComplete < x.Data.YearsTotal)
        {
            list.Add(new Insight("Данные собраны частично",
                $"Полностью пройдено {x.Data.YearsComplete} из {x.Data.YearsTotal} лет каталога. Пока статистика смещена к более заметным играм: " +
                "медианы и доли успешных игр завышены. «Глубокий сбор» доберёт остальное.", InsightTone.Warn, "/data"));
        }

        if (all.TotalItch is { } total && total > 0)
        {
            var zero = (double)all.Bin0 / all.Denominator;
            list.Add(new Insight("Большинство игр остаются незамеченными",
                $"{RuFormat.Pct(zero)} из {RuFormat.N(total)} игр на itch.io не получили ни одной оценки, а {p.SuccessThreshold}+ оценок набрали лишь {RuFormat.Pct(all.SuccessShare, 1)}. " +
                $"Поэтому медианы считаются по играм с ≥ {min} оценками: такая игра набирает в среднем {RuFormat.N(all.Median)} (P75 — {RuFormat.N(all.P75, 1)}, P90 — {RuFormat.N(all.P90, 1)}).",
                InsightTone.Info));
        }

        var genres = x.Genres.Where(s => s.Opportunity is not null).OrderByDescending(s => s.Opportunity).ToList();
        if (genres.Count >= 3)
        {
            var best = genres.Take(3).ToList();
            list.Add(new Insight("Самые перспективные жанры",
                $"По Opportunity лидируют {RuFormat.Join(best.Select(s => $"{s.Name} ({RuFormat.N(s.Opportunity!.Value)})"))}. " +
                $"У «{best[0].Name}» медиана {RuFormat.N(best[0].Median)} оценок, успешны {RuFormat.Pct(best[0].SuccessShare, 1)} игр.",
                InsightTone.Good, "/tags?kind=genre"));
            var traps = genres.Where(s => s.Quadrant == Quadrant.Trap).OrderBy(s => s.Opportunity).Take(2).ToList();
            if (traps.Count > 0)
            {
                list.Add(new Insight("Жанры-ловушки",
                    $"{RuFormat.Join(traps.Select(s => $"«{s.Name}»"))}: игр много, а типичная игра получает {RuFormat.Join(traps.Select(s => RuFormat.N(s.Median)))} оценок " +
                    $"при медиане по itch.io {RuFormat.N(all.Median)}. Здесь нужна особенно сильная идея или крючок.", InsightTone.Warn, "/tags?kind=genre"));
            }
        }

        var blue = x.Tags.Where(s => s.Quadrant == Quadrant.BlueOcean && s.Opportunity is not null && s.Qualified >= 2 * p.MinSegmentSample)
            .OrderByDescending(s => s.Opportunity).Take(6).ToList();
        if (blue.Count > 0)
        {
            list.Add(new Insight("Голубые океаны среди тегов",
                $"{RuFormat.Join(blue.Select(s => $"«{s.Name}»"))} — спрос выше медианы тегов, а игр меньше. Хороший старт для поиска ниши.",
                InsightTone.Good, "/developer"));
        }

        var web = x.Formats.FirstOrDefault(s => s.Key == "platform-web");
        var win = x.Formats.FirstOrDefault(s => s.Key == "platform-windows");
        if (web is { SmallSample: false } && win is { SmallSample: false } && win.Median > 0)
        {
            var ratio = web.Median / win.Median;
            list.Add(new Insight("Браузер или скачивание",
                $"Игры, которые запускаются в браузере, набирают медиану {RuFormat.N(web.Median)} оценок против {RuFormat.N(win.Median)} у игр для Windows ({RuFormat.X(ratio)}); " +
                $"успешны {RuFormat.Pct(web.SuccessShare, 1)} против {RuFormat.Pct(win.SuccessShare, 1)}. Браузерную игру проще попробовать: не нужно ничего скачивать.",
                ratio >= 1 ? InsightTone.Good : InsightTone.Info));
        }

        var free = x.Formats.FirstOrDefault(s => s.Key == "price-free");
        var paid = x.Formats.FirstOrDefault(s => s.Key == "price-paid");
        if (free is { SmallSample: false } && paid is { SmallSample: false })
        {
            list.Add(new Insight("Бесплатно или платно",
                $"Среди замеченных игр медиана у платных — {RuFormat.N(paid.Median)} оценок, у бесплатных — {RuFormat.N(free.Median)}. " +
                $"Но до {p.SuccessThreshold}+ оценок доходят {RuFormat.Pct(paid.SuccessShare, 1)} платных и {RuFormat.Pct(free.SuccessShare, 1)} бесплатных игр: " +
                "цена отсекает случайных игроков, зато платные игры реже бывают «пробами пера».", InsightTone.Info));
        }

        if (x.Drivers is { Results.Count: > 0 } drivers)
        {
            var positive = drivers.Results.Where(r => r.Multiplier > 1 && r.MultiplierLow > 1 && r.Group != "Витрина")
                .OrderByDescending(r => r.Multiplier).Take(4).ToList();
            if (positive.Count > 0)
            {
                list.Add(new Insight("Что сопутствует успеху",
                    $"При прочих равных (возраст, платформы, жанр, теги) больше оценок получают игры с признаками: " +
                    $"{RuFormat.Join(positive.Select(r => $"«{r.Name}» {RuFormat.X(r.Multiplier!.Value)}"))}. Это корреляции, а не гарантия.",
                    InsightTone.Good, "/developer#drivers"));
            }
            var negative = drivers.Results.Where(r => r.Multiplier < 1 && r.MultiplierHigh < 1)
                .OrderBy(r => r.Multiplier).Take(3).ToList();
            if (negative.Count > 0)
            {
                list.Add(new Insight("Что связано с меньшим числом оценок",
                    $"{RuFormat.Join(negative.Select(r => $"«{r.Name}» {RuFormat.X(r.Multiplier!.Value)}"))} — при прочих равных такие игры получают меньше оценок.",
                    InsightTone.Warn, "/developer#drivers"));
            }
        }

        if (x.Combos is { Count: > 0 } combos)
        {
            var strong = combos.Where(c => c.Lift >= 1.3 && c.Qualified >= 2 * p.MinSegmentSample).OrderByDescending(c => c.Lift).Take(3).ToList();
            if (strong.Count > 0)
            {
                list.Add(new Insight("Сильные сочетания тегов",
                    RuFormat.Join(strong.Select(c => $"«{Segments.DisplayName(c.A)} + {Segments.DisplayName(c.B)}» — медиана {RuFormat.N(c.Median)} ({RuFormat.X(c.Lift)} к лучшему из тегов)")) + ".",
                    InsightTone.Good, "/developer#combos"));
            }
        }

        var entries = x.Tags.Where(s => s.EntryThreshold is not null && s.TotalItch > 1000).ToList();
        if (entries.Count >= 4)
        {
            var hardest = entries.OrderByDescending(s => s.EntryThreshold).Take(3);
            var easiest = entries.OrderBy(s => s.EntryThreshold).Take(3);
            list.Add(new Insight("Порог входа в popular",
                $"Чтобы оказаться на первой странице «Популярного» по тегу, больше всего оценок нужно в {RuFormat.Join(hardest.Select(s => $"«{s.Name}» (~{RuFormat.N(s.EntryThreshold!.Value)})"))}; " +
                $"среди крупных тегов проще всего — {RuFormat.Join(easiest.Select(s => $"«{s.Name}» (~{RuFormat.N(s.EntryThreshold!.Value)})"))}.",
                InsightTone.Info, "/tags"));
        }

        var rising = x.Tags.Concat(x.Genres).Where(s => s.Trend >= 0.3 && s.RecentSuccesses >= 15).OrderByDescending(s => s.Trend).Take(4).ToList();
        if (rising.Count > 0)
        {
            list.Add(new Insight("Растущие ниши",
                $"Доля среди успешных игр выросла у {RuFormat.Join(rising.Select(s => $"«{s.Name}» ({RuFormat.Signed(s.Trend!.Value)})"))}" +
                (x.TrendCohorts is { } c ? $" (когорты {c})." : "."),
                InsightTone.Good, "/tags"));
        }
        var falling = x.Tags.Concat(x.Genres).Where(s => s.Trend <= -0.3 && s.EarlierSuccesses >= 15).OrderBy(s => s.Trend).Take(3).ToList();
        if (falling.Count > 0)
        {
            list.Add(new Insight("Остывающие ниши",
                $"Теряют долю среди успешных игр: {RuFormat.Join(falling.Select(s => $"«{s.Name}» ({RuFormat.Signed(s.Trend!.Value)})"))}.",
                InsightTone.Warn, "/tags"));
        }
        return list;
    }
}
