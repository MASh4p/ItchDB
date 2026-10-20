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
    public UiLanguage Language { get; init; } = UiLanguage.Ru;
}

/// <summary>Turns computed statistics into short conclusions for the overview and developer pages.</summary>
public static class InsightGenerator
{
    public static List<Insight> Build(InsightInput x)
    {
        var list = new List<Insight>();
        var lang = x.Language;
        var culture = lang == UiLanguage.En ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ru-RU");
        string T(string ru, string en) => lang == UiLanguage.En ? en : ru;
        string N(double v, int digits = 0) => double.IsNaN(v) ? "—" : v.ToString("N" + digits, culture);
        string Pct(double share, int digits = 0) => double.IsNaN(share) ? "—" : (share * 100).ToString("N" + digits, culture) + "%";
        string Times(double ratio) => double.IsNaN(ratio) ? "—" : "×" + ratio.ToString(ratio >= 10 ? "0" : "0.0", culture);
        string Signed(double share) => double.IsNaN(share) ? "—" : (share > 0 ? "+" : "") + (share * 100).ToString("N0", culture) + "%";
        string Join(IEnumerable<string> items)
        {
            var rows = items.ToList();
            return rows.Count switch
            {
                0 => "",
                1 => rows[0],
                _ => string.Join(", ", rows.Take(rows.Count - 1)) + T(" и ", " and ") + rows[^1],
            };
        }

        var p = x.Params;
        var min = x.Filter.MinRatings;
        var all = x.All;

        if (all.TotalItch is { } total && total > 0)
        {
            var zero = (double)all.Bin0 / all.Denominator;
            list.Add(new Insight(T("Большинство игр остаются незамеченными", "Most games stay unnoticed"),
                T($"{Pct(zero)} из {N(total)} игр на itch.io не получили ни одной оценки, а {p.SuccessThreshold}+ оценок набрали лишь {Pct(all.SuccessShare, 1)}. Поэтому медианы считаются по играм с ≥ {min} оценками: такая игра набирает в среднем {N(all.Median)} (P75 — {N(all.P75, 1)}, P90 — {N(all.P90, 1)}).",
                  $"{Pct(zero)} of {N(total)} games on itch.io have no ratings, and only {Pct(all.SuccessShare, 1)} reached {p.SuccessThreshold}+. Medians use games with ≥ {min} ratings: such a game collects {N(all.Median)} on average (P75 — {N(all.P75, 1)}, P90 — {N(all.P90, 1)})."),
                InsightTone.Info));
        }

        var genres = x.Genres.Where(s => s.Opportunity is not null).OrderByDescending(s => s.Opportunity).ToList();
        if (genres.Count >= 3)
        {
            var best = genres.Take(3).ToList();
            list.Add(new Insight(T("Самые перспективные жанры", "Most promising genres"),
                T($"По Opportunity лидируют {Join(best.Select(s => $"{s.Name} ({N(s.Opportunity!.Value)})"))}. У «{best[0].Name}» медиана {N(best[0].Median)} оценок, успешны {Pct(best[0].SuccessShare, 1)} игр.",
                  $"Opportunity leaders: {Join(best.Select(s => $"{s.Name} ({N(s.Opportunity!.Value)})"))}. “{best[0].Name}” has a median of {N(best[0].Median)} ratings and {Pct(best[0].SuccessShare, 1)} of its games succeed."),
                InsightTone.Good, "/tags?kind=genre"));
            var traps = genres.Where(s => s.Quadrant == Quadrant.Trap).OrderBy(s => s.Opportunity).Take(2).ToList();
            if (traps.Count > 0)
            {
                list.Add(new Insight(T("Жанры-ловушки", "Trap genres"),
                    T($"{Join(traps.Select(s => $"«{s.Name}»"))}: игр много, а типичная игра получает {Join(traps.Select(s => N(s.Median)))} оценок при медиане по itch.io {N(all.Median)}. Здесь нужна особенно сильная идея или крючок.",
                      $"{Join(traps.Select(s => $"“{s.Name}”"))}: many games, and a typical one gets {Join(traps.Select(s => N(s.Median)))} ratings against an itch.io median of {N(all.Median)}. This needs a strong idea or a hook."),
                    InsightTone.Warn, "/tags?kind=genre"));
            }
        }

        var blue = x.Tags.Where(s => s.Quadrant == Quadrant.BlueOcean && s.Opportunity is not null && s.Qualified >= 2 * p.MinSegmentSample)
            .OrderByDescending(s => s.Opportunity).Take(6).ToList();
        if (blue.Count > 0)
        {
            list.Add(new Insight(T("Голубые океаны среди тегов", "Blue oceans among tags"),
                T($"{Join(blue.Select(s => $"«{s.Name}»"))} — спрос выше медианы тегов, а игр меньше. Хороший старт для поиска ниши.",
                  $"{Join(blue.Select(s => $"“{s.Name}”"))} — demand above the tag median, and fewer games. A good place to look for a niche."),
                InsightTone.Good, "/developer"));
        }

        var web = x.Formats.FirstOrDefault(s => s.Key == "platform-web");
        var win = x.Formats.FirstOrDefault(s => s.Key == "platform-windows");
        if (web is { SmallSample: false } && win is { SmallSample: false } && win.Median > 0)
        {
            var ratio = web.Median / win.Median;
            list.Add(new Insight(T("Браузер или скачивание", "Browser or download"),
                T($"Игры, которые запускаются в браузере, набирают медиану {N(web.Median)} оценок против {N(win.Median)} у игр для Windows ({Times(ratio)}); успешны {Pct(web.SuccessShare, 1)} против {Pct(win.SuccessShare, 1)}. Браузерную игру проще попробовать: не нужно ничего скачивать.",
                  $"Games that run in the browser have a median of {N(web.Median)} ratings against {N(win.Median)} for Windows ({Times(ratio)}); {Pct(web.SuccessShare, 1)} succeed against {Pct(win.SuccessShare, 1)}. A browser game is easier to try: nothing to download."),
                ratio >= 1 ? InsightTone.Good : InsightTone.Info));
        }

        var free = x.Formats.FirstOrDefault(s => s.Key == "price-free");
        var paid = x.Formats.FirstOrDefault(s => s.Key == "price-paid");
        if (free is { SmallSample: false } && paid is { SmallSample: false })
        {
            list.Add(new Insight(T("Бесплатно или платно", "Free or paid"),
                T($"Среди замеченных игр медиана у платных — {N(paid.Median)} оценок, у бесплатных — {N(free.Median)}. Но до {p.SuccessThreshold}+ оценок доходят {Pct(paid.SuccessShare, 1)} платных и {Pct(free.SuccessShare, 1)} бесплатных игр: цена отсекает случайных игроков, зато платные игры реже бывают «пробами пера».",
                  $"Among noticed games the paid median is {N(paid.Median)} ratings, the free median is {N(free.Median)}. But {Pct(paid.SuccessShare, 1)} of paid games and {Pct(free.SuccessShare, 1)} of free games reach {p.SuccessThreshold}+ ratings: price filters out casual players, and paid games are less often first tries."),
                InsightTone.Info));
        }

        if (x.Drivers is { Results.Count: > 0 } drivers)
        {
            var positive = drivers.Results.Where(r => r.Multiplier > 1 && r.MultiplierLow > 1 && r.Group != "Витрина" && r.Group != "Storefront")
                .OrderByDescending(r => r.Multiplier).Take(4).ToList();
            if (positive.Count > 0)
            {
                list.Add(new Insight(T("Что сопутствует успеху", "What goes with success"),
                    T($"При прочих равных (возраст, платформы, жанр, теги) больше оценок получают игры с признаками: {Join(positive.Select(r => $"«{r.Name}» {Times(r.Multiplier!.Value)}"))}. Это корреляции, а не гарантия.",
                      $"Other things equal (age, platforms, genre, tags), more ratings go to games with: {Join(positive.Select(r => $"“{r.Name}” {Times(r.Multiplier!.Value)}"))}. These are correlations, not a guarantee."),
                    InsightTone.Good, "/developer#drivers"));
            }
            var negative = drivers.Results.Where(r => r.Multiplier < 1 && r.MultiplierHigh < 1)
                .OrderBy(r => r.Multiplier).Take(3).ToList();
            if (negative.Count > 0)
            {
                list.Add(new Insight(T("Что связано с меньшим числом оценок", "What goes with fewer ratings"),
                    T($"{Join(negative.Select(r => $"«{r.Name}» {Times(r.Multiplier!.Value)}"))} — при прочих равных такие игры получают меньше оценок.",
                      $"{Join(negative.Select(r => $"“{r.Name}” {Times(r.Multiplier!.Value)}"))} — other things equal, these games get fewer ratings."),
                    InsightTone.Warn, "/developer#drivers"));
            }
        }

        if (x.Combos is { Count: > 0 } combos)
        {
            var strong = combos.Where(c => c.Lift >= 1.3 && c.Qualified >= 2 * p.MinSegmentSample).OrderByDescending(c => c.Lift).Take(3).ToList();
            if (strong.Count > 0)
            {
                list.Add(new Insight(T("Сильные сочетания тегов", "Strong tag combinations"),
                    Join(strong.Select(c => T(
                        $"«{Segments.DisplayName(c.A, lang)} + {Segments.DisplayName(c.B, lang)}» — медиана {N(c.Median)} ({Times(c.Lift)} к лучшему из тегов)",
                        $"“{Segments.DisplayName(c.A, lang)} + {Segments.DisplayName(c.B, lang)}” — median {N(c.Median)} ({Times(c.Lift)} vs the stronger tag)"))) + ".",
                    InsightTone.Good, "/developer#combos"));
            }
        }

        var entries = x.Tags.Where(s => s.EntryThreshold is not null && s.TotalItch > 1000).ToList();
        if (entries.Count >= 4)
        {
            var hardest = entries.OrderByDescending(s => s.EntryThreshold).Take(3);
            var easiest = entries.OrderBy(s => s.EntryThreshold).Take(3);
            list.Add(new Insight(T("Порог входа в popular", "Bar to reach popular"),
                T($"Чтобы оказаться на первой странице «Популярного» по тегу, больше всего оценок нужно в {Join(hardest.Select(s => $"«{s.Name}» (~{N(s.EntryThreshold!.Value)})"))}; среди крупных тегов проще всего — {Join(easiest.Select(s => $"«{s.Name}» (~{N(s.EntryThreshold!.Value)})"))}.",
                  $"The first page of Popular needs the most ratings in {Join(hardest.Select(s => $"“{s.Name}” (~{N(s.EntryThreshold!.Value)})"))}; among large tags the easiest are {Join(easiest.Select(s => $"“{s.Name}” (~{N(s.EntryThreshold!.Value)})"))}."),
                InsightTone.Info, "/tags"));
        }

        var rising = x.Tags.Concat(x.Genres).Where(s => s.Trend >= 0.3 && s.RecentSuccesses >= 15).OrderByDescending(s => s.Trend).Take(4).ToList();
        if (rising.Count > 0)
        {
            list.Add(new Insight(T("Растущие ниши", "Rising niches"),
                T($"Доля среди успешных игр выросла у {Join(rising.Select(s => $"«{s.Name}» ({Signed(s.Trend!.Value)})"))}" + (x.TrendCohorts is { } c1 ? $" (когорты {c1})." : "."),
                  $"Share among successful games grew for {Join(rising.Select(s => $"“{s.Name}” ({Signed(s.Trend!.Value)})"))}" + (x.TrendCohorts is { } c2 ? $" (cohorts {c2})." : ".")),
                InsightTone.Good, "/tags"));
        }
        var falling = x.Tags.Concat(x.Genres).Where(s => s.Trend <= -0.3 && s.EarlierSuccesses >= 15).OrderBy(s => s.Trend).Take(3).ToList();
        if (falling.Count > 0)
        {
            list.Add(new Insight(T("Остывающие ниши", "Cooling niches"),
                T($"Теряют долю среди успешных игр: {Join(falling.Select(s => $"«{s.Name}» ({Signed(s.Trend!.Value)})"))}.",
                  $"Losing share among successful games: {Join(falling.Select(s => $"“{s.Name}” ({Signed(s.Trend!.Value)})"))}."),
                InsightTone.Warn, "/tags"));
        }
        return list;
    }
}
