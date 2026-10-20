const D = window.ITCH_DEMO;
const state = { lang: "ru", route: "overview", tabs: {}, menus: {} };
const charts = [];

const T = (ru, en) => (state.lang === "en" ? en : ru);
const L = (v) => (v && typeof v === "object" && ("ru" in v || "en" in v) ? (v[state.lang] ?? v.ru ?? "") : (v ?? ""));
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const num = (v, d) => Fmt.n(v, d ?? 0, state.lang);
const pct = (v, d) => Fmt.pct(v, d ?? 0, state.lang);
const demo = () => T("В демонстрационной версии это недоступно.", "Not available in the demo.");

function quadClass(q) {
    return { BlueOcean: "itch-quadrant-blue", RedOcean: "itch-quadrant-red", QuietNiche: "itch-quadrant-quiet", Trap: "itch-quadrant-trap" }[q] || "itch-muted";
}
function quadName(q) {
    const map = {
        BlueOcean: T("Голубой океан", "Blue ocean"),
        RedOcean: T("Красный океан", "Red ocean"),
        QuietNiche: T("Тихая ниша", "Quiet niche"),
        Trap: T("Ловушка", "Trap"),
    };
    return map[q] || T("мало данных", "too little data");
}
function quadHint(q) {
    const map = {
        BlueOcean: T("Спрос выше медианы и игр меньше медианы — сюда проще войти.", "Demand above the median and fewer games than the median — the easiest place to enter."),
        RedOcean: T("Спрос высокий, и игр много: самой игре нужно выделяться.", "Demand is high, and so is the number of games: the game itself has to stand out."),
        QuietNiche: T("Игр мало и спрос скромный: проще быть замеченным, потолок аудитории ниже.", "Few games and modest demand: easier to be noticed, lower audience ceiling."),
        Trap: T("Игр много, а типичная получает мало оценок — отсюда труднее всего выбраться.", "Many games, and a typical one gets few ratings — the hardest place to break out."),
    };
    return map[q] || "";
}

function info(id) {
    return `<button type="button" class="info" data-info="${id}" aria-label="info"><span class="ms">info</span></button>`;
}

function field(label, value, menuId, itemsHtml) {
    return `<div class="field sel" data-menu="${menuId}">
        <span class="lab">${esc(label)}</span>
        <button type="button" class="val sel-toggle" style="text-align:left;cursor:pointer">${esc(value)}</button>
        <span class="ms chev">expand_more</span>
        <div class="menu" hidden>${itemsHtml}</div>
    </div>`;
}

function lockItems(labels, current) {
    return labels.map((x) => `<button type="button" class="sel-item${x === current ? " on" : ""}" data-lock>${esc(x)}</button>`).join("");
}

function gameCell(g) {
    const img = g.cover
        ? `<img src="${esc(g.cover)}" alt="" referrerpolicy="no-referrer" />`
        : `<span class="ph"></span>`;
    return `<div class="game">${img}<div style="min-width:0"><button class="linkish itch-game-title" data-go="game">${esc(g.title)}</button><div class="itch-game-sub">${esc(g.author || "")}</div></div></div>`;
}

function pager(from, to, total, sizes) {
    const size = to - from + 1;
    return `<div class="pager">
        <span>${esc(T("Строк:", "Rows:"))}</span>
        ${field("", String(size), "pagesize-" + from, lockItems((sizes || [10, 25, 50, 100, 250]).map(String), String(size)))}
        <span class="itch-num">${from}–${to} ${T("из", "of")} ${num(total)}</span>
        <button class="icon-btn demo-lock" type="button"><span class="ms">chevron_left</span></button>
        <button class="icon-btn demo-lock" type="button"><span class="ms">chevron_right</span></button>
    </div>`;
}

function exportBtn() {
    return `<div class="field sel" data-menu="export" style="width:42px;min-height:36px;border:0">
        <button type="button" class="icon-btn sel-toggle" aria-label="export"><span class="ms">download</span></button>
        <div class="menu" hidden style="right:0;left:auto;min-width:140px">
            <button type="button" class="sel-item" data-lock>CSV</button>
            <button type="button" class="sel-item" data-lock>Excel</button>
        </div>
    </div>`;
}

function searchBox() {
    return `<div class="field" style="width:220px"><span class="ms" style="margin-left:8px;font-size:18px;opacity:.6">search</span><input class="lock-type" placeholder="${esc(T("Поиск", "Search"))}" /></div>`;
}

function stub() {
    return `<div class="stub">${esc(demo())}</div>`;
}

function kpi(label, value, hint, color, tipId) {
    return `<div class="card"><div class="kpi-label">${esc(label)}${tipId ? info(tipId) : ""}</div><div class="kpi-value" ${color ? `style="color:${color}"` : ""}>${value}</div>${hint ? `<div class="kpi-hint">${hint}</div>` : ""}</div>`;
}

function linkGo(route, text) {
    return `<button class="linkish" data-go="${route}">${esc(text)}</button>`;
}

const F = () => D.filters;

function formulas() {
    const m = F().minRatings, s = F().success, w = F().weights;
    const mean = F().demandMean;
    return {
        median: {
            title: T("Медиана оценок", "Median ratings"),
            formula: T(`median(оценки | оценок ≥ ${m})`, `median(ratings | ratings ≥ ${m})`),
            text: T("Типичное число оценок у игры, которую хоть кто-то заметил. У большинства игр itch.io оценок нет, поэтому игры ниже порога не учитываются. Оценки — видимая часть аудитории: их оставляет примерно 1 игрок из 100.",
                "Typical rating count for a game somebody noticed. Most itch.io games have none, so games under the threshold are left out. Ratings are the visible part of the audience: about 1 player in 100 leaves one."),
        },
        mean: {
            title: T("Среднее число оценок", "Mean ratings"),
            formula: T(`mean(оценки | оценок ≥ ${m})`, `mean(ratings | ratings ≥ ${m})`),
            text: T("Среднее арифметическое по тем же играм, что и медиана. Медиана на itch.io часто равна 1 или 2: половина игр еле заметна, и соседние теги слипаются. Среднее учитывает хиты и лучше их разделяет.",
                "Arithmetic mean of the same games as the median. On itch.io the median is often 1 or 2, so neighboring tags stick together. The mean notices hits and separates them."),
        },
        adj: {
            title: mean ? T("Сглаженное среднее", "Smoothed mean") : T("Сглаженная медиана", "Smoothed median"),
            formula: mean
                ? T("adj = (n·среднее сегмента + k·среднее itch.io) / (n + k)", "adj = (n·segment mean + k·itch.io mean) / (n + k)")
                : T("adj = (n·медиана сегмента + k·медиана itch.io) / (n + k)", "adj = (n·segment median + k·itch.io median) / (n + k)"),
            text: T("n — игр сегмента с оценками выше порога, k — сила сглаживания (настройка). Маленькие сегменты притягиваются к общему уровню, чтобы пара удачных игр не делала нишу «самой востребованной».",
                "n is games in the segment above the ratings threshold, k is the smoothing strength. Small segments are pulled toward the global level so a few lucky games do not make a niche look hottest."),
        },
        p90: {
            title: "P75 / P90",
            formula: T("75-й и 90-й перцентили числа оценок", "75th and 90th percentiles of rating counts"),
            text: T("Сколько набирают лучшие 25% и 10% игр сегмента — потолок, до которого реально дорасти.",
                "What the best 25% and 10% of games in the segment collect — a ceiling you can actually aim at."),
        },
        success: {
            title: T("Шанс успеха", "Chance of success"),
            formula: T(`P = игр с ≥ ${s} оценками / все игры сегмента на itch.io\n95%: интервал Уилсона`, `P = games with ≥ ${s} ratings / every game in the segment on itch.io\n95%: Wilson interval`),
            text: T("Знаменатель — «(N results)» фильтра на itch.io, включая игры без оценок, когда в шапке включено «0 оценок». Выключенный переключатель считает шанс только среди игр с хотя бы одной оценкой.",
                "The denominator is the filter’s “(N results)” on itch.io, including unrated games when “0 ratings” is on. Off, the chance is only among games with at least one rating."),
        },
        dist: {
            title: T("Распределение", "Distribution"),
            text: T("Столбец «0» — оценка по счётчику itch.io (все игры каталога минус игры с оценками), остальные — по собранным играм.",
                "The “0” bar is estimated from the itch.io counter (every catalog game minus rated games); the rest use collected games."),
        },
        years: {
            title: T("Когорты по годам", "Year cohorts"),
            text: T("Линия — среднее число оценок у игр с порогом из шапки. Медиана года почти всегда 1 или 2 и не различает когорты. Старые игры успели накопить больше оценок, поэтому сравнивайте форму, а не уровни.",
                "The line is the mean rating count for games at the bar’s threshold. A year’s median is almost always 1 or 2 and does not separate cohorts. Older games have had longer to collect ratings, so compare the shape, not the levels."),
        },
        competition: {
            title: T("Конкуренция", "Competition"),
            formula: T("игр в сегменте на itch.io", "games in the segment on itch.io"),
            text: T("Сколько игр борется за ту же аудиторию. При включённом «0 оценок» это счётчик «(N results)», вместе с играми без оценок.",
                "How many games compete for the same audience. With “0 ratings” on, this is the “(N results)” counter, including unrated games."),
        },
        trend: {
            title: T("Тренд", "Trend"),
            formula: T("(доля среди успешных свежих лет) / (доля двумя годами раньше) − 1", "(share among successful recent years) / (share two years earlier) − 1"),
            text: T("Растёт ли ниша среди игр, которые набрали порог оценок" + (D.genres.cohorts ? `: ${D.genres.cohorts}` : "") + ". Сравниваются доли, поэтому общий рост itch.io не искажает тренд.",
                "Whether the niche is growing among games that cleared the ratings threshold" + (D.genres.cohorts ? `: ${D.genres.cohorts}` : "") + ". Shares are compared, so overall itch.io growth does not bend the trend."),
        },
        entry: {
            title: T("Порог входа", "Entry bar"),
            formula: T("медиана оценок игр на 1-й странице «Популярного»", "median ratings of games on page 1 of Popular"),
            text: T("Сколько оценок сейчас у игр, которые видны на первой странице тега. Первая страница — главный источник трафика внутри itch.io.",
                "How many ratings the games on the tag’s first page have right now. That page is the main source of traffic inside itch.io."),
        },
        opportunity: {
            title: "Opportunity score",
            formula: `100 · (${w.demand}·${T("Спрос", "Demand")} + ${w.success}·${T("Успех", "Success")} + ${w.competition}·(1 − ${T("Конкуренция", "Competition")}) + ${w.trend}·${T("Тренд", "Trend")}) / Σw`,
            text: T("Каждый компонент — перцентиль сегмента среди остальных (0…1). Веса меняются в настройках.",
                "Each part is the segment’s percentile among the others (0…1). Weights live in settings."),
        },
        quadrant: {
            title: T("Квадрант", "Quadrant"),
            formula: T("спрос ≥ медианы сегментов? × конкуренция ≥ медианы сегментов?", "demand ≥ median of segments? × competition ≥ median of segments?"),
            text: T("Голубой океан — спрос высокий, игр мало. Красный океан — спрос высокий, игр много. Тихая ниша — мало и того и другого. Ловушка — игр много, спрос низкий.",
                "Blue ocean — high demand, few games. Red ocean — high demand, many games. Quiet niche — little of both. Trap — many games, low demand."),
        },
        wr: {
            title: T("Взвешенный рейтинг", "Weighted rating"),
            formula: `WR = v/(v+m)·R + m/(v+m)·C,  m = ${F().bayes}`,
            text: T("R — средняя оценка игры, v — число оценок, C — средняя оценка по itch.io. Игры с 3 оценками по 5★ не обгоняют игры с сотнями оценок по 4,7★.",
                "R is the game’s average, v is the number of ratings, C is the itch.io average. A game with three 5★ ratings does not pass a game with hundreds of 4.7★ ratings."),
        },
        hot: {
            title: T("Горячее", "Hot"),
            formula: T("(Δоценок + a·Δкомментариев + b·рост позиции) · 7 / дней окна / (возраст + 2)^g", "(Δratings + a·Δcomments + b·rank gain) · 7 / window days / (age + 2)^g"),
            text: T("Прирост за последние дни между ежедневными снимками, приведённый к недельному темпу. Нужны снимки минимум за 2 дня.",
                "Growth between daily snapshots, scaled to a week. Needs snapshots from at least two days."),
        },
        zero: {
            title: T("Без единой оценки", "No ratings at all"),
            formula: T("(игр на itch.io − игр с оценками) / игр на itch.io", "(games on itch.io − rated games) / games on itch.io"),
            text: T("Большинство игр никто не оценивает. Поэтому медианы считаются только по играм с оценками выше порога.",
                "Most games are never rated. Medians therefore use only games above the ratings threshold."),
        },
        catalog: {
            title: T("Игр на itch.io", "Games on itch.io"),
            text: T("Счётчик «(N results)» списка всех игр itch.io; в базе — сколько из них уже собрано.",
                "The “(N results)” counter of the full itch.io game list; in the database is how many of them are collected."),
        },
        stars: {
            title: T("Средняя оценка", "Average rating"),
            text: T("Звёзды почти не различают игры: у большинства заметных игр 4–5★. Спрос лучше видно по числу оценок.",
                "Stars barely separate games: most visible ones sit at 4–5★. Demand shows up better in the rating count."),
        },
    };
}

function insightsHtml(items, max) {
    const list = (items || []).slice(0, max || 99);
    if (!list.length) return `<p class="itch-muted">${esc(T("Выводов пока нет — данных слишком мало.", "No conclusions yet — there is too little data."))}</p>`;
    return list.map((i) => {
        const href = insightHref(i.link);
        return `<div class="itch-insight ${esc(i.tone)} mb-3"><div style="font-weight:600">${esc(L(i.title))}</div><div class="itch-muted" style="font-size:.9rem">${esc(L(i.text))}</div>${href ? `<div><button class="linkish" data-go="${esc(href)}" style="font-size:.8rem">${esc(T("подробнее →", "details →"))}</button></div>` : ""}</div>`;
    }).join("");
}

function insightHref(link) {
    if (!link) return "";
    if (link.startsWith("/developer#")) return "developer?tab=" + link.split("#")[1];
    if (link.startsWith("/developer")) return "developer";
    if (link.startsWith("/tags")) return "tags";
    return "";
}

function gamesTable(rows, opts) {
    const o = opts || {};
    const showWr = o.wr !== false;
    const head = `<tr>${o.rank === false ? "" : `<th style="width:44px">#</th>`}<th>${esc(T("Игра", "Game"))}</th><th class="right">${esc(T("Оценок", "Ratings"))}</th><th class="right">★</th>${showWr ? `<th class="right">WR ${info("wr")}</th>` : ""}${o.hot ? `<th class="right">Hot ${info("hot")}</th>` : ""}<th class="right">${esc(T("Комм.", "Cmts"))}</th><th>${esc(T("Жанр", "Genre"))}</th><th>${esc(T("Выход", "Released"))}</th><th>${esc(T("Платформы", "Platforms"))}</th><th>${esc(T("Цена", "Price"))}</th></tr>`;
    const body = rows.map((g, i) => `<tr>
        ${o.rank === false ? "" : `<td class="itch-muted itch-num">${(o.offset || 0) + i + 1}</td>`}
        <td>${gameCell(g)}</td>
        <td class="right itch-num">${g.ratingsKnown ? num(g.ratings) : "?"}</td>
        <td class="right itch-num">${Fmt.stars(g.stars, state.lang)}</td>
        ${showWr ? `<td class="right itch-num">${g.wr == null ? "—" : num(g.wr, 2)}</td>` : ""}
        ${o.hot ? `<td class="right itch-num">${g.hot == null ? "—" : num(g.hot, 2)}</td>` : ""}
        <td class="right itch-num">${num(g.comments)}</td>
        <td>${esc(L(g.genre))}</td>
        <td class="itch-num">${esc(g.released)}</td>
        <td style="font-size:.78rem">${esc(g.platforms)}</td>
        <td style="font-size:.8rem">${esc(L(g.price))}</td>
    </tr>`).join("");
    return `<table class="data"><thead>${head}</thead><tbody>${body}</tbody></table>`;
}

function pageOverview() {
    const o = D.overview;
    const central = F().demandMean ? T("Среднее оценок", "Mean ratings") : T("Медиана оценок", "Median ratings");
    const kpis = `<div class="row-6">
        ${kpi(T("Игр на itch.io", "Games on itch.io"), esc(Fmt.short(o.catalogTotal, state.lang)), esc(T(`в базе ${num(o.known)}`, `in the database ${num(o.known)}`)), null, "catalog")}
        ${kpi(T("Без единой оценки", "No ratings at all"), esc(pct(o.zeroShare)), esc(T("доля игр каталога", "share of the catalog")), "#F5B84A", "zero")}
        ${kpi(central, esc(num(o.median, F().demandMean ? 1 : 0)), esc(T(`по ${num(o.qualified)} играм с ≥ ${F().minRatings}`, `across ${num(o.qualified)} games with ≥ ${F().minRatings}`)), null, F().demandMean ? "mean" : "median")}
        ${kpi(T("Лучшие 10%", "Top 10%"), esc(num(o.p90, 0) + "+"), esc(T("оценок (P90)", "ratings (P90)")), null, "p90")}
        ${kpi(T(`Шанс ${F().success}+ оценок`, `Chance of ${F().success}+ ratings`), esc(pct(o.successShare, 1)), `95%: ${esc(pct(o.successLow, 1))}–${esc(pct(o.successHigh, 1))}`, "#4CC38A", "success")}
        ${kpi(T("Средняя оценка", "Average rating"), esc(Fmt.stars(o.stars, state.lang)), esc(T("медиана среди игр с оценками", "median among rated games")), null, "stars")}
    </div>`;
    const niches = (o.niches || []).map((s) => `<div style="display:flex;align-items:center;gap:8px;padding:3px 0">
        <button class="linkish" data-go="tag" style="min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(L(s.name))}</button>
        <span class="grow"></span>
        <span class="itch-muted" style="font-size:.8rem">${esc(T("медиана", "median"))} ${num(s.median, 0)} · ${esc(T("игр", "games"))} ${esc(Fmt.short(s.denominator, state.lang))}</span>
        <span class="itch-quadrant ${quadClass(s.quadrant)}" title="${esc(quadHint(s.quadrant))}">${esc(quadName(s.quadrant))}</span>
        <b class="itch-num" style="width:28px;text-align:right">${num(s.opportunity, 0)}</b>
    </div>`).join("");
    const tops = (o.topGames || []).map((g) => `<div style="display:flex;align-items:center;gap:12px;padding:3px 0"><div style="flex:1;min-width:0">${gameCell(g)}</div><span class="itch-num">${num(g.ratings)} ${esc(T("оц.", "rtg"))}</span></div>`).join("");
    return `<h1 class="page-title">${esc(T("Обзор", "Overview"))}</h1>
        <p class="lead">${esc(T("Состояние itch.io по собранным данным. Все цифры учитывают фильтры «18+», «0 оценок» и «Мин. оценок» в шапке.", "itch.io as collected. Every figure follows the 18+, 0 ratings and minimum-ratings filters in the bar."))}</p>
        ${kpis}
        <div class="row-5-7">
            <div class="card"><div class="section-title">${esc(T("Главные выводы", "Main conclusions"))}</div>${insightsHtml(o.insights, 7)}
                <button class="linkish" data-go="developer">${esc(T("Все выводы для разработчика", "All conclusions for developers"))} →</button></div>
            <div>
                <div class="card" style="margin-bottom:16px"><div class="section-title">${esc(T("Сколько оценок набирают игры", "How many ratings games collect"))} ${info("dist")}</div><div id="chart-dist"></div></div>
                <div class="card"><div class="section-title">${esc(T("По годам выхода", "By release year"))} ${info("years")}</div><div id="chart-years"></div>
                ${o.withYear < o.known * 0.8 ? `<div class="itch-muted" style="font-size:.75rem;margin-top:6px">${esc(T(`Год выхода известен у ${num(o.withYear)} из ${num(o.known)} игр в базе. Остальные годы появятся после глубокого сбора (списки по годам и страницы игр).`, `Release year is known for ${num(o.withYear)} of ${num(o.known)} games in the database. The rest appear after a deep collection (year lists and game pages).`))}</div>` : ""}
                </div>
            </div>
        </div>
        <div class="row-2">
            <div class="card"><div class="section-title">${esc(F().demandMean ? T("Жанры: спрос", "Genres: demand") : T("Жанры: спрос", "Genres: demand"))} ${info("adj")}</div><div id="chart-genres"></div></div>
            <div>
                <div class="card" style="margin-bottom:16px"><div style="display:flex"><div class="section-title">${esc(T("Лучшие ниши сейчас", "Best niches right now"))}</div><span class="grow"></span><button class="linkish" data-go="developer?tab=map" style="font-size:.8rem">${esc(T("карта возможностей →", "opportunity map →"))}</button></div>${niches}</div>
                <div class="card"><div style="display:flex"><div class="section-title">${esc(o.hotAvailable ? T("Горячее сейчас", "Hot right now") : T("Самые оценённые", "Most rated"))}</div><span class="grow"></span><button class="linkish" data-go="tops" style="font-size:.8rem">${esc(T("все топы →", "all tops →"))}</button></div>${tops}</div>
            </div>
        </div>`;
}

function pageGames() {
    const g = D.games;
    const title = state.lang === "en"
        ? `${num(g.total)} ${g.total === 1 ? "game" : "games"}`
        : `${num(g.total)} ${Fmt.plural(g.total, "игра", "игры", "игр")}`;
    const genres = [T("Все жанры", "All genres"), ...D.options.genres.map((x) => L(x.name))];
    const platforms = [T("Любая", "Any"), ...D.options.platforms.map((x) => L(x.name))];
    const prices = [T("Любая", "Any"), T("Бесплатные", "Free"), T("Своя цена", "Name your price"), T("Платные", "Paid")];
    const years = [T("Любой", "Any"), ...D.options.years.map(String)];
    const sorts = [T("Больше оценок", "Most ratings"), T("Взвешенный рейтинг", "Weighted rating"), T("Горячее", "Hot"), T("Прирост оценок", "Rating gain"), T("Новые", "Newest"), T("Больше комментариев", "Most comments")];
    const tags = D.options.tags.map((t) => L(t.name));
    return `<h1 class="page-title">${esc(T("Игры", "Games"))}</h1>
        <p class="lead">${esc(T("Все собранные игры с фильтрами. Клик по названию открывает карточку игры с историей и позициями.", "Every collected game, with filters. The title opens the card with history and positions."))}</p>
        <div class="card" style="margin-bottom:12px"><div class="filters">
            <div class="field"><span class="lab">${esc(T("Поиск", "Search"))}</span><span class="ms" style="margin-left:8px;font-size:18px;opacity:.55">search</span><input class="lock-type" placeholder="${esc(T("название или автор", "title or author"))}" /></div>
            ${field(T("Жанр", "Genre"), genres[0], "genre", lockItems(genres, genres[0]))}
            <div class="field sel" data-menu="tag"><span class="lab">${esc(T("Тег", "Tag"))}</span><input class="sel-toggle lock-type" placeholder="" style="cursor:pointer" /><div class="menu" hidden>${tags.map((t) => `<button type="button" class="sel-item" data-lock>${esc(t)}</button>`).join("")}</div></div>
            ${field(T("Платформа", "Platform"), platforms[0], "platform", lockItems(platforms, platforms[0]))}
            ${field(T("Цена", "Price"), prices[0], "price", lockItems(prices, prices[0]))}
            ${field(T("Год", "Year"), years[0], "year", lockItems(years, years[0]))}
            ${field(T("Сортировка", "Sort"), sorts[0], "sort", lockItems(sorts, sorts[0]))}
        </div></div>
        <div class="card" style="padding:8px 8px 12px">
            <div class="toolbar"><h2>${esc(title)}</h2>${g.total > g.shown ? `<span class="itch-muted" style="font-size:.8rem">${esc(T(`показаны первые ${num(g.shown)}`, `showing the first ${num(g.shown)}`))}</span>` : ""}<span class="grow"></span>${exportBtn()}</div>
            ${gamesTable(g.rows, { hot: D.overview.hotAvailable })}
            ${pager(1, g.rows.length, g.shown, [10, 25, 50, 100, 250])}
        </div>`;
}

function pageTops() {
    const tab = state.tabs.tops || "demand";
    const tabs = [
        ["demand", T("По спросу", "By demand")],
        ["reviews", T("По отзывам", "By reviews")],
        ["hot", T("Горячее", "Hot")],
        ["rising", T("Новые и растущие", "New and rising")],
        ["featured", "Featured"],
        ["comments", T("Обсуждаемые", "Discussed")],
    ];
    const periods = [T("За всё время", "All time"), T("За последний год", "Last year"), T("За 90 дней", "Last 90 days"), T("За 30 дней", "Last 30 days")];
    const genres = [T("Все жанры", "All genres"), ...D.options.genres.map((x) => L(x.name))];
    let body;
    if (tab === "demand") {
        const t = D.tops;
        body = `<div class="card" style="padding:8px 8px 12px"><div class="toolbar"><h2>${esc(T("Больше всего оценок", "Most ratings"))}</h2><span class="itch-muted" style="font-size:.8rem">${esc(T("число оценок ≈ размер аудитории", "rating count ≈ audience size"))}</span><span class="grow"></span>${exportBtn()}</div>
            ${gamesTable(t.rows, {})}${pager(1, t.rows.length, Math.min(t.total, 500))}</div>`;
    } else body = `<div class="card">${stub()}</div>`;
    return `<div class="head-row"><h1 class="page-title" style="margin:0">${esc(T("Топы", "Tops"))}</h1><span class="grow"></span>
            ${field(T("Вышли", "Released"), periods[0], "period", lockItems(periods, periods[0]))}
            ${field(T("Жанр", "Genre"), genres[0], "tgenre", lockItems(genres, genres[0]))}
        </div>
        <p class="lead">${esc(T("Спрос — сколько людей оценили игру. Отзывы — качество с поправкой на малое число голосов. Горячее — кто набирает оценки прямо сейчас.", "Demand is how many people rated the game. Reviews are quality adjusted for a small vote count. Hot is who is collecting ratings right now."))}</p>
        <div class="tabs">${tabs.map(([id, name]) => `<button type="button" data-tab="tops:${id}" class="${tab === id ? "on" : ""}">${esc(name)}</button>`).join("")}</div>
        ${body}`;
}

function segTable(rows) {
    const central = F().demandMean ? T("Среднее", "Mean") : T("Медиана", "Median");
    const head = `<tr><th>${esc(T("Сегмент", "Segment"))}</th>
        <th class="right">${esc(T("Игр на itch.io", "Games on itch.io"))} ${info("competition")}</th>
        <th class="right">${esc(T(`С оценками ≥ ${F().minRatings}`, `With ≥ ${F().minRatings} ratings`))}</th>
        <th class="right">${esc(central)} ${info(F().demandMean ? "mean" : "median")}</th>
        <th class="right">${esc(T("Сглаж.", "Adj."))} ${info("adj")}</th>
        <th class="right">P75 / P90 ${info("p90")}</th>
        <th class="right">${esc(T(`Шанс ≥ ${F().success}`, `Chance ≥ ${F().success}`))} ${info("success")}</th>
        <th class="right">${esc(T("Тренд", "Trend"))} ${info("trend")}</th>
        <th class="right">${esc(T("Порог входа", "Entry bar"))} ${info("entry")}</th>
        <th>Opportunity ${info("opportunity")}</th>
        <th>${esc(T("Квадрант", "Quadrant"))} ${info("quadrant")}</th></tr>`;
    const body = rows.map((s) => {
        const trend = s.trend == null ? "—" : Fmt.signed(s.trend * 100, 0, "%", state.lang);
        const color = s.trend == null ? "" : s.trend >= 0.15 ? "#4CC38A" : s.trend <= -0.15 ? "#FF8A8A" : "";
        const tip = T(`95%: ${pct(s.successLow, 1)} – ${pct(s.successHigh, 1)}; успешных ${num(s.successes)} из ${num(s.denominator)}`,
            `95%: ${pct(s.successLow, 1)} – ${pct(s.successHigh, 1)}; ${num(s.successes)} successes of ${num(s.denominator)}`);
        return `<tr>
            <td><button class="linkish" data-go="tag">${esc(L(s.name))}</button>${s.small ? ` <span class="itch-muted" style="font-size:.75rem">${esc(T("мало данных", "thin data"))}</span>` : ""}</td>
            <td class="right itch-num">${s.estimated ? "≈ " : ""}${num(s.denominator)}</td>
            <td class="right itch-num">${num(s.qualified)}</td>
            <td class="right itch-num">${num(s.central, F().demandMean ? 1 : 0)}</td>
            <td class="right itch-num"><b>${num(s.adjusted, 1)}</b></td>
            <td class="right itch-num">${num(s.p75, 0)} / ${num(s.p90, 0)}</td>
            <td class="right itch-num" title="${esc(tip)}">${pct(s.success, 1)}</td>
            <td class="right itch-num" style="color:${color}">${esc(trend)}</td>
            <td class="right itch-num">${s.entry == null ? "—" : num(s.entry, 0)}</td>
            <td>${s.opportunity == null ? "—" : `<span class="progress"><span style="width:${Math.max(0, Math.min(100, s.opportunity))}%"></span></span> <span class="itch-num">${num(s.opportunity, 0)}</span>`}</td>
            <td><span class="itch-quadrant ${quadClass(s.quadrant)}" title="${esc(quadHint(s.quadrant))}">${esc(quadName(s.quadrant))}</span></td>
        </tr>`;
    }).join("");
    return `<table class="data"><thead>${head}</thead><tbody>${body}</tbody></table>`;
}

function pageTags() {
    const tab = state.tabs.tags || "genres";
    const c = D.counts;
    const tabs = [
        ["genres", `${T("Жанры", "Genres")} · ${c.genres}`],
        ["tags", `${T("Теги", "Tags")} · ${c.tags}`],
        ["engines", `${T("Движки", "Engines")} · ${c.engines}`],
        ["formats", `${T("Форматы", "Formats")} · ${c.formats}`],
        ["languages", `${T("Языки", "Languages")} · ${c.languages}`],
        ["catalog", T("Каталог itch.io", "itch.io catalog")],
    ];
    let body = `<div class="card">${stub()}</div>`;
    if (tab === "genres") {
        body = `<div class="row-2">
            <div class="card"><div class="section-title">${esc(F().demandMean ? T("Спрос: сглаженное среднее оценок", "Demand: smoothed mean ratings") : T("Спрос: сглаженная медиана оценок", "Demand: smoothed median ratings"))}</div><div id="chart-gdemand"></div></div>
            <div class="card"><div class="section-title">${esc(T("Конкуренция: игр на itch.io", "Competition: games on itch.io"))}</div><div id="chart-gsupply"></div></div>
        </div>
        <div class="card" style="padding:8px 8px 12px">
            <div class="toolbar"><span class="grow"></span>${searchBox()}
                <span class="sw demo-lock"><i></i>${esc(T("Скрыть малые", "Hide small"))}</span>
                <span style="font-size:.8rem" class="itch-muted">${esc(T("Порог, игр", "Game floor"))}</span>
                ${field("", String(F().smallGames), "floor", lockItems(["15", "50", "150", "500"], String(F().smallGames)))}
                <span class="chip on demo-lock">${esc(F().demandMean ? T("Среднее", "Mean") : T("Медиана", "Median"))}</span>
                ${exportBtn()}
            </div>
            ${segTable(D.genres.rows)}
            ${pager(1, D.genres.rows.length, D.counts.genres, [15, 25, 50, 100, 500])}
        </div>`;
    }
    return `<h1 class="page-title">${esc(T("Теги и жанры", "Tags and genres"))}</h1>
        <p class="lead">${esc(T(`Спрос, конкуренция и шансы каждого сегмента. Медианы считаются по играм с ≥ ${F().minRatings} оценками. При включённом «0 оценок» конкуренция и шанс делятся на счётчик всего списка, включая игры без оценок. Клик по названию открывает подробности сегмента.`,
            `Demand, competition and the odds of each segment. Medians use games with ≥ ${F().minRatings} ratings. With “0 ratings” on, competition and chance use the full list counter, including unrated games. The name opens the segment page.`))}</p>
        <div class="tabs">${tabs.map(([id, name]) => `<button type="button" data-tab="tags:${id}" class="${tab === id ? "on" : ""}">${esc(name)}</button>`).join("")}</div>
        ${body}`;
}

function pageDeveloper() {
    const tab = state.tabs.developer || "insights";
    const tabs = [
        ["insights", T("Выводы", "Conclusions"), "tips_and_updates"],
        ["map", T("Карта возможностей", "Opportunity map"), "bubble_chart"],
        ["niches", T("Ниши", "Niches"), "leaderboard"],
        ["formats", T("Форматы", "Formats"), "devices"],
        ["combos", T("Сочетания тегов", "Tag combinations"), "join_inner"],
        ["drivers", T("Факторы успеха", "Success factors"), "insights"],
        ["concept", T("Проверка идеи", "Idea check"), "lightbulb"],
    ];
    const dev = D.developer;
    let body = `<div class="card">${stub()}</div>`;
    if (tab === "insights") {
        const bits = [];
        if (dev.age) bits.push(`<div class="itch-insight info"><b>${esc(T("Возраст игры.", "Age of the game."))}</b> ${esc(T(`При прочих равных игра вдвое старше имеет в ${Fmt.times(dev.age, state.lang)} больше оценок: оценки копятся годами, поэтому старые хиты сравнивают с новыми только по когортам.`, `All else equal, a game twice as old has ${Fmt.times(dev.age, state.lang)} the ratings: ratings accumulate for years, so old hits are compared with new ones only inside a cohort.`))}</div>`);
        if (dev.screenshots) bits.push(`<div class="itch-insight ${dev.screenshots > 1.05 ? "good" : "info"}"><b>${esc(T("Оформление страницы.", "Page presentation."))}</b> ${esc(T(`Вдвое больше скриншотов — ${Fmt.times(dev.screenshots, state.lang)} к оценкам. Страница — это витрина: обложка, скриншоты и короткое описание решают, откроют ли игру.`, `Twice as many screenshots is ${Fmt.times(dev.screenshots, state.lang)} the ratings. The page is the storefront: cover, screenshots and a short description decide whether the game is opened.`))}</div>`);
        bits.push(...(dev.drivers || []).map((d) => `<div class="itch-insight ${d.good ? "good" : "warn"}"><b>${esc(L(d.name))}</b> <span class="itch-muted">(${esc(L(d.group).toLowerCase())})</span> — ${esc(T(`${Fmt.times(d.multiplier, state.lang)} к оценкам при прочих равных, шанс ${F().success}+ оценок ${pct(d.successWith)} против ${pct(d.successWithout)}.`, `${Fmt.times(d.multiplier, state.lang)} the ratings, all else equal; chance of ${F().success}+ ratings is ${pct(d.successWith)} versus ${pct(d.successWithout)}.`))}</div>`));
        if (dev.sample < 200) bits.push(`<p class="itch-muted">${esc(T(`Модели нужно ≥ 200 игр с прочитанными страницами (сейчас ${num(dev.sample)}).`, `The model needs ≥ 200 games with pages read (currently ${num(dev.sample)}).`))}</p>`);
        body = `<div class="row-7-5">
            <div class="card"><div class="section-title">${esc(T("Выводы по данным", "What the data says"))}</div>${insightsHtml(dev.insights)}</div>
            <div>
                <div class="card" style="margin-bottom:16px"><div class="section-title">${esc(T("От чего зависит число оценок", "What the rating count depends on"))}</div>${bits.join("")}
                    <button class="linkish" data-tab="developer:drivers">${esc(T("Все факторы", "All factors"))} →</button></div>
                <div class="card"><div class="section-title">${esc(T("Как читать показатели", "How to read the figures"))}</div>
                    <table class="kv">
                        <tr><td>${esc(T("Медиана / среднее", "Median / mean"))}</td><td>${esc(T(`сколько оценок набирает игра с ≥ ${F().minRatings} оценками. Медиана часто равна 1–2 и слипает теги; «Среднее» рядом с таблицей и картой учитывает хиты и лучше их разделяет.`, `how many ratings a game with ≥ ${F().minRatings} ratings collects. The median is often 1–2 and piles tags together; “Mean” next to the table and the map counts hits and separates them better.`))}</td></tr>
                        <tr><td>${esc(T("Шанс успеха", "Success chance"))}</td><td>${esc(T(`доля игр сегмента, набравших ≥ ${F().success} оценок.`, `share of the segment’s games that reached ≥ ${F().success} ratings.`))}</td></tr>
                        <tr><td>${esc(T("Конкуренция", "Competition"))}</td><td>${esc(T("сколько игр в сегменте.", "how many games are in the segment."))}</td></tr>
                        <tr><td>${esc(T("Тренд", "Trend"))}</td><td>${esc(T("как меняется доля сегмента среди успешных игр в новых когортах против старых.", "how the segment’s share among successful games changes in newer cohorts versus older ones."))}</td></tr>
                        <tr><td>Opportunity</td><td>${esc(T("0–100: спрос, шанс, низкая конкуренция и рост, взвешенные в настройках.", "0–100: demand, chance, low competition and growth, weighted in settings."))}</td></tr>
                        <tr><td>${esc(T("Квадранты", "Quadrants"))}</td><td>${esc(T("спрос выше/ниже медианы × игр больше/меньше медианы.", "demand above/below the median × more/fewer games than the median."))}</td></tr>
                    </table>
                </div>
            </div>
        </div>`;
    } else if (tab === "map") {
        const map = dev.map;
        const cards = (map.quadrants || []).map((q) => `<div class="card" style="border-top:3px solid ${q.color}"><div style="color:${q.color};font-weight:700">${esc(L(q.name))}</div><div class="itch-muted" style="font-size:.75rem;margin-bottom:8px">${esc(L(q.hint))}</div>${(q.items || []).map((s) => `<div style="display:flex;gap:8px;padding:3px 0;font-size:.86rem"><button class="linkish" data-go="tag" style="flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis">${esc(L(s.name))}</button><span class="itch-muted">${num(s.value, F().demandMean ? 1 : 0)}</span><b class="itch-num">${num(s.opportunity, 0)}</b></div>`).join("") || `<div class="itch-muted">${esc(T("Нет сегментов.", "No segments."))}</div>`}</div>`).join("");
        body = `<div class="card" style="margin-bottom:16px">
            <div class="head-row"><div class="section-title" style="margin:0">${esc(T("Спрос против конкуренции", "Demand versus competition"))} ${info("quadrant")}</div><span class="grow"></span>
                <span class="chip demo-lock">${esc(T("Теги", "Tags"))}</span><span class="chip demo-lock">${esc(T("Жанры", "Genres"))}</span><span class="chip demo-lock">${esc(T("Форматы", "Formats"))}</span><span class="chip on">${esc(T("показана выборка", "sample shown"))}</span>
            </div>
            ${mapSvg(map)}
            <div class="itch-muted" style="font-size:.75rem;margin-top:6px">${esc(T("В демонстрации на карте небольшая выборка ниш. Пунктир — медианы среди показанных. Размер точки — шанс набрать порог оценок.", "The demo map shows a small set of niches. Dashed lines are the medians of the points shown. Point size is the chance of clearing the ratings threshold."))}</div>
        </div><div class="row-4">${cards}</div>`;
    } else if (tab === "concept") {
        const genres = [T("Любой", "Any"), ...D.options.genres.map((x) => L(x.name))];
        const platforms = [T("Любая", "Any"), T("Скачиваемая (Win / Mac / Linux)", "Downloadable (Win / Mac / Linux)"), ...D.options.platforms.map((x) => L(x.name))];
        const prices = [T("Любая", "Any"), T("Бесплатная", "Free"), T("Платная", "Paid")];
        const tags = D.options.tags.map((t) => L(t.name));
        body = `<div class="card"><p class="itch-muted">${esc(T("Опишите игру тегами, жанром, платформой и ценой. Сравним с похожими играми из базы и спросим itch.io, сколько игр с такой комбинацией уже есть.", "Describe the game with tags, a genre, a platform, and a price. We compare it with similar games in the database and ask itch.io how many games already use that combination."))}</p>
            <div class="filters" style="grid-template-columns: 2fr 1fr 1.3fr 1fr auto">
                <div class="field sel" data-menu="ctag"><span class="lab">${esc(T("Добавить тег", "Add a tag"))}</span><input class="sel-toggle" /><div class="menu" hidden>${tags.map((t) => `<button type="button" class="sel-item" data-lock>${esc(t)}</button>`).join("")}</div></div>
                ${field(T("Жанр", "Genre"), genres[0], "cgenre", lockItems(genres, genres[0]))}
                ${field(T("Платформа", "Platform"), platforms[0], "cplat", lockItems(platforms, platforms[0]))}
                ${field(T("Цена", "Price"), prices[0], "cprice", lockItems(prices, prices[0]))}
                <button class="btn fill demo-lock" type="button"><span class="ms fill">play_arrow</span>${esc(T("Проверить", "Check"))}</button>
            </div>
            <div class="itch-muted" style="font-size:.75rem;margin-top:8px">${esc(T("Теги не выбраны — будет проверен только жанр, платформа и цена.", "No tags selected — only genre, platform, and price will be checked."))}</div>
            <label class="sw demo-lock" style="margin-top:8px"><i></i>${esc(T("Спросить itch.io о числе конкурентов (1 запрос)", "Ask itch.io how many competitors there are (1 request)"))}</label>
        </div>`;
    }
    return `<h1 class="page-title">${esc(T("Для разработчика", "For developers"))}</h1>
        <p class="lead">${esc(T("Какие игры востребованы, где меньше конкуренции и что сопутствует успеху. Все цифры посчитаны по собранным данным с учётом фильтров в шапке.", "Which games find an audience, where competition is thinner, and what goes with success. Every figure uses the collected data and the filters in the bar."))}</p>
        <div class="tabs">${tabs.map(([id, name, icon]) => `<button type="button" data-tab="developer:${id}" class="${tab === id ? "on" : ""}"><span class="ms">${icon}</span>${esc(name)}</button>`).join("")}</div>
        ${body}`;
}

function mapSvg(map) {
    const ticksX = (map.xTicks || []).map((t) => `<g><line x1="${t.x}" x2="${t.x}" y1="16" y2="532" stroke="rgba(255,255,255,0.06)" /><text x="${t.x}" y="548" text-anchor="middle" class="itch-map-tick">${esc(t.label)}</text></g>`).join("");
    const ticksY = (map.yTicks || []).map((t) => `<g><line x1="64" x2="976" y1="${t.y}" y2="${t.y}" stroke="rgba(255,255,255,0.06)" /><text x="56" y="${t.y + 4}" text-anchor="end" class="itch-map-tick">${esc(t.label)}</text></g>`).join("");
    const pts = (map.points || []).map((p) => `<g class="itch-map-point" data-go="tag" style="cursor:pointer"><circle cx="${p.x}" cy="${p.y}" r="${p.r}" fill="${p.color}" fill-opacity="0.55" stroke="${p.color}" stroke-width="1.2" />${p.label ? `<text x="${p.labelX}" y="${p.labelY}" text-anchor="${p.anchor}" class="itch-map-label">${esc(L(p.name))}</text>` : ""}</g>`).join("");
    const q = (map.quadrants || []);
    const by = Object.fromEntries(q.map((x) => [x.key, x]));
    const label = (key, x, y, anchor) => by[key] ? `<text x="${x}" y="${y}" text-anchor="${anchor}" class="itch-map-quadrant" fill="${by[key].color}">${esc(L(by[key].name))}</text>` : "";
    return `<svg viewBox="0 0 1000 580" class="itch-map">
        <rect x="64" y="16" width="912" height="516" fill="rgba(255,255,255,0.015)" />
        ${ticksX}${ticksY}
        <line x1="${map.splitX}" x2="${map.splitX}" y1="16" y2="532" stroke="rgba(255,255,255,0.28)" stroke-dasharray="5 5" />
        <line x1="64" x2="976" y1="${map.splitY}" y2="${map.splitY}" stroke="rgba(255,255,255,0.28)" stroke-dasharray="5 5" />
        ${label("BlueOcean", 74, 34, "start")}${label("RedOcean", 966, 34, "end")}${label("QuietNiche", 74, 522, "start")}${label("Trap", 966, 522, "end")}
        ${pts}
        <text x="520" y="574" text-anchor="middle" class="itch-map-axis">${esc(T("Конкуренция: игр в сегменте на itch.io (лог. шкала) →", "Competition: games in the segment on itch.io (log scale) →"))}</text>
    </svg>`;
}

function pageDevelopers() {
    const d = D.developers;
    const exp = (d.experience || []).map((b) => `<tr><td>${esc(L(b.label))}</td><td class="right itch-num">${num(b.developers)}</td><td class="right itch-num">${num(b.games)}</td><td class="right itch-num"><b>${b.median == null ? "—" : num(b.median, 0)}</b></td><td class="right itch-num">${pct(b.success, 1)}</td></tr>`).join("");
    const grow = (d.growing || []).map((g) => `<div style="display:flex;gap:8px;align-items:center;padding:3px 0"><button class="linkish" data-go="dev" style="flex:1">${esc(g.name)}</button><span class="itch-muted itch-num" style="font-size:.82rem">${num(g.followers)} ${esc(T("подп.", "followers"))}</span><b class="itch-num" style="color:#4CC38A;width:80px;text-align:right">${Fmt.signed(g.delta, 0, "", state.lang)}</b></div>`).join("");
    const rows = (d.rows || []).map((r) => `<tr>
        <td><button class="linkish" data-go="dev">${esc(r.name)}</button></td>
        <td class="right itch-num">${num(r.followers)}</td>
        <td class="right itch-num">${Fmt.signed(r.delta, 0, "", state.lang)}</td>
        <td class="right itch-num">${num(r.games)}</td>
        <td class="right itch-num">${num(r.sum)}</td>
        <td class="right itch-num">${r.median == null ? "—" : num(r.median, 0)}</td>
        <td>${r.best ? `<button class="linkish" data-go="game">${esc(r.best)}</button> <span class="itch-muted" style="font-size:.8rem">· ${num(r.bestRatings)}</span>` : "—"}</td>
    </tr>`).join("");
    return `<h1 class="page-title">${esc(T("Разработчики", "Developers"))}</h1>
        <p class="lead">${esc(T("Авторы игр из базы: у кого больше подписчиков и оценок, кто быстро растёт и как опыт автора связан с успехом его игр.", "Authors in the database: who has more followers and ratings, who is growing fast, and how an author's experience relates to how their games do."))}</p>
        <div class="row-4">
            ${kpi(T("Разработчиков в базе", "Developers in the database"), num(d.total), esc(T(`с известными подписчиками: ${num(d.withFollowers)}`, `with a known follower count: ${num(d.withFollowers)}`)))}
            ${kpi(T("Одна игра в базе", "One game in the database"), pct(d.singleShare), esc(T("доля авторов", "share of authors")))}
            ${kpi(T("Топ-1% авторов", "Top 1% of authors"), pct(d.top1Share), esc(T("всех оценок в базе", "of all ratings in the database")))}
            ${kpi(T("Медиана подписчиков", "Median followers"), num(d.medianFollowers, 0), esc(T("у авторов с известным числом", "among authors with a known count")))}
        </div>
        <div class="row-2">
            <div class="card"><div class="section-title">${esc(T("Опыт автора и оценки его игр", "Author experience and ratings"))}</div>
                <table class="data"><thead><tr><th>${esc(T("Игр у автора", "Games by the author"))}</th><th class="right">${esc(T("Авторов", "Authors"))}</th><th class="right">${esc(T("Игр", "Games"))}</th><th class="right">${esc(T("Медиана оценок", "Median ratings"))}</th><th class="right">${esc(T(`Шанс ${F().success}+`, `Chance of ${F().success}+`))}</th></tr></thead><tbody>${exp}</tbody></table></div>
            <div class="card"><div class="section-title">${esc(T("Быстрее всех набирают подписчиков", "Fastest follower growth"))}</div>${grow || `<p class="itch-muted">${esc(T("Роста подписчиков в срезе нет.", "No follower growth in this slice."))}</p>`}</div>
        </div>
        <div class="card" style="padding:8px 8px 12px"><div class="toolbar"><h2>${esc(T("Все разработчики", "All developers"))}</h2><span class="grow"></span>${searchBox()}<span class="sw demo-lock"><i></i>${esc(T("С подписчиками", "With followers"))}</span>${exportBtn()}</div>
            <table class="data"><thead><tr><th>${esc(T("Разработчик", "Developer"))}</th><th class="right">${esc(T("Подписчики", "Followers"))}</th><th class="right">${esc(T("За 30 дней", "Last 30 days"))}</th><th class="right">${esc(T("Игр", "Games"))}</th><th class="right">${esc(T("Оценок всего", "Ratings total"))}</th><th class="right">${esc(T("Медиана", "Median"))}</th><th>${esc(T("Самая оценённая игра", "Highest-rated game"))}</th></tr></thead><tbody>${rows}</tbody></table>
            ${pager(1, d.rows.length, d.total)}
        </div>`;
}

function jamState(s) {
    return {
        upcoming: [T("скоро", "soon"), "#5CC8FA"],
        active: [T("идёт", "live"), "#4CC38A"],
        voting: [T("голосование", "voting"), "#F5B84A"],
        ended: [T("завершён", "ended"), "inherit"],
    }[s] || ["—", "inherit"];
}

function pageJams() {
    const j = D.jams;
    const rows = (j.rows || []).map((r) => {
        const [name, color] = jamState(r.state);
        return `<tr><td><button class="linkish" data-go="jam">${esc(r.title)}</button>${r.featured ? ` <span style="color:#F5B84A;font-size:.75rem">featured</span>` : ""}${r.hosts ? `<div class="itch-game-sub">${esc(r.hosts)}</div>` : ""}</td>
            <td class="itch-num">${esc(r.start)}</td><td class="itch-num">${esc(r.end)}</td>
            <td class="right itch-num">${num(r.joined)}</td><td class="right itch-num">${num(r.submissions)}</td>
            <td class="right itch-num">${pct(r.conversion)}</td><td class="right itch-num">${r.entries > 0 ? num(r.entries) : "—"}</td>
            <td style="color:${color}">${esc(name)}${r.ranked ? ` <span class="itch-muted">· ${esc(T("с оценками", "ranked"))}</span>` : ""}</td></tr>`;
    }).join("");
    return `<h1 class="page-title">${esc(T("Джемы", "Jams"))}</h1>
        <p class="lead">${esc(T("Джемы — быстрый способ получить первых игроков и оценки. Здесь видно, какие джемы крупные, сколько участников доходит до сдачи и как игры с джемов живут потом.", "Jams are a fast way to get the first players and ratings. This shows which jams are large, how many participants finish a game, and how jam games do afterwards."))}</p>
        <div class="row-6">
            ${kpi(T("Джемов в базе", "Jams in the database"), num(j.total), esc(T(`с участниками: ${num(j.withEntries)}`, `with entries: ${num(j.withEntries)}`)))}
            ${kpi(T("Идут сейчас", "Live now"), num(j.active), esc(T(`скоро начнутся: ${num(j.upcoming)}`, `starting soon: ${num(j.upcoming)}`)), "#4CC38A")}
            ${kpi(T("Медиана работ", "Median submissions"), num(j.medianSubmissions, 0), esc(T("на джем", "per jam")))}
            ${kpi(T("Доходят до сдачи", "Finish a submission"), pct(j.medianConversion), esc(T("медиана: работ / записавшихся", "median: submissions / joined")))}
            ${kpi(T("Игр с джемов", "Games from jams"), num(j.jamGames), esc(T(`медиана оценок ${num(j.jamMedian, 0)} против ${num(j.otherMedian, 0)}`, `median ratings ${num(j.jamMedian, 0)} vs ${num(j.otherMedian, 0)}`)))}
            ${kpi(T(`Шанс ${F().success}+ у игр с джемов`, `Chance of ${F().success}+ for jam games`), pct(j.jamSuccess, 1), esc(T(`у остальных ${pct(j.otherSuccess, 1)}`, `other games ${pct(j.otherSuccess, 1)}`)))}
        </div>
        <div class="card" style="padding:8px 8px 12px"><div class="toolbar"><h2>${esc(T("Список джемов", "Jam list"))}</h2><span class="grow"></span>
            <span class="chip on">${esc(T("Все", "All"))}</span><span class="chip demo-lock">${esc(T("Идут", "Live"))}</span><span class="chip demo-lock">${esc(T("Скоро", "Soon"))}</span><span class="chip demo-lock">${esc(T("Прошли", "Ended"))}</span>
            ${searchBox()}${exportBtn()}</div>
            <table class="data"><thead><tr><th>${esc(T("Джем", "Jam"))}</th><th>${esc(T("Начало", "Starts"))}</th><th>${esc(T("Сдача до", "Submit by"))}</th><th class="right">${esc(T("Записались", "Joined"))}</th><th class="right">${esc(T("Работ", "Submissions"))}</th><th class="right">${esc(T("Сдали", "Finished"))}</th><th class="right">${esc(T("В базе", "In database"))}</th><th>${esc(T("Статус", "Status"))}</th></tr></thead><tbody>${rows}</tbody></table>
            ${pager(1, j.rows.length, j.total)}
        </div>`;
}

function pageMyGame() {
    return `<h1 class="page-title">${esc(T("Моя игра", "My game"))}</h1>
        <p class="lead">${esc(T("Следите за позициями своих и чужих игр в списках «Популярное» по тегам. С ключом API itch.io приложение узнает реальные просмотры и скачивания ваших игр и уточнит по ним оценку аудитории для всех остальных игр.", "Watch positions of your games and others in Popular lists by tag. With an itch.io API key the app learns the real views and downloads of your games and uses them to refine the audience estimate for every other game."))}</p>
        <div class="row-5-7">
            <div class="card"><div class="section-title">${esc(T("Ключ API itch.io", "itch.io API key"))}</div>
                <p class="itch-muted" style="font-size:.9rem">${esc(T("Создайте ключ на", "Create a key at"))} <a href="https://itch.io/user/settings/api-keys" target="_blank" rel="noreferrer">itch.io/user/settings/api-keys</a>. ${esc(T("Он хранится только на этом компьютере в зашифрованном виде и используется для одного запроса к api.itch.io.", "It stays only on this computer, encrypted, and is used for a single request to api.itch.io."))}</p>
                <div style="display:flex;gap:8px;align-items:center"><div class="field" style="flex:1"><span class="lab">${esc(T("Ключ API", "API key"))}</span><input class="lock-type" type="password" /></div><button class="btn demo-lock" type="button">${esc(T("Сохранить", "Save"))}</button></div>
                <div style="margin-top:12px"><button class="btn fill demo-lock" type="button"><span class="ms fill">cloud_download</span>${esc(T("Загрузить статистику моих игр", "Load my games' stats"))}</button></div>
            </div>
            <div class="card"><div class="section-title">${esc(T("Калибровка оценки аудитории", "Audience estimate calibration"))}</div>
                <table class="data"><thead><tr><th></th><th class="right">${esc(T("Сейчас в расчётах", "Used in calculations"))}</th><th class="right">${esc(T("По вашим играм", "From your games"))}</th></tr></thead>
                <tbody>
                    <tr><td>${esc(T("Просмотров на одну оценку", "Views per rating"))}</td><td class="right itch-num">${num(F().viewsPerRating, 0)} <span class="itch-muted">${esc(F().calibrated ? T("калибровка", "calibrated") : T("по умолчанию", "default"))}</span></td><td class="right">—</td></tr>
                    <tr><td>${esc(T("Скачиваний на одну оценку", "Downloads per rating"))}</td><td class="right itch-num">${num(F().downloadsPerRating, 1)} <span class="itch-muted">${esc(T("по умолчанию", "default"))}</span></td><td class="right">—</td></tr>
                </tbody></table>
                <p class="itch-muted" style="font-size:.75rem">${esc(T("Для калибровки нужна хотя бы одна ваша игра с ≥ 5 оценками, которая уже есть в базе.", "Calibration needs at least one of your games with ≥ 5 ratings that is already in the database."))}</p>
                <button class="btn fill demo-lock" type="button" disabled style="opacity:.45">${esc(T("Применить калибровку", "Apply calibration"))}</button>
                <button class="btn demo-lock" type="button" style="border:0">${esc(T("Сбросить", "Reset"))}</button>
            </div>
        </div>
        <div class="card"><div style="display:flex;align-items:center"><h2 style="margin:0;font-size:1.05rem;font-weight:500">${esc(T("Отслеживаемые игры", "Tracked games"))}</h2><span class="grow"></span><button class="btn demo-lock" type="button">${esc(T("Проверить сейчас", "Check now"))}</button></div>
            <p class="itch-muted" style="font-size:.9rem">${esc(T("Проверка читает страницу игры и ищет её в «Популярном» по каждому её тегу (до N страниц по 36 игр). Ежедневный сбор делает это сам.", "The check reads the game page and looks for it in Popular for each of its tags (up to N pages of 36 games). The daily crawl does this on its own."))}</p>
            <div style="display:flex;gap:8px;align-items:center">
                <div class="field" style="flex:1"><span class="lab">${esc(T("Ссылка на игру", "Game URL"))}</span><input class="lock-type" value="https://author.itch.io/game" /></div>
                <div class="field" style="width:180px"><span class="lab">${esc(T("Заметка", "Note"))}</span><input class="lock-type" /></div>
                ${field(T("Страниц", "Pages"), "5", "pages", lockItems(["1", "2", "3", "5", "10"], "5"))}
                <label class="sw demo-lock"><i></i>${esc(T("Моя", "Mine"))}</label>
                <button class="btn demo-lock" type="button">${esc(T("Добавить", "Add"))}</button>
            </div>
            <p class="itch-muted" style="margin-top:16px">${esc(T("Пока ничего не отслеживается. Добавьте ссылку выше или нажмите «Отслеживать позиции» на странице любой игры.", "Nothing is tracked yet. Add a link above or press “Track positions” on any game page."))}</p>
        </div>`;
}

function profileText(p) {
    const c = D.collection;
    if (p.id === "QuickStart") return T(
        `Первые данные за полчаса: каталог тегов, топ по оценкам по всему itch.io, за два последних года, по 18 жанрам и ${c.quickTopTags} крупнейшим тегам; позиции в popular, новые релизы, featured, ${c.quickDetails} страниц игр, профили и джемы. Статистика приблизительная — смещена к популярным играм, это отмечено меткой покрытия.`,
        `First data in about half an hour: the tag catalog, top rated across itch.io, the last two years, 18 genres and the ${c.quickTopTags} largest tags; popular positions, new releases, featured, ${c.quickDetails} game pages, profiles and jams. The stats are approximate and skewed toward popular games, which the coverage label marks.`);
    if (p.id === "Daily") return T(
        `Раз в сутки или по кнопке «Обновить»: счётчики списков, позиции в popular по ${c.dailyPopularTags} тегам, отслеживаемые игры, свежие оценки топов, новые релизы, ${c.dailyDetails} страниц игр, ${c.dailyProfiles} профилей, джемы и порция глубокого сбора. Ежедневные снимки дают тренды и «горячее».`,
        `Once a day, or from the Refresh button: listing counters, popular positions for ${c.dailyPopularTags} tags, tracked games, fresh ratings on the tops, new releases, ${c.dailyDetails} game pages, ${c.dailyProfiles} profiles, jams and a slice of the deep crawl. Daily snapshots produce trends and Hot.`);
    if (p.id === "GamePages") return T(
        `Только страницы игр: полные теги, дата выхода, движок, скриншоты и статус. Списки не качаются. Сначала игры с наибольшим числом оценок. Вкладки грузят страницы параллельно и ждут только свою загрузку, затем каждая отдыхает ${c.gamePageSeconds} с. Остановка сохраняет прогресс, следующий запуск продолжит с оставшихся.`,
        `Game pages only: full tags, release date, engine, screenshots and status. Lists are not downloaded. Games with the most ratings go first. Tabs load pages in parallel and wait only for their own load, then each rests ${c.gamePageSeconds} s. Stopping saves progress; the next run continues with what is left.`);
    return T(
        `Сначала счётчики списков (сколько всего игр у тега или жанра, включая нули — сами такие игры не скачиваются), затем страницы самых популярных игр и списки: годы, жанры, теги от ${c.deepMinTagProjects} проектов, движки и в конце 18+. Списки длиннее 200 страниц дробятся. Уже пройденное пропускается.`,
        `Listing counters first (how many games a tag or genre has, including zeros — those games themselves are not downloaded), then pages of the most popular games and the lists: years, genres, tags from ${c.deepMinTagProjects} projects, engines, and 18+ last. Lists longer than 200 pages are split. What is already done is skipped.`);
}
function profileName(id) {
    return { QuickStart: T("Быстрый старт", "Quick start"), Daily: T("Ежедневное обновление", "Daily update"), Deep: T("Глубокий сбор", "Deep crawl"), GamePages: T("Карточки игр", "Game pages") }[id] || id;
}

function numField(label, value, help) {
    return `<div class="field"><span class="lab">${esc(label)}</span><input class="lock-type" value="${esc(value)}" /></div>${help ? `<div class="help">${esc(help)}</div>` : ""}`;
}

function pageData() {
    const c = D.collection;
    const tab = state.tabs.data || "settings";
    const s = c.stats;
    const last = c.last;
    const statusName = { Completed: T("завершён", "completed"), Stopped: T("остановлен", "stopped"), Failed: T("ошибка", "failed"), Interrupted: T("прерван закрытием", "interrupted by closing"), Running: T("идёт", "running") }[last?.status] || "";
    const cards = (c.profiles || []).map((p) => `<div class="card profile"><div style="display:flex;gap:10px"><span class="ms fill" style="color:#FA5C5C">${p.id === "QuickStart" ? "rocket_launch" : p.id === "Daily" ? "today" : p.id === "GamePages" ? "web" : "database"}</span><div><h3>${esc(profileName(p.id))}</h3><div class="sub">${p.allPages ? esc(T(`все игры без карточки · осталось ${num(p.left)}`, `every game without a page · ${num(p.left)} left`)) : esc(T(`до ~${num(p.requests)} запросов ≈ ${L(p.duration)}`, `up to ~${num(p.requests)} requests ≈ ${L(p.duration)}`))}</div></div></div><p>${esc(profileText(p))}</p><div class="actions"><button class="btn ${p.recommended ? "fill" : ""} demo-lock" type="button"><span class="ms fill">play_arrow</span>${esc(T("Запустить", "Start"))}</button>${p.recommended ? `<span class="itch-muted" style="font-size:.75rem;margin-left:8px">${esc(T("рекомендуется сейчас", "recommended now"))}</span>` : ""}</div></div>`).join("");
    const years = (c.years || []).map((y) => `<tr><td>${y.year}</td><td class="itch-num">${num(y.total)}</td><td class="itch-num">${num(y.known)}${y.total ? ` (${pct(y.known / y.total)})` : ""}</td><td class="itch-num">${num(y.rated)}</td><td><span class="year-status ${y.done ? "ok" : y.fresh ? "run" : ""}">${esc(L(y.status))}</span></td></tr>`).join("");
    const st = c.settings;
    const settings = `<div class="settings-grid">
        <div><h3>${esc(T("Сеть", "Network"))}</h3>
            ${numField(T("Интервал списков и тегов, мс", "List and tag interval, ms"), st.requestIntervalMs, T("Пауза этой вкладки после списка или тега.", "This tab pauses after a list or tag."))}
            ${numField(T("Интервал страниц игр, мс", "Game page interval, ms"), st.gamePageIntervalMs)}
            ${numField(T("Параллельных запросов", "Parallel requests"), st.parallelRequests)}
            ${numField(T("Случайная добавка, мс", "Random extra delay, ms"), st.requestJitterMs)}
            ${numField(T("Паузы при Cloudflare, мин", "Cloudflare pauses, minutes"), st.pauses)}
            <div class="field"><span class="lab">User-Agent</span><input class="lock-type" value="${esc(st.userAgent)}" /></div>
        </div>
        <div><h3>${esc(T("Расписание", "Schedule"))}</h3>
            <label class="sw ${st.autoDailyEnabled ? "on" : ""} demo-lock"><i></i>${esc(T("Автоматическое ежедневное обновление, пока приложение открыто", "Automatic daily update while the app is open"))}</label>
            ${numField(T("Интервал, часов", "Interval, hours"), st.dailyIntervalHours)}
            <h3>${esc(T("Аналитика", "Analytics"))}</h3>
            ${numField(T("«Успех» — оценок не меньше", "“Success” — at least this many ratings"), st.successThreshold)}
            ${numField(T("Минимальная выборка сегмента", "Minimum segment sample"), st.minSegmentSample)}
            ${numField(T("Сглаживание медианы k", "Median smoothing k"), st.shrinkK)}
            ${numField(T("Взвешенный рейтинг: m", "Weighted rating: m"), st.bayesMinVotes, "WR = v/(v+m)·R + m/(v+m)·C")}
        </div>
        <div><h3>${esc(T("Opportunity score — веса", "Opportunity score — weights"))}</h3>
            ${numField(T("Спрос", "Demand"), st.weightDemand)}
            ${numField(T("Шанс успеха", "Chance of success"), st.weightSuccess)}
            ${numField(T("Низкая конкуренция", "Low competition"), st.weightCompetition)}
            ${numField(T("Тренд", "Trend"), st.weightTrend)}
            <h3>${esc(T("Горячее", "Hot"))}</h3>
            <div class="itch-muted" style="font-size:.75rem;margin-bottom:8px">Hot = (Δ + a·Δ + b·rank) / (age + 2)^g</div>
            <div style="display:flex;gap:8px">${numField("a", st.hotA)}${numField("b", st.hotB)}${numField("g", st.hotG)}</div>
        </div>
        <div><h3>${esc(T("Оценка аудитории", "Audience estimate"))}</h3>
            ${numField(T("Доля оценивающих от просмотров", "Share of viewers who rate"), st.raterShare)}
            ${numField(T("Нижняя", "Lower"), st.raterShareLow)}
            ${numField(T("Верхняя", "Upper"), st.raterShareHigh)}
            ${numField(T("Доля запусков в браузере", "Share of views that become browser plays"), st.webPlayShare)}
            ${numField(T("Доля скачиваний от просмотров", "Share of views that become downloads"), st.downloadShare)}
        </div>
        <div style="grid-column: span 2"><h3>${esc(T("Объём сбора", "Crawl volume"))}</h3>
            <div class="filters" style="grid-template-columns:repeat(4,1fr)">
                ${numField(T("Быстрый: тегов", "Quick: tags"), st.quickTopTags)}
                ${numField(T("Быстрый: стр. на тег", "Quick: pages per tag"), st.quickPagesPerList)}
                ${numField(T("Быстрый: страниц игр", "Quick: game pages"), st.quickDetails)}
                ${numField(T("Джемов с участниками", "Jams with entries per run"), st.jamEntriesPerRun)}
                ${numField(T("Ежедн.: тегов popular", "Daily: popular tags"), st.dailyPopularTags)}
                ${numField(T("Ежедн.: стр. popular", "Daily: popular pages"), st.dailyPopularPages)}
                ${numField(T("Ежедн.: стр. top-rated", "Daily: top-rated pages"), st.dailyTopRatedPages)}
                ${numField(T("Ежедн.: стр. новинок", "Daily: new-release pages"), st.dailyNewestMaxPages)}
                ${numField(T("Ежедн.: страниц игр", "Daily: game pages"), st.dailyDetails)}
                ${numField(T("Ежедн.: профилей", "Daily: profiles"), st.dailyProfiles)}
                ${numField(T("Глубокий: страниц списков", "Deep: list pages"), st.deepListPages)}
                ${numField(T("Карточки: страниц за запуск", "Pages: game pages per run"), st.gamePagesPerRun)}
            </div>
        </div>
    </div>
    <div style="margin-top:8px"><button class="btn fill demo-lock" type="button"><span class="ms fill">save</span>${esc(T("Сохранить", "Save"))}</button> <button class="btn demo-lock" type="button" style="border:0">${esc(T("Отменить изменения", "Discard changes"))}</button></div>`;
    return `<h1 class="page-title">${esc(T("Сбор данных", "Data collection"))}</h1>
        <p class="lead">${esc(T(`У itch.io нет открытого API каталога, поэтому ItchDB читает публичные страницы как обычный браузер. Одновременно открыто до ${c.tabs} вкладок. Каждая ждёт только свою загрузку, затем эта вкладка отдыхает: списки и теги — ${num(c.listFrom, 1)}–${num(c.listTo, 1)} с, страницы игр — ${c.gamePageSeconds} с. Данные хранятся локально, каждый запуск продолжает с места остановки. Сбор идёт только пока приложение открыто.`,
            `itch.io has no public catalog API, so ItchDB reads public pages like a normal browser. Up to ${c.tabs} tabs stay open at once. Each one waits only for its own load, then that tab rests: lists and tags for ${num(c.listFrom, 1)}–${num(c.listTo, 1)} s, game pages for ${c.gamePageSeconds} s. Data is stored locally, and each run continues from where it stopped. The crawl runs only while the app is open.`))}</p>
        <div class="row-7-5">
            <div class="card"><h2 style="margin:0 0 8px;font-size:1.15rem;font-weight:500">${esc(T("Текущий сбор", "Current crawl"))}</h2>
                <div class="itch-muted">${esc(T("Сейчас ничего не собирается.", "Nothing is being collected right now."))}</div>
                ${last ? `<div class="alert ok">${esc(T(`Последний запуск ${L(last.ago)}: ${statusName}.`, `Last run ${L(last.ago)}: ${statusName}.`))} ${esc(last.message || "")}</div>` : ""}
                <p style="font-size:.9rem">${esc(T("Автообновление:", "Auto-update:"))} <b>${c.schedule.auto ? esc(T(`раз в ${c.schedule.hours} ч`, `every ${c.schedule.hours} h`)) : esc(T("выключено", "off"))}</b>. ${esc(T(`Последнее успешное: ${L(c.schedule.lastSuccess)}.`, `Last success: ${L(c.schedule.lastSuccess)}.`))} ${c.schedule.next ? esc(T("Следующее:", "Next:")) + " " + esc(L(c.schedule.next)) : ""}</p>
            </div>
            <div class="card"><h2 style="margin:0 0 8px;font-size:1.15rem;font-weight:500">${esc(T("Сеть и Cloudflare", "Network and Cloudflare"))}</h2>
                <div class="row-4" style="margin-bottom:8px">
                    <div><div class="kpi-label">${esc(T("Запросов за сессию", "Requests this session"))}</div><div class="kpi-value" style="font-size:1.15rem">${num(0)}</div></div>
                    <div><div class="kpi-label">${esc(T("Неудачных", "Failed"))}</div><div class="kpi-value" style="font-size:1.15rem">${num(0)}</div></div>
                    <div><div class="kpi-label">${esc(T("Последний код", "Last status"))}</div><div class="kpi-value" style="font-size:1.15rem">—</div></div>
                    <div><div class="kpi-label">Cookies</div><div class="kpi-value" style="font-size:1.15rem">${num(c.cookies)}</div></div>
                </div>
                <p class="itch-muted" style="font-size:.9rem">${esc(T("Сбор открывает отдельное окно Chrome (или Edge). Проверка Cloudflare проходит в нём. Вставленный сюда cookie приложению не подходит — Cloudflare привязывает его к браузеру.", "The crawl opens a separate Chrome (or Edge) window. The Cloudflare check passes there. A cookie pasted here does not work for the app — Cloudflare ties it to the browser."))}</p>
                <div class="field" style="margin-bottom:8px"><span class="lab">${esc(T("cf_clearance или строка Cookie", "cf_clearance or a Cookie header"))}</span><input class="lock-type" /></div>
                <div class="field" style="margin-bottom:8px"><span class="lab">${esc(T("User-Agent браузера", "Browser User-Agent"))}</span><input class="lock-type" value="${esc(c.userAgent)}" /></div>
                <button class="btn fill demo-lock" type="button">${esc(T("Импорт и проверка", "Import and check"))}</button>
                <button class="btn demo-lock" type="button"><span class="ms">restart_alt</span>${esc(T("Сбросить сеанс", "Reset session"))}</button>
            </div>
        </div>
        <div class="row-4">${cards}</div>
        <div class="row-7-5">
            <div class="card"><div class="head-row"><h2 style="margin:0;font-size:1.15rem">${esc(T("Журнал", "Log"))}</h2><span class="grow"></span><label class="sw demo-lock"><i></i>${esc(T("Только предупреждения", "Warnings only"))}</label></div><div class="itch-log"><div class="itch-muted">${esc(T("Записей пока нет.", "No entries yet."))}</div></div></div>
            <div class="card"><h2 style="margin:0 0 8px;font-size:1.15rem">${esc(T("База данных", "Database"))}</h2>
                <table class="kv">
                    <tr><td>${esc(T("Игр в базе", "Games in the database"))}</td><td class="itch-num">${num(s.games)} <span class="itch-muted">(18+: ${num(s.nsfw)})</span></td></tr>
                    <tr><td>${esc(T("С оценками", "With ratings"))}</td><td class="itch-num">${num(s.rated)}</td></tr>
                    <tr><td>${esc(T("Загружены страницы", "Pages loaded"))}</td><td class="itch-num">${num(s.details)}</td></tr>
                    <tr><td>${esc(T("Всего на itch.io", "Total on itch.io"))}</td><td class="itch-num">${num(s.catalog)}</td></tr>
                    <tr><td>${esc(T("Счётчиков списков", "List counters"))}</td><td class="itch-num">${num(s.counters)}</td></tr>
                    <tr><td>${esc(T("Разработчиков / с профилем", "Developers / with a profile"))}</td><td class="itch-num">${num(s.developers)} / ${num(s.developersProfile)}</td></tr>
                    <tr><td>${esc(T("Тегов и фильтров", "Tags and filters"))}</td><td class="itch-num">${num(s.tags)}</td></tr>
                    <tr><td>${esc(T("Джемов / с участниками", "Jams / with entries"))}</td><td class="itch-num">${num(s.jams)} / ${num(s.jamsWithEntries)}</td></tr>
                    <tr><td>${esc(T("Дней истории", "Days of history"))}</td><td class="itch-num">${num(s.snapshotDays)} <span class="itch-muted">${esc(T("с", "since"))} ${esc(s.firstDay || "—")}</span></td></tr>
                    <tr><td>${esc(T("Списков: готово / дробится / в процессе", "Lists: done / splitting / in progress"))}</td><td class="itch-num">${num(s.listsComplete)} / ${num(s.listsTruncated)} / ${num(s.listsInProgress)}</td></tr>
                    <tr><td>${esc(T("Размер файла", "File size"))}</td><td class="itch-num">${num(s.megabytes, 1)} ${esc(T("МБ", "MB"))}</td></tr>
                </table>
            </div>
        </div>
        <div class="card"><h2 style="margin:0 0 8px;font-size:1.15rem">${esc(T("Покрытие каталога по годам", "Catalog coverage by year"))}</h2>
            <p class="itch-muted" style="font-size:.9rem">${esc(T("Список itch.io отдаёт максимум 200 страниц (7 200 игр). Глубокий сбор идёт по годам, а если оценённых игр за год больше — дробит год по цене и жанрам.", "An itch.io list returns at most 200 pages (7,200 games). The deep crawl goes year by year, and splits a year by price and genre when it has more rated games."))}</p>
            <div style="max-height:420px;overflow:auto"><table class="data"><thead><tr><th>${esc(T("Год", "Year"))}</th><th>${esc(T("Игр на itch.io", "Games on itch.io"))}</th><th>${esc(T("Известно", "Known"))}</th><th>${esc(T("С оценками", "With ratings"))}</th><th>${esc(T("Статус", "Status"))}</th></tr></thead><tbody>${years}</tbody></table></div>
            <div style="margin-top:10px"><button class="btn demo-lock" type="button" style="border:0;color:#F5B84A"><span class="ms">restart_alt</span>${esc(T("Начать глубокий сбор заново", "Start the deep crawl over"))}</button></div>
        </div>
        <div class="card" style="margin-top:16px">
            <div class="tabs">
                <button type="button" data-tab="data:history" class="${tab === "history" ? "on" : ""}">${esc(T("История запусков", "Run history"))}</button>
                <button type="button" data-tab="data:settings" class="${tab === "settings" ? "on" : ""}">${esc(T("Настройки", "Settings"))}</button>
            </div>
            ${tab === "history" ? stub() : settings}
        </div>`;
}

function pageStub(titleRu, titleEn) {
    return `<h1 class="page-title">${esc(T(titleRu, titleEn))}</h1><div class="card">${stub()}</div>`;
}

const NAV = [
    ["overview", "dashboard", () => T("Обзор", "Overview")],
    ["games", "sports_esports", () => T("Игры", "Games")],
    ["tops", "emoji_events", () => T("Топы", "Tops")],
    ["tags", "local_offer", () => T("Теги и жанры", "Tags and genres")],
    null,
    ["developer", "insights", () => T("Для разработчика", "For developers")],
    ["developers", "groups", () => T("Разработчики", "Developers")],
    ["jams", "celebration", () => T("Джемы", "Jams")],
    ["mygame", "track_changes", () => T("Моя игра", "My game")],
    null,
    ["data", "cloud_sync", () => T("Сбор данных", "Data collection")],
];

function shell(body) {
    const min = "≥ " + F().minRatings;
    const options = (D.options.minRatings || [1, 2, 4, 5, 10, 25, 50, 100]).map((v) => "≥ " + v);
    const nav = NAV.map((item) => item
        ? `<a class="nav-link${state.route === item[0] || (state.route === "game" && item[0] === "games") ? " active" : ""}" href="#/${item[0]}"><span class="ms">${item[1]}</span>${esc(item[2]())}</a>`
        : `<div class="nav-gap"></div>`).join("");
    return `<header class="appbar">
        <button class="icon-btn demo-lock" type="button" aria-label="menu"><span class="ms">menu</span></button>
        <div class="itch-logo" style="margin-left:8px;font-size:1.25rem">Itch<span>DB</span></div>
        <div class="itch-muted" style="margin-left:12px;font-size:.8rem">${esc(T("локальная аналитика itch.io", "local itch.io analytics"))}</div>
        <span class="grow"></span>
        <button class="btn fill small demo-lock" type="button"><span class="ms fill" style="font-size:16px">refresh</span>${esc(T("Обновить", "Refresh"))}</button>
        <span class="sw demo-lock${F().showNsfw ? " on" : ""}"><i></i>18+</span>
        <span class="sw demo-lock${F().includeUnrated ? " on" : ""}"><i></i>${esc(T("0 оценок", "0 ratings"))}</span>
        <div class="bar-field">${field(T("Мин. оценок", "Min ratings"), min, "min", lockItems(options, min))}</div>
        <div class="lang"><button type="button" data-lang="en" class="${state.lang === "en" ? "on" : ""}">EN</button><button type="button" data-lang="ru" class="${state.lang === "ru" ? "on" : ""}">RU</button></div>
    </header>
    <div class="frame"><aside class="drawer">${nav}</aside><main class="main">${body}</main></div>`;
}

function page() {
    const r = state.route;
    if (r === "games") return pageGames();
    if (r === "game") return pageStub("Карточка игры", "Game");
    if (r === "tops") return pageTops();
    if (r === "tags") return pageTags();
    if (r === "tag") return pageStub("Сегмент", "Segment");
    if (r === "developer") return pageDeveloper();
    if (r === "developers") return pageDevelopers();
    if (r === "dev") return pageStub("Разработчик", "Developer");
    if (r === "jams") return pageJams();
    if (r === "jam") return pageStub("Джем", "Jam");
    if (r === "mygame") return pageMyGame();
    if (r === "data") return pageData();
    return pageOverview();
}

function killCharts() {
    while (charts.length) {
        try { charts.pop().destroy(); } catch { /* already gone */ }
    }
}

function mountCharts() {
    if (!window.ApexCharts) return;
    const base = {
        chart: { background: "transparent", fontFamily: "Segoe UI, Inter, Roboto, sans-serif", toolbar: { show: false }, zoom: { enabled: false }, animations: { enabled: false } },
        theme: { mode: "dark" },
        grid: { borderColor: "rgba(255,255,255,0.08)" },
        legend: { position: "top", horizontalAlign: "left", labels: { colors: "rgba(255,255,255,0.7)" } },
        tooltip: { theme: "dark" },
        dataLabels: { enabled: false },
    };
    const el = (id) => document.getElementById(id);
    const add = (id, options) => {
        const node = el(id);
        if (!node) return;
        const chart = new ApexCharts(node, options);
        charts.push(chart);
        chart.render();
    };
    if (state.route === "overview" || state.route === "") {
        const o = D.overview;
        const dist = o.distribution.map((d) => d.value);
        add("chart-dist", {
            ...base,
            chart: { ...base.chart, type: "bar", height: 260 },
            colors: ["#FA5C5C"],
            series: [{ name: T("Игр", "Games"), data: dist }],
            tooltip: {
                ...base.tooltip,
                y: { formatter: (_v, opts) => Fmt.n(dist[opts.dataPointIndex], 0, state.lang) },
            },
            xaxis: { categories: o.distribution.map((d) => d.label), labels: { style: { colors: "rgba(255,255,255,0.6)" } } },
            yaxis: {
                logarithmic: true,
                logBase: 10,
                min: 10,
                forceNiceScale: true,
                labels: {
                    style: { colors: "rgba(255,255,255,0.6)" },
                    formatter: (v) => {
                        const n = Number(v);
                        if (!Number.isFinite(n) || n < 1) return "";
                        const exp = Math.pow(10, Math.round(Math.log10(n)));
                        return exp >= 1000 ? String(exp) : String(Math.round(n));
                    },
                },
            },
            plotOptions: { bar: { borderRadius: 3, columnWidth: "70%" } },
        });
        const yearSeries = [];
        if (o.yearHasItch) yearSeries.push({ name: T("Игр на itch.io", "Games on itch.io"), type: "column", data: o.years.map((y) => y.itch) });
        yearSeries.push({ name: T("В базе", "In the database"), type: "column", data: o.years.map((y) => y.known) });
        yearSeries.push({ name: T("Среднее оценок", "Mean ratings"), type: "line", data: o.years.map((y) => y.mean) });
        add("chart-years", {
            ...base,
            chart: { ...base.chart, type: "line", height: 280 },
            colors: o.yearHasItch ? ["#3A3F58", "#2F6F8F", "#FA5C5C"] : ["#2F6F8F", "#FA5C5C"],
            series: yearSeries,
            stroke: { width: yearSeries.map((s) => (s.type === "line" ? 3 : 0)), curve: "straight" },
            xaxis: { categories: o.years.map((y) => String(y.year)), labels: { style: { colors: "rgba(255,255,255,0.6)" } } },
            yaxis: [
                {
                    seriesName: yearSeries[0].name,
                    decimalsInFloat: 0,
                    forceNiceScale: true,
                    title: { text: T("Игр", "Games"), style: { color: "rgba(255,255,255,0.6)" } },
                    labels: { style: { colors: "rgba(255,255,255,0.6)" } },
                },
                ...(o.yearHasItch ? [{
                    seriesName: yearSeries[0].name,
                    show: false,
                    decimalsInFloat: 0,
                    forceNiceScale: true,
                }] : []),
                {
                    seriesName: T("Среднее оценок", "Mean ratings"),
                    opposite: true,
                    decimalsInFloat: 1,
                    forceNiceScale: true,
                    title: { text: T("Среднее оценок", "Mean ratings"), style: { color: "rgba(255,255,255,0.6)" } },
                    labels: { style: { colors: "rgba(255,255,255,0.6)" }, formatter: (v) => Fmt.n(Number(v), 1, state.lang) },
                },
            ],
            plotOptions: { bar: { borderRadius: 3, columnWidth: "60%" } },
        });
        add("chart-genres", {
            ...base,
            chart: { ...base.chart, type: "bar", height: 440 },
            colors: ["#5CC8FA"],
            plotOptions: { bar: { horizontal: true, borderRadius: 3, barHeight: "70%" } },
            series: [{ name: T("Сглаженная медиана", "Smoothed median"), data: o.genres.map((g) => Number(g.value.toFixed ? g.value.toFixed(1) : g.value)) }],
            xaxis: { categories: o.genres.map((g) => L(g.name)), labels: { style: { colors: "rgba(255,255,255,0.6)" } } },
            yaxis: { labels: { trim: false, maxWidth: 240, style: { colors: "rgba(255,255,255,0.75)", fontSize: "12px" } } },
        });
    }
    if (state.route === "tags" && (state.tabs.tags || "genres") === "genres") {
        const g = D.genres;
        add("chart-gdemand", {
            ...base,
            chart: { ...base.chart, type: "bar", height: 420 },
            colors: ["#5CC8FA"],
            plotOptions: { bar: { horizontal: true, borderRadius: 3, barHeight: "70%" } },
            series: [{ name: T("Сглаженная медиана", "Smoothed median"), data: g.demand.map((x) => x.value) }],
            xaxis: { categories: g.demand.map((x) => L(x.name)), labels: { style: { colors: "rgba(255,255,255,0.6)" } } },
            yaxis: { labels: { trim: false, maxWidth: 240, style: { colors: "rgba(255,255,255,0.75)", fontSize: "12px" } } },
        });
        add("chart-gsupply", {
            ...base,
            chart: { ...base.chart, type: "bar", height: 420 },
            colors: ["#3A3F58"],
            plotOptions: { bar: { horizontal: true, borderRadius: 3, barHeight: "70%" } },
            series: [{ name: T("Игр", "Games"), data: g.supply.map((x) => x.value) }],
            xaxis: { categories: g.supply.map((x) => L(x.name)), labels: { style: { colors: "rgba(255,255,255,0.6)" } } },
            yaxis: { labels: { trim: false, maxWidth: 240, style: { colors: "rgba(255,255,255,0.75)", fontSize: "12px" } } },
        });
    }
}

function render() {
    hideTip();
    const pop = document.getElementById("pop");
    if (pop) pop.hidden = true;
    killCharts();
    document.documentElement.lang = state.lang;
    document.title = "ItchDB";
    document.getElementById("app").innerHTML = shell(page());
    window.scrollTo(0, 0);
    mountCharts();
}

function showTip(el, text) {
    const tip = document.getElementById("tip");
    const r = el.getBoundingClientRect();
    tip.hidden = false;
    tip.textContent = text;
    const x = Math.min(r.left, window.innerWidth - 300);
    tip.style.left = Math.max(8, x) + "px";
    tip.style.top = (r.bottom + 6) + "px";
}
function hideTip() { document.getElementById("tip").hidden = true; }

function showPop(el, id) {
    const f = formulas()[id];
    if (!f) return;
    const pop = document.getElementById("pop");
    pop.hidden = false;
    pop.innerHTML = `<b>${esc(f.title)}</b>${f.formula ? `<div class="formula">${esc(f.formula)}</div>` : ""}<div>${esc(f.text)}</div>`;
    const r = el.getBoundingClientRect();
    pop.style.left = Math.max(8, r.left) + "px";
    pop.style.top = (r.bottom + 6) + "px";
}

function readHash() {
    const raw = (location.hash || "#/overview").replace(/^#\/?/, "");
    const [path, query] = raw.split("?");
    state.route = path || "overview";
    const params = new URLSearchParams(query || "");
    if (params.get("tab")) {
        const key = state.route === "developer" || state.route === "tags" || state.route === "tops" || state.route === "data" ? state.route : null;
        if (key) state.tabs[key] = params.get("tab");
    }
}

document.getElementById("app").addEventListener("click", (ev) => {
    const lang = ev.target.closest("[data-lang]");
    if (lang) {
        state.lang = lang.dataset.lang;
        render();
        return;
    }
    const tab = ev.target.closest("[data-tab]");
    if (tab) {
        const [key, id] = tab.dataset.tab.split(":");
        state.tabs[key] = id;
        render();
        return;
    }
    const go = ev.target.closest("[data-go]");
    if (go && !ev.target.closest(".itch-map-point")) {
        location.hash = "#/" + go.dataset.go;
        return;
    }
    const point = ev.target.closest(".itch-map-point");
    if (point) { location.hash = "#/tag"; return; }
    const infoBtn = ev.target.closest("[data-info]");
    if (infoBtn) { ev.stopPropagation(); showPop(infoBtn, infoBtn.dataset.info); return; }
    const item = ev.target.closest(".sel-item");
    if (item) {
        ev.preventDefault();
        ev.stopPropagation();
        item.closest(".menu").hidden = true;
        hideTip();
        return;
    }
    const toggle = ev.target.closest(".sel-toggle");
    if (toggle) {
        const menu = toggle.parentElement.querySelector(".menu");
        const open = menu.hidden;
        document.querySelectorAll(".menu").forEach((m) => { m.hidden = true; });
        menu.hidden = !open;
        ev.stopPropagation();
        return;
    }
    if (!ev.target.closest(".pop")) document.getElementById("pop").hidden = true;
    if (!ev.target.closest(".sel")) document.querySelectorAll(".menu").forEach((m) => { m.hidden = true; });
});

document.getElementById("app").addEventListener("mouseover", (ev) => {
    const item = ev.target.closest(".sel-item");
    if (item && item.closest(".menu") && !item.closest(".menu").hidden) {
        showTip(item, demo());
        return;
    }
    const lock = ev.target.closest(".demo-lock");
    if (lock) showTip(lock, demo());
});
document.getElementById("app").addEventListener("mouseout", (ev) => {
    const item = ev.target.closest(".sel-item");
    const lock = ev.target.closest(".demo-lock");
    const host = item || lock;
    if (!host) return;
    const next = ev.relatedTarget;
    if (next && host.contains(next)) return;
    if (item && next && next.closest && next.closest(".sel-item")) return;
    hideTip();
});
document.documentElement.addEventListener("mouseleave", () => hideTip());
document.getElementById("app").addEventListener("beforeinput", (ev) => {
    if (ev.target.classList.contains("lock-type")) {
        ev.preventDefault();
        showTip(ev.target, demo());
    }
});
document.getElementById("app").addEventListener("keydown", (ev) => {
    if (ev.target.classList.contains("lock-type") && ev.key.length === 1) {
        ev.preventDefault();
        showTip(ev.target, demo());
    }
});

window.addEventListener("hashchange", () => { readHash(); render(); });
readHash();
if (!D) {
    document.getElementById("app").innerHTML = "<p style='padding:24px'>demo.js is missing. Run the preview export.</p>";
} else render();
