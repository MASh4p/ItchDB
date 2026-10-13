using ItchDB.Core;

namespace ItchDB.Web.Services;

/// <summary>Tooltip texts: how every metric is calculated and what it means.</summary>
public static class Formulas
{
    public sealed record Tip(string Title, string Formula, string Text);

    private static string T(string ru, string en) => TextLang.Current == UiLanguage.En ? en : ru;

    public static Tip Median(int min) => new(T("Медиана оценок", "Median ratings"),
        T($"median(оценки | оценок ≥ {min})", $"median(ratings | ratings ≥ {min})"),
        T("Типичное число оценок у игры, которую хоть кто-то заметил. У большинства игр itch.io оценок нет, поэтому игры ниже порога не учитываются. Оценки — видимая часть аудитории: их оставляет примерно 1 игрок из 100.",
          "Typical rating count for a game somebody noticed. Most itch.io games have none, so games under the threshold are left out. Ratings are the visible part of the audience: about 1 player in 100 leaves one."));

    public static Tip AdjMedian => new(T("Сглаженная медиана", "Smoothed median"),
        T("adj = (n·медиана сегмента + k·медиана itch.io) / (n + k)", "adj = (n·segment median + k·itch.io median) / (n + k)"),
        T("n — игр сегмента с оценками выше порога, k — сила сглаживания (настройка). Маленькие сегменты притягиваются к общей медиане, чтобы 3 удачные игры не делали нишу «самой востребованной».",
          "n is games in the segment above the ratings threshold, k is the smoothing strength. Small segments are pulled toward the global median so three lucky games do not make a niche look hottest."));

    public static Tip Mean(int min) => new(T("Среднее число оценок", "Mean ratings"),
        T($"mean(оценки | оценок ≥ {min})", $"mean(ratings | ratings ≥ {min})"),
        T("Среднее арифметическое по тем же играм, что и медиана. Медиана на itch.io часто равна 1 или 2: половина игр еле заметна, и соседние теги слипаются. Среднее учитывает хиты и лучше их разделяет.",
          "Arithmetic mean of the same games as the median. On itch.io the median is often 1 or 2, so neighboring tags stick together. The mean notices hits and separates them."));

    public static Tip AdjMean => new(T("Сглаженное среднее", "Smoothed mean"),
        T("adj = (n·среднее сегмента + k·среднее itch.io) / (n + k)", "adj = (n·segment mean + k·itch.io mean) / (n + k)"),
        T("n — игр сегмента с оценками выше порога, k — сила сглаживания. Маленькая выборка притягивается к общему среднему, чтобы пара хитов не делала нишу самой востребованной.",
          "n is games above the threshold, k is smoothing. A small sample is pulled toward the global mean so a couple of hits do not crown a niche."));

    public static Tip Quantiles => new("P75 / P90",
        T("75-й и 90-й перцентили числа оценок", "75th and 90th percentiles of rating counts"),
        T("Сколько набирают лучшие 25% и 10% игр сегмента — потолок, до которого реально дорасти.",
          "What the best 25% and 10% of games in the segment collect — a ceiling you can actually aim at."));

    public static Tip Success(int threshold) => new(T("Шанс успеха", "Chance of success"),
        T($"P = игр с ≥ {threshold} оценками / все игры сегмента на itch.io\n95%: интервал Уилсона", $"P = games with ≥ {threshold} ratings / every game in the segment on itch.io\n95%: Wilson interval"),
        T("Знаменатель — «(N results)» фильтра на itch.io, включая игры без оценок, когда в шапке включено «0 оценок». Выключенный переключатель считает шанс только среди игр с хотя бы одной оценкой. Интервал Уилсона показывает неопределённость; для рейтинга берётся его нижняя граница. Медиана от переключателя не зависит.",
          "The denominator is the filter’s “(N results)” on itch.io, including unrated games when “0 ratings” is on. Off, the chance is only among games with at least one rating. The Wilson interval shows the uncertainty; rankings use its lower bound. The median ignores the switch."));

    public static Tip Competition => new(T("Конкуренция", "Competition"),
        T("игр в сегменте на itch.io; перцентиль log(1 + N) среди сегментов", "games in the segment on itch.io; percentile of log(1 + N) among segments"),
        T("Сколько игр борется за ту же аудиторию. При включённом «0 оценок» это счётчик «(N results)» фильтра или число проектов со страницы /tags, вместе с играми без оценок. При выключенном — только игры, у которых уже есть оценка.",
          "How many games compete for the same audience. With “0 ratings” on, this is the “(N results)” counter or the project count on /tags, including unrated games. Off, only games that already have a rating."));

    public static Tip Opportunity(double wd, double ws, double wc, double wt, bool mean = false) => new("Opportunity score",
        $"100 · ({wd:0.##}·{T("Спрос", "Demand")} + {ws:0.##}·{T("Успех", "Success")} + {wc:0.##}·(1 − {T("Конкуренция", "Competition")}) + {wt:0.##}·{T("Тренд", "Trend")}) / Σw",
        T("Каждый компонент — перцентиль сегмента среди остальных (0…1): спрос — ", "Each part is the segment’s percentile among the others (0…1): demand is ") +
        (mean
            ? T("среднее число оценок, сглаженное к itch.io", "the mean rating count, smoothed toward itch.io")
            : T("типичное число оценок (геометрическое среднее, сглаженное к itch.io)", "the typical rating count (geometric mean, smoothed toward itch.io)")) +
        T(", успех — нижняя граница шанса успеха, конкуренция — число игр, тренд — рост доли среди успешных игр. Веса меняются в настройках.",
          ", success is the lower bound of the success chance, competition is the number of games, trend is the change in share among successful games. Weights live in settings."));

    public static Tip Quadrant => new(T("Квадрант", "Quadrant"),
        T("спрос ≥ медианы сегментов? × конкуренция ≥ медианы сегментов?", "demand ≥ median of segments? × competition ≥ median of segments?"),
        T("Голубой океан — спрос высокий, игр мало. Красный океан — спрос высокий, игр много. Тихая ниша — мало и того и другого. Ловушка — игр много, спрос низкий.",
          "Blue ocean — high demand, few games. Red ocean — high demand, many games. Quiet niche — little of both. Trap — many games, low demand."));

    public static Tip Trend(string? cohorts) => new(T("Тренд", "Trend"),
        T("(доля сегмента среди успешных игр свежих лет) / (доля среди успешных игр двумя годами раньше) − 1", "(segment share among successful games in recent years) / (share among successful games two years earlier) − 1"),
        T($"Растёт ли ниша среди игр, которые набрали порог оценок{(cohorts is null ? "" : $": {cohorts}")}. Сравниваются доли, поэтому общий рост itch.io не искажает тренд.",
          $"Whether the niche is growing among games that cleared the ratings threshold{(cohorts is null ? "" : $": {cohorts}")}. Shares are compared, so overall itch.io growth does not bend the trend."));

    public static Tip Entry => new(T("Порог входа", "Entry bar"),
        T("медиана оценок игр на 1-й странице «Популярного» по тегу", "median ratings of games on page 1 of Popular for the tag"),
        T("Сколько оценок сейчас у игр, которые видны на первой странице тега. Первая страница — главный источник трафика внутри itch.io.",
          "How many ratings the games on the tag’s first page have right now. That page is the main source of traffic inside itch.io."));

    public static Tip WeightedRating(int m) => new(T("Взвешенный рейтинг", "Weighted rating"),
        $"WR = v/(v+m)·R + m/(v+m)·C,  m = {m}",
        T("R — средняя оценка игры, v — число оценок, C — средняя оценка по itch.io. Игры с 3 оценками по 5★ не обгоняют игры с сотнями оценок по 4,7★ (формула IMDb).",
          "R is the game’s average, v is the number of ratings, C is the itch.io average. A game with three 5★ ratings does not pass a game with hundreds of 4.7★ ratings (the IMDb formula)."));

    public static Tip Hot => new(T("Горячее", "Hot"),
        T("(Δоценок + a·Δкомментариев + b·рост позиции) · 7 / дней окна / (возраст + 2)^g", "(Δratings + a·Δcomments + b·rank gain) · 7 / window days / (age + 2)^g"),
        T("Прирост за последние до 7 дней между ежедневными снимками, приведённый к недельному темпу. Деление на возраст поднимает новые игры, которые быстро набирают аудиторию. Нужны снимки минимум за 2 дня.",
          "Growth over up to 7 days between daily snapshots, scaled to a week. Dividing by age lifts new games that are finding an audience fast. Needs snapshots from at least two days."));

    public static Tip Audience => new(T("Оценка аудитории", "Audience estimate"),
        T("просмотры ≈ оценки / доля оценивающих (по умолчанию 1%, диапазон 0,5–2%)", "views ≈ ratings / share who rate (default 1%, range 0.5–2%)"),
        T("itch.io не показывает просмотры и скачивания чужих игр. С API-ключом в «Моя игра» доли считаются по вашим реальным играм.",
          "itch.io does not show views and downloads of other people’s games. With an API key in My game, the shares come from your real games."));

    public static Tip Uplift => new("Uplift",
        T("P(успех | признак) / P(успех | без признака), 95% ДИ логарифмическим методом", "P(success | feature) / P(success | without feature), 95% CI by the log method"),
        T("Во сколько раз чаще игры с признаком набирают порог оценок. Это корреляция: признак может просто сопутствовать более старым или более крупным проектам.",
          "How many times more often games with the feature clear the ratings threshold. It is a correlation: the feature may simply travel with older or larger projects."));

    public static Tip Multiplier => new(T("Множитель (модель)", "Multiplier (model)"),
        T("exp(β) из OLS: ln(1 + оценки) ~ ln(1 + возраст) + ln(1 + скриншоты) + признаки", "exp(β) from OLS: ln(1 + ratings) ~ ln(1 + age) + ln(1 + screenshots) + features"),
        T("Во сколько раз больше оценок у игр с признаком при прочих равных, включая возраст. Звёздочка — 95% интервал не включает ×1. Корреляция ≠ причинность.",
          "How many times more ratings games with the feature have, other things equal, including age. A star means the 95% interval excludes ×1. Correlation is not causation."));

    public static Tip Lift => new(T("Лифт сочетания", "Combination lift"),
        T("медиана(A и B) / max(медиана A, медиана B)", "median(A and B) / max(median A, median B)"),
        T("Лифт > 1 — вместе теги работают лучше, чем каждый по отдельности.",
          "Lift > 1 — together the tags beat either tag alone."));
}
