using System.Text.RegularExpressions;
using ItchDB.Core;

namespace ItchDB.Web.Services;

/// <summary>
/// Shows stored crawl text in the current UI language.
/// The crawler writes Russian; old journal rows and run results stay in the database and are translated here.
/// </summary>
public static partial class CrawlText
{
    public static string L(string? message)
    {
        if (string.IsNullOrEmpty(message) || TextLang.Current != UiLanguage.En) return message ?? "";
        var s = message;
        foreach (var rule in Rules) s = rule.Rx.Replace(s, rule.En);
        s = PageStep().Replace(s, PageStepEn);
        foreach (var (ru, en) in Phrases) s = s.Replace(ru, en, StringComparison.Ordinal);
        return s;
    }

    private static string PageStepEn(Match m)
    {
        var end = m.Groups[2].Success ? "–" + m.Groups[2].Value : "";
        var of = m.Groups[3].Success ? " of ~" + m.Groups[3].Value : "";
        return $" — page {m.Groups[1].Value}{end}{of}";
    }

    private static readonly (string Ru, string En)[] Phrases = BuildPhrases();

    private static (string Ru, string En)[] BuildPhrases()
    {
        (string Ru, string En)[] raw =
        [
            ("Остановлено. Прогресс сохранён, следующий запуск продолжит с того же места.",
                "Stopped. Progress is saved; the next run continues from the same place."),
            ("Cloudflare продолжает показывать проверку. Откройте itch.io в браузере, пройдите проверку и импортируйте cookie cf_clearance на странице «Сбор данных».",
                "Cloudflare is still showing a check. Open itch.io in a browser, complete the check, and import the cf_clearance cookie on the Data collection page."),
            ("Не найдено значение cf_clearance. Откройте DevTools на itch.io → Application → Cookies → https://itch.io и скопируйте значение cf_clearance. Через консоль (document.cookie) его не видно.",
                "No cf_clearance value found. Open DevTools on itch.io → Application → Cookies → https://itch.io and copy the cf_clearance value. It is not visible through the console (document.cookie)."),
            ("Код 400 повторился больше 5 раз подряд. Сеанс браузера сброшен, следующий запрос откроет чистое окно.",
                "Status 400 happened more than 5 times in a row. The browser session was reset; the next request opens a clean window."),
            ("Приложение было закрыто во время сбора", "The app was closed during collection"),
            ("Вставьте значение cookie cf_clearance.", "Paste the cf_clearance cookie value."),
            ("Страница не загрузилась", "The page did not load"),
            ("Браузер не открыл отладочный порт", "The browser did not open a debug port"),
            ("счётчик не найден на странице", "counter not found on the page"),
            ("Страницы игр: у всех игр в базе уже есть карточка",
                "Game pages: every game in the database already has a page"),
            ("Полный каталог по сегментам (годы → цена → жанр, теги, движки, 18+)",
                "Full catalog by segment (years → price → genre, tags, engines, 18+)"),
            ("Топ по оценкам: весь каталог, годы, жанры, крупные теги",
                "Top rated: whole catalog, years, genres, large tags"),
            ("Счётчики списков (игры без оценок — только число)",
                "List counters (unrated games — the count only)"),
            ("Страницы игр (теги, даты, движок, комментарии)",
                "Game pages (tags, dates, engine, comments)"),
            ("Порог входа: первая страница popular каждого тега",
                "Entry bar: first popular page of each tag"),
            ("Глубокий сбор: продолжение по курсорам",
                "Deep crawl: continue from cursors"),
            ("Позиции в popular и new-and-popular",
                "Positions in popular and new-and-popular"),
            ("Новые релизы (newest + RSS с датами)",
                "New releases (newest + RSS with dates)"),
            ("Отслеживаемые игры: позиции в тегах",
                "Tracked games: positions in tags"),
            ("Профили разработчиков (подписчики)",
                "Developer profiles (followers)"),
            ("Каталог тегов (/tags)", "Tag catalog (/tags)"),
            ("Обновление оценок (top-rated)", "Rating refresh (top-rated)"),
            ("Позиции в popular", "Positions in popular"),
            ("Проверка отслеживаемых игр", "Tracked games check"),
            ("Ежедневное обновление", "Daily update"),
            ("Быстрый старт", "Quick start"),
            ("Глубокий сбор", "Deep crawl"),
            ("Карточки игр", "Game pages"),
            ("кнопка «Обновить»", "Refresh button"),
            ("по расписанию", "on schedule"),
            ("Фоновый сбор страниц игр остановлен:", "Background game-page crawl stopped:"),
            ("В окне Chrome проверка Cloudflare. Если она не исчезла сама, пройдите её там — сбор продолжится и пауза не понадобится.",
                "Chrome is showing a Cloudflare check. If it does not disappear on its own, complete it there — the crawl continues and no pause is needed."),
            ("itch.io перестал отдавать страницы (проверка или код 400). Пройдите проверку в окне Chrome: сбор подождёт и повторит запрос, а не будет пропускать следующие.",
                "itch.io stopped returning pages (a check or status 400). Complete the check in the Chrome window: the crawl will wait and retry the request instead of skipping the next ones."),
            ("проверка Cloudflare только на этой странице, пропускаю её и иду дальше",
                "Cloudflare check on this page only, skipping it and moving on"),
            ("ответ не похож на страницу игры, повторим позже",
                "response does not look like a game page, will retry later"),
            ("вместо JSON пришла HTML-страница без списка игр",
                "got an HTML page with no game list instead of JSON"),
            ("упёрлись в лимит 200 страниц, дальнейшее дробление невозможно",
                "hit the 200-page limit, cannot split further"),
            ("фильтр не сузил список, дробление остановлено",
                "the filter did not narrow the list, splitting stopped"),
            ("itch.io не поддерживает такой список:", "itch.io does not support this list:"),
            ("itch.io не поддерживает список ", "itch.io does not support the list "),
            ("не найдено число подписчиков", "follower count not found"),
            ("не найден счётчик results", "results counter not found"),
            ("не найдено ни одного тега", "no tags found"),
            ("не найдено ни одного джема", "no jams found"),
            ("не найден id игры", "game id not found"),
            ("не найден id джема", "jam id not found"),
            ("нет ответа, соединение оборвалось:", "no response, connection dropped:"),
            ("Ошибка разбора:", "Parse error:"),
            ("(перенаправил на ", "(redirected to "),
            ("весь каталог", "whole catalog"),
            ("Старт:", "Start:"),
            ("Продолжение", "Resume"),
            ("Пауза", "Pause"),
            ("вручную", "manual"),
            ("Джемы", "Jams"),
        ];
        return raw.OrderByDescending(p => p.Ru.Length).ToArray();
    }

    private static readonly (Regex Rx, string En)[] Rules =
    [
        (Requests(), "Requests $1, games updated $2, errors $3"),
        (TagsUpdated(), "Tag catalog updated: $1 pages"),
        (BudgetLists(), "request budget used up, $1 lists left — continuing next time"),
        (ListSummary(), "lists $1/$2, complete $3, split $4, skipped as fresh $5"),
        (NewReleases(), "New releases: $1 new games, $2 dates from RSS"),
        (FeaturedAdded(), "Featured: $1 new entries"),
        (PagesLeft(), "Game pages: processed $1, errors $2, about $3 left"),
        (PagesDone(), "Game pages: processed $1, errors $2"),
        (PagesQueued(), "Game pages: $1 queued"),
        (ProfilesUpdated(), "Developer profiles: updated $1"),
        (JamsSummary(), "Jams: $1 cards, loaded entries $2 of $3 jams"),
        (Counters(), "List counters: saved $1, fresh skipped $2, errors $3"),
        (FewerKnown(), ", fewer than already known games $1"),
        (BudgetLeft(), ", budget used up — $1 left"),
        (GameStep(), "Game $1 of $2:"),
        (ProfileStep(), "Profile $1 of $2:"),
        (TrackStep(), "Tracking $1 of $2:"),
        (CounterStep(), "Counter $1 of $2:"),
        (JamEntries(), "Jam $1: entries"),
        (TagsPage(), "/tags — page $1 of $2"),
        (NewestPage(), "newest — page $1"),
        (SplitParts(), "more than 200 pages — splitting into $1 parts"),
        (ParallelTabs(), "Game pages run in their own tabs alongside the lists, up to $1 at once. Each tab waits only for its own load, then rests $2 s."),
        (Http400Pause(), "Status 400. Pause $1 s ($2 in a row). After five in a row the browser session resets itself."),
        (NoResponsePause(), "No response $1 times in a row. Pause $2 s, then the crawl continues. This is not a Cloudflare check."),
    ];

    [GeneratedRegex(@"Запросов (\d+), игр обновлено (\d+), ошибок (\d+)")]
    private static partial Regex Requests();

    [GeneratedRegex(@"Каталог тегов обновлён: (\d+) стр\.")]
    private static partial Regex TagsUpdated();

    [GeneratedRegex(@"бюджет запросов исчерпан, осталось (\d+) списков — продолжим в следующий раз")]
    private static partial Regex BudgetLists();

    [GeneratedRegex(@"списков (\d+)/(\d+), полностью (\d+), дробились (\d+), пропущено как свежие (\d+)")]
    private static partial Regex ListSummary();

    [GeneratedRegex(@"Новые релизы: новых игр (\d+), дат из RSS (\d+)")]
    private static partial Regex NewReleases();

    [GeneratedRegex(@"Featured: новых записей (\d+)")]
    private static partial Regex FeaturedAdded();

    [GeneratedRegex(@"Страницы игр: обработано (\d+), ошибок (\d+), осталось примерно (\d+)")]
    private static partial Regex PagesLeft();

    [GeneratedRegex(@"Страницы игр: обработано (\d+), ошибок (\d+)")]
    private static partial Regex PagesDone();

    [GeneratedRegex(@"Страницы игр: в очереди (\d+)")]
    private static partial Regex PagesQueued();

    [GeneratedRegex(@"Профили разработчиков: обновлено (\d+)")]
    private static partial Regex ProfilesUpdated();

    [GeneratedRegex(@"Джемы: карточек (\d+), загружено участников (\d+) из (\d+) джемов")]
    private static partial Regex JamsSummary();

    [GeneratedRegex(@"Счётчики списков: записано (\d+), свежие пропущены (\d+), ошибок (\d+)")]
    private static partial Regex Counters();

    [GeneratedRegex(@", меньше уже известных игр (\d+)")]
    private static partial Regex FewerKnown();

    [GeneratedRegex(@", бюджет кончился — осталось (\d+)")]
    private static partial Regex BudgetLeft();

    [GeneratedRegex(@"Игра (\d+) из (\d+):")]
    private static partial Regex GameStep();

    [GeneratedRegex(@"Профиль (\d+) из (\d+):")]
    private static partial Regex ProfileStep();

    [GeneratedRegex(@"Отслеживание (\d+) из (\d+):")]
    private static partial Regex TrackStep();

    [GeneratedRegex(@"Счётчик (\d+) из (\d+):")]
    private static partial Regex CounterStep();

    [GeneratedRegex(@"Джем ([^:]+): участники")]
    private static partial Regex JamEntries();

    [GeneratedRegex(@"/tags — стр\. (\d+) из (\d+)")]
    private static partial Regex TagsPage();

    [GeneratedRegex(@"newest — стр\. (\d+)")]
    private static partial Regex NewestPage();

    [GeneratedRegex(@" — стр\. (\d+)(?:–(\d+))?(?: из ~(\d+))?")]
    private static partial Regex PageStep();

    [GeneratedRegex(@"больше 200 страниц — дробим на (\d+) частей")]
    private static partial Regex SplitParts();

    [GeneratedRegex(@"Страницы игр идут в своих вкладках параллельно со списками, до (\d+) сразу\. Каждая вкладка ждёт только свою загрузку, затем отдыхает ([0-9.,]+) с\.")]
    private static partial Regex ParallelTabs();

    [GeneratedRegex(@"Код 400\. Пауза (\d+) с \((\d+) подряд\)\. После пяти подряд сеанс браузера сбросится сам\.")]
    private static partial Regex Http400Pause();

    [GeneratedRegex(@"Нет ответа уже (\d+) раз подряд\. Пауза (\d+) с, затем сбор продолжится\. Это не проверка Cloudflare\.")]
    private static partial Regex NoResponsePause();
}
