using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace ItchDB.Infrastructure.Parsing;

public static partial class ParseHelpers
{
    private static readonly HtmlParser Parser = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IHtmlDocument ParseHtml(string html) => Parser.ParseDocument(html);

    /// <summary>Parses "1,794", "14.4k", "516k projects", "1.2m" and similar counters.</summary>
    public static int? ParseCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = CountRegex().Match(text.Replace("\u00a0", " "));
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, Inv, out var value)) return null;
        var suffix = m.Groups[2].Value.ToLowerInvariant();
        value *= suffix switch { "k" => 1_000, "m" => 1_000_000, _ => 1 };
        return (int)Math.Round(value);
    }

    /// <summary>"16 September 2026 @ 06:44 UTC" (abbr titles on itch pages).</summary>
    public static DateTime? ParseItchDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        string[] formats = ["d MMMM yyyy @ HH:mm 'UTC'", "d MMMM yyyy @ H:mm 'UTC'", "d MMM yyyy @ HH:mm 'UTC'"];
        if (DateTime.TryParseExact(t, formats, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return ParseLooseDate(t);
    }

    /// <summary>RFC 1123 dates from RSS, ISO dates from jam pages and "2026-07-31 17:00:00".</summary>
    public static DateTime? ParseLooseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text.Trim(), Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return null;
    }

    /// <summary>"$7.99 USD", "$19.99", "€5" to cents and currency code.</summary>
    public static (int? Cents, string? Currency) ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var m = MoneyRegex().Match(text);
        if (!m.Success) return (null, null);
        var symbol = m.Groups[1].Value;
        if (!decimal.TryParse(m.Groups[2].Value.Replace(",", ""), NumberStyles.Number, Inv, out var amount)) return (null, null);
        var code = m.Groups[3].Success && m.Groups[3].Value.Length == 3 ? m.Groups[3].Value : symbol switch
        {
            "$" => "USD",
            "€" => "EUR",
            "£" => "GBP",
            "¥" => "JPY",
            _ => null,
        };
        return ((int)Math.Round(amount * 100m), code);
    }

    public static int? ParsePercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = PercentRegex().Match(text);
        return m.Success ? int.Parse(m.Groups[1].Value, Inv) : null;
    }

    public static double? ParseDouble(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, Inv, out var v) ? v : null;

    public static long? ParseLong(string? text) =>
        long.TryParse(text?.Trim(), NumberStyles.Integer, Inv, out var v) ? v : null;

    public static string Clean(string? text) =>
        string.IsNullOrEmpty(text) ? "" : WhitespaceRegex().Replace(text, " ").Trim();

    /// <summary>"https://itch.io/games/tag-2d" -> "tag-2d"; returns null for other links.</summary>
    public static string? FilterSlugFromHref(string? href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        var m = FilterHrefRegex().Match(href);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string AbsoluteUrl(string href) =>
        href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href
        : href.StartsWith('/') ? "https://itch.io" + href
        : href;

    public static string? Attr(this IElement? el, string name) => el?.GetAttribute(name);

    public static string Text(this IElement? el) => Clean(el?.TextContent);

    [GeneratedRegex(@"([0-9][0-9,]*(?:\.[0-9]+)?)\s*([kKmM]?)(?![a-zA-Z])")]
    private static partial Regex CountRegex();

    [GeneratedRegex(@"([$€£¥]|[A-Z]{3}\s?)?\s*([0-9][0-9,]*(?:\.[0-9]+)?)\s*([A-Z]{3})?")]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"(\d+)\s*%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"itch\.io/(?:games|physical-games|tools|game-assets|game-mods|soundtracks|comics|books|misc)/([a-z0-9][a-z0-9\-_.]*)(?:[/?#]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex FilterHrefRegex();
}
