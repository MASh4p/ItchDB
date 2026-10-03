using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Infrastructure.Parsing;

/// <summary>Parses browse pages: the JSON variant (?format=json) and the full HTML page.</summary>
public static partial class ListingParser
{
    /// <summary>
    /// JSON lists are sometimes answered with the HTML page (a browser navigation, or a check page).
    /// An HTML body is read as a listing instead of failing the JSON parser.
    /// </summary>
    public static ListingPage ParseRequested(string body, bool json)
    {
        if (json && !body.AsSpan().TrimStart().StartsWith("<")) return ParseJson(body);
        return ParseHtml(body);
    }

    public static ListingPage ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var content = root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
        var num = root.TryGetProperty("num_items", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
        int? page = root.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
        var cards = ParseCells(ParseHelpers.ParseHtml(content));
        return new ListingPage
        {
            Cards = cards,
            NumItems = num,
            Page = page,
            HasNextPage = num >= ItchCatalog.PageSize,
        };
    }

    public static ListingPage ParseHtml(string html)
    {
        var doc = ParseHelpers.ParseHtml(html);
        var cards = ParseCells(doc);
        return new ListingPage
        {
            Cards = cards,
            NumItems = cards.Count,
            TotalResults = ParseHelpers.ParseCount(doc.QuerySelector(".game_count")?.TextContent),
            Page = 1,
            HasNextPage = doc.QuerySelector(".next_page a, a.next_page") is not null,
        };
    }

    public static List<ParsedGameCard> ParseCells(IParentNode root)
    {
        var result = new List<ParsedGameCard>();
        foreach (var cell in root.QuerySelectorAll("div.game_cell[data-game_id]"))
        {
            var id = ParseHelpers.ParseLong(cell.GetAttribute("data-game_id"));
            var titleLink = cell.QuerySelector(".game_title a.title") ?? cell.QuerySelector("a.title");
            var url = titleLink?.GetAttribute("href") ?? cell.QuerySelector("a.thumb_link")?.GetAttribute("href");
            if (id is null || string.IsNullOrEmpty(url)) continue;

            var img = cell.QuerySelector(".game_thumb img");
            var cover = img?.GetAttribute("data-lazy_src") ?? img?.GetAttribute("src");
            var author = cell.QuerySelector(".game_author a");
            var priceText = cell.QuerySelector(".price_tag .price_value").Text();
            var (cents, currency) = ParseHelpers.ParseMoney(priceText);
            var (avg, count) = ParseRating(cell);
            var genre = cell.QuerySelector(".game_genre").Text();
            var shortText = cell.QuerySelector(".game_text");

            result.Add(new ParsedGameCard
            {
                Id = id.Value,
                Url = url,
                Title = titleLink.Text(),
                ShortText = NullIfEmpty(shortText?.GetAttribute("title") ?? shortText.Text()),
                CoverUrl = cover,
                CoverIsGif = cell.QuerySelector(".gif_label, .gif_overlay") is not null
                             || (cover?.Contains(".gif", StringComparison.OrdinalIgnoreCase) ?? false),
                AuthorName = NullIfEmpty(author.Text()),
                AuthorUrl = author?.GetAttribute("href"),
                AuthorId = ParseUserId(author?.GetAttribute("data-label")),
                GenreName = NullIfEmpty(genre),
                PriceCents = string.IsNullOrEmpty(priceText) ? null : cents,
                Currency = currency,
                SalePercent = ParseHelpers.ParsePercent(cell.QuerySelector(".sale_tag").Text()),
                Platforms = ParsePlatforms(cell),
                HasRating = count is not null,
                RatingAvg = avg,
                RatingCount = count,
            });
        }
        return result;
    }

    public static Platforms ParsePlatforms(IElement cell)
    {
        var platforms = Platforms.None;
        var box = cell.QuerySelector(".game_platform");
        if (box is null) return platforms;
        if (box.QuerySelector(".web_flag") is not null) platforms |= Platforms.Web;
        foreach (var icon in box.QuerySelectorAll("span[title], span.icon"))
        {
            var title = icon.GetAttribute("title") ?? "";
            var cls = icon.ClassName ?? "";
            if (title.Contains("Windows", StringComparison.OrdinalIgnoreCase) || cls.Contains("icon-windows")) platforms |= Platforms.Windows;
            if (title.Contains("Linux", StringComparison.OrdinalIgnoreCase) || cls.Contains("icon-tux")) platforms |= Platforms.Linux;
            if (title.Contains("macOS", StringComparison.OrdinalIgnoreCase) || title.Contains("Mac", StringComparison.Ordinal) || cls.Contains("icon-apple")) platforms |= Platforms.MacOS;
            if (title.Contains("Android", StringComparison.OrdinalIgnoreCase) || cls.Contains("icon-android")) platforms |= Platforms.Android;
            if (title.Contains("iOS", StringComparison.Ordinal)) platforms |= Platforms.IOS;
        }
        return platforms;
    }

    private static (double? Avg, int? Count) ParseRating(IElement cell)
    {
        var rating = cell.QuerySelector(".game_rating");
        if (rating is null) return (null, null);
        var tip = rating.GetAttribute("data-tooltip");
        if (tip is not null)
        {
            var m = RatingTooltipRegex().Match(tip);
            if (m.Success)
                return (ParseHelpers.ParseDouble(m.Groups[1].Value), ParseHelpers.ParseCount(m.Groups[2].Value));
        }
        var count = ParseHelpers.ParseCount(rating.QuerySelector(".rating_count").Text());
        var width = StarWidthRegex().Match(rating.QuerySelector(".star_fill")?.GetAttribute("style") ?? "");
        double? avg = width.Success ? Math.Round(ParseHelpers.ParseDouble(width.Groups[1].Value)!.Value / 20.0, 2) : null;
        return (avg, count);
    }

    internal static long? ParseUserId(string? dataLabel)
    {
        if (string.IsNullOrEmpty(dataLabel)) return null;
        var m = UserLabelRegex().Match(dataLabel);
        return m.Success ? long.Parse(m.Groups[1].Value) : null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"([0-9.]+)\s+average rating from\s+([0-9,]+)\s+total rating")]
    internal static partial Regex RatingTooltipRegex();

    [GeneratedRegex(@"width:\s*([0-9.]+)%")]
    private static partial Regex StarWidthRegex();

    [GeneratedRegex(@"user:(\d+)")]
    private static partial Regex UserLabelRegex();
}
