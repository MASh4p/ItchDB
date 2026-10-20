# ItchDB — превью

Демонстрация интерфейса ItchDB: те же экраны и те же стили, что у полной программы, и срез уже собранной базы. Цифры на открытых страницах настоящие. Обложки ведут на itch.io.

Превью только показывает. Поиск, фильтры и экспорт открываются, но список не меняют. Кнопки шапки, кроме смены языка, и кнопки запуска сбора при наведении отвечают: «В демонстрационной версии это недоступно». Карточка игры, разработчика, джема или тега — заглушка. Подсказки формул у значка ⓘ работают.

| Экран | Что открыто |
|---|---|
| Обзор | целиком |
| Игры | первая страница таблицы |
| Топы | первая страница «По спросу»; остальные вкладки переключаются и пустые |
| Теги и жанры | жанры, с графиками и таблицей; у остальных вкладок видны только числа сегментов |
| Для разработчика | выводы; на карте возможностей — небольшая выборка ниш по всем четырём квадрантам |
| Разработчики и джемы | карточки сверху и первая страница списка |
| Моя игра | пустая форма, без добавленной игры |
| Сбор данных | текущий сбор, сеть, профили запуска, журнал, база, покрытие по годам и настройки. История запусков закрыта |

Язык интерфейса переключается между русским и английским.

## Полная версия

ItchDB — локальная аналитика [itch.io](https://itch.io). Программа читает публичные страницы каталога и считает по ним спрос, конкуренцию, шансы и тренды. База, cookies и ключ API остаются на компьютере. К itch.io уходят только те запросы, которые запускает сам пользователь. С площадкой программа не связана.

В шапке три общих фильтра, и от них зависят все цифры: «18+», игры с нулём оценок и минимальное число оценок для медиан. Рядом кнопка «Обновить»: она снимает свежий ежедневный срез, а если данных ещё нет — запускает быстрый старт.

### Обзор

Сводка по каталогу: сколько игр на itch.io и сколько из них уже в базе, доля без единой оценки, медиана или среднее, порог лучших 10% (P90), шанс набрать порог успеха и типичная оценка в звёздах.

Ниже — короткие выводы, распределение числа оценок (столбец «0» оценён по счётчику каталога, остальные — по собранным играм) и когорты по году выхода: сколько игр вышло, сколько из них в базе и какое у них среднее число оценок. Ещё ниже — спрос по жанрам, лучшие ниши и либо «горячее», либо самые оценённые игры, если второго снимка ещё нет.

### Игры

Все собранные игры. Поиск по названию и автору, отбор по жанру, тегу, платформе, цене и году, сортировка по числу оценок, взвешенному рейтингу, горячему, приросту, новизне и комментариям. Таблицу можно выгрузить в CSV или Excel.

Карточка игры — история оценок, места в списках «Популярное» с соседями по тегу, похожие игры и оценка аудитории. Просмотры и скачивания чужих игр itch.io не показывает, поэтому аудитория считается из числа оценок.

### Топы

Шесть списков:

- **По спросу** — сколько людей поставили оценку. Число оценок здесь и есть размер аудитории.
- **По отзывам** — качество с поправкой на малое число голосов (взвешенный рейтинг).
- **Горячее** — кто набирает оценки прямо сейчас, по приросту между ежедневными снимками.
- **Новые и растущие** — недавние игры, которые уже растут.
- **Featured** — игры с отметкой площадки.
- **Обсуждаемые** — по комментариям.

Списки режутся по давности выхода и по жанру.

### Теги и жанры

Жанры, теги, движки, форматы, языки и каталог фильтров itch.io. У каждого сегмента — сколько игр в списке на itch.io, сколько из них с оценками, медиана и сглаженная медиана, квантили, шанс набрать порог успеха, тренд по когортам, порог входа на первую страницу popular и сводный Opportunity.

Медианы считаются по играм не ниже порога из шапки. У большинства игр itch.io оценок нет, и без этого порога медиана была бы нулём везде. Если включены «0 оценок», конкуренция и шанс делятся на полный счётчик списка, вместе с играми без оценок.

Карточка сегмента повторяет эти цифры, показывает распределение оценок внутри него и предлагает проверить идею с этим тегом.

### Для разработчика

Семь вкладок.

- **Выводы** — что данные говорят о возрасте игры, оформлении страницы, тегах, цене, платформе и сочетаниях.
- **Карта возможностей** — теги, жанры или форматы на плоскости «спрос против конкуренции». Пунктир — медианы. Размер точки — шанс набрать порог оценок.
- **Ниши** — рейтинг сегментов по Opportunity.
- **Форматы** — браузер, Windows и остальные способы запуска: где медиана выше и где шанс успеха больше.
- **Сочетания тегов** — пары через lift: медиана пары, делённая на лучшую из медиан тегов по отдельности.
- **Факторы успеха** — что сопутствует большему числу оценок при прочих равных, в том числе при постоянном возрасте игры. Это корреляции, не причина.
- **Проверка идеи** — теги, жанр, платформа и цена. Программа сравнивает запрос с похожими играми из базы и может спросить itch.io, сколько игр с такой комбинацией уже есть.

Квадранты карты:

| | Игр меньше медианы | Игр больше медианы |
|---|---|---|
| Спрос выше медианы | Голубой океан | Красный океан |
| Спрос ниже медианы | Тихая ниша | Ловушка |

Opportunity сводит спрос, шанс успеха, низкую конкуренцию и тренд в шкалу 0–100. Веса задаются в настройках сбора.

### Разработчики

Авторы игр из базы: сколько их, какая доля с одной игрой, какая доля всех оценок приходится на верхний 1%, медиана подписчиков. Отдельно — как число игр у автора связано с медианой и шансом успеха его игр, кто быстрее всех набирает подписчиков, и таблица с суммой оценок, медианой и самой оценённой игрой.

### Джемы

Сколько джемов в базе, сколько идут сейчас и скоро начнутся, медиана сданных работ, какая доля записавшихся доходит до сдачи, сколько игр с джемов потом живёт на itch.io и какой у них шанс набрать порог по сравнению с остальными играми. Список фильтруется на идущие, ближайшие и прошедшие.

### Моя игра

Слежение за позициями своих и чужих игр в «Популярном» по тегам. С ключом API itch.io программа один раз запрашивает реальные просмотры и скачивания ваших игр и по ним уточняет, сколько просмотров приходится на одну оценку. Это число затем используется в оценке аудитории всех остальных игр. Ключ хранится на компьютере в зашифрованном виде.

### Сбор данных

У каталога itch.io нет открытого API, поэтому программа читает публичные страницы как обычный браузер, несколькими вкладками, с паузой после каждой. Сбор идёт только пока программа открыта. Прогресс сохраняется и продолжается со следующего запуска.

| Профиль | Зачем |
|---|---|
| Быстрый старт | Первые выводы примерно за полчаса: каталог тегов, топы, два последних года, жанры и крупные теги. Выборка смещена к популярным играм, и это помечено. |
| Ежедневное обновление | Раз в сутки или по кнопке «Обновить»: счётчики списков, позиции в popular, отслеживаемые игры, свежие оценки топов, новые релизы, страницы игр, профили и джемы. Из этих снимков появляются тренды и «горячее». |
| Глубокий сбор | Счётчики списков, затем страницы популярных игр и сами списки: годы, жанры, теги, движки и в конце 18+. Список itch.io отдаёт не больше 200 страниц, поэтому длинный год дробится по цене и жанру. |
| Карточки игр | Только страницы игр: полные теги, дата выхода, движок, скриншоты и статус. Сначала игры с наибольшим числом оценок. |

На той же странице — журнал, размер и состав базы, покрытие каталога по годам и настройки: паузы сети, расписание, порог успеха, сглаживание медианы, веса Opportunity, формула «горячего» и объём каждого профиля.

Если вместо страницы приходит проверка Cloudflare, сбор делает паузу и пробует снова. Когда паузы кончаются, на эту страницу можно импортировать cookie `cf_clearance`. Он привязан к браузеру, из которого взят, и к User-Agent в настройках.

Пока глубокий сбор не закончен, медианы и доли успеха завышены: в базу раньше попадают заметные игры. Страницы пишут об этом прямо. Списки, которые itch.io не умеет отфильтровать, пропускаются, а не записываются как пустые.

---

# ItchDB — preview

A demonstration of the ItchDB interface: the same screens and the same styles as the full program, and a slice of an already collected database. Figures on the open pages are real. Covers link to itch.io.

The preview only shows. Search, filters, and export open, but they do not change the list. Bar buttons other than the language switch, and the crawl start buttons, say on hover: “Not available in the demo.” A game, developer, jam, or tag page is a stub. Formula help on the ⓘ icon works.

| Screen | What is open |
|---|---|
| Overview | the whole page |
| Games | the first page of the table |
| Tops | the first page of “By demand”; the other tabs switch and are empty |
| Tags and genres | genres, with charts and a table; the other tabs only show segment counts |
| For developers | conclusions; the opportunity map is a small sample of niches across all four quadrants |
| Developers and jams | the cards above and the first page of the list |
| My game | an empty form, with no game added |
| Data collection | the current crawl, network, start profiles, log, database, coverage by year, and settings. Run history is closed |

The interface language switches between Russian and English.

## Full version

ItchDB is local analytics for [itch.io](https://itch.io). The program reads public catalog pages and computes demand, competition, odds, and trends from them. The database, cookies, and API key stay on the computer. The only requests sent to itch.io are the ones the user starts. The program is not affiliated with the site.

Three shared filters in the bar affect every figure: “18+”, games with zero ratings, and the minimum rating count used for medians. Next to them, Refresh takes a fresh daily snapshot, or starts a quick start when there is no data yet.

### Overview

A catalog summary: how many games are on itch.io and how many of them are already in the database, the share with no ratings at all, the median or the mean, the top 10% threshold (P90), the chance of clearing the success threshold, and the typical star rating.

Below that are short conclusions, the distribution of rating counts (the “0” bar is estimated from the catalog counter, the rest from collected games), and release-year cohorts: how many games came out, how many of them are in the database, and their mean rating count. Further down are demand by genre, the best niches, and either Hot or the most rated games when a second snapshot does not exist yet.

### Games

Every collected game. Search by title and author, filter by genre, tag, platform, price, and year, and sort by rating count, weighted rating, Hot, gain, recency, and comments. The table can be exported to CSV or Excel.

A game page holds the rating history, places in Popular lists with neighbors on the same tag, similar games, and an audience estimate. itch.io does not show views and downloads of other people’s games, so the audience is estimated from the rating count.

### Tops

Six lists:

- **By demand** — how many people left a rating. The rating count is the audience size.
- **By reviews** — quality adjusted for a small number of votes (weighted rating).
- **Hot** — who is collecting ratings right now, from the gain between daily snapshots.
- **New and rising** — recent games that are already growing.
- **Featured** — games marked by the site.
- **Discussed** — by comments.

The lists can be cut by release age and by genre.

### Tags and genres

Genres, tags, engines, formats, languages, and the itch.io filter catalog. Each segment shows how many games are on its itch.io list, how many of them have ratings, the median and the smoothed median, quantiles, the chance of clearing the success threshold, the trend across cohorts, the threshold for the first page of Popular, and a combined Opportunity score.

Medians use games at or above the bar’s threshold. Most itch.io games have no ratings, and without that threshold the median would be zero everywhere. With “0 ratings” on, competition and chance are divided by the full list counter, including unrated games.

A segment page repeats these figures, shows the rating distribution inside the segment, and offers to check an idea with that tag.

### For developers

Seven tabs.

- **Conclusions** — what the data says about a game’s age, page presentation, tags, price, platform, and combinations.
- **Opportunity map** — tags, genres, or formats on a “demand versus competition” plane. Dashed lines are the medians. Point size is the chance of clearing the ratings threshold.
- **Niches** — segments ranked by Opportunity.
- **Formats** — browser, Windows, and the other ways to run a game: where the median is higher and where the chance of success is greater.
- **Tag combinations** — pairs through lift: the pair’s median divided by the better of the two tags’ medians alone.
- **Success factors** — what goes with a higher rating count, all else equal, including with the game’s age held constant. These are correlations, not causes.
- **Idea check** — tags, genre, platform, and price. The program compares the query with similar games in the database and can ask itch.io how many games already use that combination.

Map quadrants:

| | Fewer games than the median | More games than the median |
|---|---|---|
| Demand above the median | Blue ocean | Red ocean |
| Demand below the median | Quiet niche | Trap |

Opportunity folds demand, chance of success, low competition, and trend into a 0–100 scale. The weights are set in the collection settings.

### Developers

Authors in the database: how many there are, what share has a single game, what share of all ratings belongs to the top 1%, and the median follower count. Separately, how an author’s number of games relates to the median and the chance of success of those games, who is gaining followers fastest, and a table with the rating sum, the median, and the highest-rated game.

### Jams

How many jams are in the database, how many are live and starting soon, the median number of submissions, what share of people who joined finish a submission, how many jam games later live on itch.io, and how their chance of clearing the threshold compares with other games. The list filters into live, upcoming, and ended.

### My game

Watch positions of your games and others in Popular lists by tag. With an itch.io API key the program makes one request for the real views and downloads of your games and uses them to refine how many views correspond to one rating. That number is then used in the audience estimate for every other game. The key stays on the computer, encrypted.

### Data collection

The itch.io catalog has no public API, so the program reads public pages like a normal browser, with several tabs and a pause after each one. The crawl runs only while the program is open. Progress is saved and continues on the next launch.

| Profile | Purpose |
|---|---|
| Quick start | First conclusions in about half an hour: the tag catalog, tops, the last two years, genres, and the largest tags. The sample is skewed toward popular games, and that is marked. |
| Daily update | Once a day, or from Refresh: listing counters, Popular positions, tracked games, fresh ratings on the tops, new releases, game pages, profiles, and jams. These snapshots produce trends and Hot. |
| Deep crawl | Listing counters, then pages of popular games and the lists themselves: years, genres, tags, engines, and 18+ last. An itch.io list returns at most 200 pages, so a long year is split by price and genre. |
| Game pages | Game pages only: full tags, release date, engine, screenshots, and status. Games with the most ratings go first. |

The same page holds the log, the size and contents of the database, catalog coverage by year, and settings: network pauses, the schedule, the success threshold, median smoothing, Opportunity weights, the Hot formula, and the volume of each profile.

If a Cloudflare check arrives instead of a page, the crawl pauses and tries again. When the pauses run out, a `cf_clearance` cookie can be imported on this page. It is tied to the browser it was taken from and to the User-Agent in settings.

Until the deep crawl is finished, medians and success shares are high: noticeable games enter the database first. The pages say so directly. Lists that itch.io cannot filter are skipped rather than recorded as empty.
