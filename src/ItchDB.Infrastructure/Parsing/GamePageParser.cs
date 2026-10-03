using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Infrastructure.Parsing;

public static partial class GamePageParser
{
    public static GamePageData Parse(string html)
    {
        var doc = ParseHelpers.ParseHtml(html);
        var data = new GamePageData();

        data.Id = ParseId(doc, html);
        data.Title = ParseHelpers.Clean(doc.QuerySelector("h1.game_title")?.TextContent)
                     is { Length: > 0 } t ? t : ParseHelpers.Clean(doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"));
        data.CoverUrl = doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content");
        data.ShortText = doc.QuerySelector("meta[name='description']")?.GetAttribute("content");
        data.UploaderUserId = ParseHelpers.ParseLong(doc.QuerySelector(".follow_user_btn[data-user_id]")?.GetAttribute("data-user_id"));

        ParseInfoTable(doc, data);
        ParseJsonLd(doc, data);
        ParsePrice(doc, html, data);

        data.CommentsCount = ParseComments(doc);
        data.ScreenshotsCount = doc.QuerySelectorAll(".screenshot_list a").Length;
        data.IsFeatured = doc.QuerySelector(".breadcrumbs a[href$='/featured'], .breadcrumbs a[href*='/games/featured']") is not null
                          || html.Contains("\"url\":\"https:\\/\\/itch.io\\/games\\/featured\"", StringComparison.Ordinal);

        foreach (var a in doc.QuerySelectorAll("li.jam_entry a[href]"))
        {
            var m = JamSlugRegex().Match(a.GetAttribute("href") ?? "");
            if (m.Success) data.JamSlugs.Add(m.Groups[1].Value);
        }

        if (data.Tags.Any(t => ItchCatalog.NsfwTags.Contains(t.Slug))) data.IsNsfw = true;
        return data;
    }

    private static long? ParseId(IHtmlDocument doc, string html)
    {
        var path = doc.QuerySelector("meta[name='itch:path']")?.GetAttribute("content");
        if (path is not null)
        {
            var m = PathIdRegex().Match(path);
            if (m.Success) return long.Parse(m.Groups[1].Value);
        }
        var g = InitGameIdRegex().Match(html);
        if (g.Success) return long.Parse(g.Groups[1].Value);
        return ParseHelpers.ParseLong(doc.QuerySelector("input[name='game_id']")?.GetAttribute("value"));
    }

    private static void ParseInfoTable(IHtmlDocument doc, GamePageData data)
    {
        foreach (var row in doc.QuerySelectorAll(".game_info_panel_widget table tr"))
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 2) continue;
            var label = ParseHelpers.Clean(cells[0].TextContent).ToLowerInvariant();
            var value = cells[1];
            switch (label)
            {
                case "published":
                    data.PublishedAt = ParseAbbr(value);
                    break;
                case "updated":
                    data.UpdatedAt = ParseAbbr(value);
                    break;
                case "release date":
                    data.ReleaseDate = ParseAbbr(value);
                    break;
                case "status":
                    data.Status = ItchCatalog.ParseStatus(value.TextContent);
                    break;
                case "platforms":
                    data.Platforms = ParsePlatforms(value);
                    break;
                case "rating":
                    ParseRatingCell(value, data);
                    break;
                case "author":
                case "authors":
                    foreach (var a in value.QuerySelectorAll("a[href]"))
                        data.Authors.Add((ParseHelpers.Clean(a.TextContent), a.GetAttribute("href")!));
                    break;
                case "genre":
                    data.Genres.AddRange(Links(value));
                    break;
                case "made with":
                    data.Engines.AddRange(Links(value).Where(l => l.Slug.StartsWith("made-with-", StringComparison.Ordinal)));
                    break;
                case "tags":
                    data.Tags.AddRange(Links(value).Where(l => l.Slug.StartsWith("tag-", StringComparison.Ordinal)));
                    break;
                case "average session":
                    data.Durations.AddRange(Links(value).Where(l => l.Slug.StartsWith("duration-", StringComparison.Ordinal)));
                    break;
                case "languages":
                    data.Languages.AddRange(Links(value).Where(l => l.Slug.StartsWith("lang-", StringComparison.Ordinal)));
                    break;
                case "inputs":
                    data.Inputs.AddRange(Links(value).Where(l => l.Slug.StartsWith("input-", StringComparison.Ordinal)));
                    break;
                case "accessibility":
                    data.Accessibility.AddRange(Links(value).Where(l => l.Slug.StartsWith("accessibility-", StringComparison.Ordinal)));
                    break;
                case "multiplayer":
                    data.Multiplayer.AddRange(Links(value).Where(l => l.Slug.StartsWith("multiplayer-", StringComparison.Ordinal)));
                    break;
                case "mentions":
                    data.MentionsCount = value.QuerySelectorAll("a").Length;
                    break;
                case "content":
                    var text = value.TextContent;
                    if (text.Contains("No generative AI", StringComparison.OrdinalIgnoreCase)) data.Ai = AiUsage.NoAi;
                    if (text.Contains("adult", StringComparison.OrdinalIgnoreCase)
                        || value.QuerySelector("a[href*='/games/nsfw']") is not null) data.IsNsfw = true;
                    break;
                case "ai disclosure":
                    data.Ai = AiUsage.UsesAi;
                    break;
            }
        }
    }

    private static IEnumerable<LinkRef> Links(IElement cell)
    {
        foreach (var a in cell.QuerySelectorAll("a[href]"))
        {
            var slug = ParseHelpers.FilterSlugFromHref(a.GetAttribute("href"));
            if (slug is not null) yield return new LinkRef(slug, ParseHelpers.Clean(a.TextContent));
        }
    }

    private static DateTime? ParseAbbr(IElement cell) =>
        ParseHelpers.ParseItchDate(cell.QuerySelector("abbr[title]")?.GetAttribute("title"));

    private static Platforms ParsePlatforms(IElement cell)
    {
        var p = Platforms.None;
        foreach (var l in Links(cell))
        {
            p |= l.Slug switch
            {
                "platform-windows" => Platforms.Windows,
                "platform-osx" => Platforms.MacOS,
                "platform-linux" => Platforms.Linux,
                "platform-android" => Platforms.Android,
                "platform-ios" => Platforms.IOS,
                "html5" or "platform-web" => Platforms.Web,
                _ => Platforms.None,
            };
        }
        return p;
    }

    private static void ParseRatingCell(IElement cell, GamePageData data)
    {
        var agg = cell.QuerySelector(".aggregate_rating");
        var tip = agg?.GetAttribute("data-tooltip");
        if (tip is not null)
        {
            var m = ListingParser.RatingTooltipRegex().Match(tip);
            if (m.Success)
            {
                data.RatingAvg = ParseHelpers.ParseDouble(m.Groups[1].Value);
                data.RatingCount = ParseHelpers.ParseCount(m.Groups[2].Value);
            }
        }
        data.RatingCount ??= ParseHelpers.ParseCount(cell.QuerySelector(".rating_count")?.GetAttribute("content"))
                              ?? ParseHelpers.ParseCount(cell.QuerySelector(".rating_count")?.TextContent);
        data.RatingAvg ??= ParseHelpers.ParseDouble(cell.QuerySelector("[itemprop='ratingValue']")?.GetAttribute("content"));
    }

    private static void ParseJsonLd(IHtmlDocument doc, GamePageData data)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var type = root.TryGetProperty("@type", out var t) ? t.GetString() : null;
                if (type == "Product")
                {
                    if (root.TryGetProperty("aggregateRating", out var ar))
                    {
                        if (data.RatingCount is null && ar.TryGetProperty("ratingCount", out var rc) && rc.TryGetInt32(out var count))
                            data.RatingCount = count;
                        if (data.RatingAvg is null && ar.TryGetProperty("ratingValue", out var rv))
                            data.RatingAvg = ParseHelpers.ParseDouble(rv.ToString());
                    }
                    if (root.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Object
                        && offers.TryGetProperty("priceCurrency", out var cur))
                        data.Currency ??= cur.GetString();
                }
                else if (type == "BreadcrumbList" && script.TextContent.Contains("games\\/featured", StringComparison.Ordinal))
                {
                    data.IsFeatured = true;
                }
            }
            catch (JsonException)
            {
                // Some creators break JSON-LD with custom HTML; the info table is the primary source anyway.
            }
        }
    }

    private static string InitBlock(string html)
    {
        var i = html.IndexOf("init_ViewGame(", StringComparison.Ordinal);
        if (i < 0) i = html.IndexOf("init_ViewHtmlGame(", StringComparison.Ordinal);
        return i < 0 ? "" : html.Substring(i, Math.Min(6000, html.Length - i));
    }

    private static void ParsePrice(IHtmlDocument doc, string html, GamePageData data)
    {
        var init = InitBlock(html);
        var min = MinPriceRegex().Match(init);
        var actual = ActualPriceRegex().Match(init);
        int? minCents = min.Success ? int.Parse(min.Groups[1].Value) : null;
        data.ActualPriceCents = actual.Success ? int.Parse(actual.Groups[1].Value) : null;

        var buy = doc.QuerySelector(".buy_row .buy_message");
        var dollars = buy?.QuerySelector(".dollars")?.TextContent;
        if (!string.IsNullOrEmpty(dollars))
        {
            var (cents, currency) = ParseHelpers.ParseMoney(dollars);
            minCents ??= cents;
            data.Currency ??= currency;
        }

        data.PriceCents = minCents;
        var nyp = buy?.TextContent.Contains("Name your own price", StringComparison.OrdinalIgnoreCase) ?? false;
        data.PriceModel = minCents switch
        {
            > 0 => PriceModel.Paid,
            _ when nyp => PriceModel.NameYourPrice,
            0 => PriceModel.Free,
            _ => PriceModel.Unknown,
        };
    }

    private static int? ParseComments(IHtmlDocument doc)
    {
        var widget = doc.QuerySelector(".game_comments_widget");
        if (widget is null) return null;
        var label = widget.QuerySelector(".topic_pager .page_label")?.TextContent;
        if (label is not null)
        {
            var m = CommentsTotalRegex().Match(ParseHelpers.Clean(label));
            if (m.Success) return ParseHelpers.ParseCount(m.Groups[1].Value);
        }
        return widget.QuerySelectorAll(".community_post").Length;
    }

    [GeneratedRegex(@"games/(\d+)")]
    private static partial Regex PathIdRegex();

    /// <summary>
    /// The game object is not the first JSON value, and hit_url can contain a raw '}' (a signed token),
    /// so the id is the first "id" field inside the init call rather than "the next braces".
    /// </summary>
    [GeneratedRegex(@"init_View(?:Html)?Game\([\s\S]{0,8000}?""id""\s*:\s*(\d+)")]
    private static partial Regex InitGameIdRegex();

    [GeneratedRegex(@"""min_price"":(\d+)")]
    private static partial Regex MinPriceRegex();

    [GeneratedRegex(@"""actual_price"":(\d+)")]
    private static partial Regex ActualPriceRegex();

    [GeneratedRegex(@"of\s+([0-9][0-9,]*)")]
    private static partial Regex CommentsTotalRegex();

    [GeneratedRegex(@"itch\.io/jam/([a-z0-9\-_]+)/rate/", RegexOptions.IgnoreCase)]
    private static partial Regex JamSlugRegex();
}
