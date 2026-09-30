using System.Text.RegularExpressions;

namespace ItchDB.Infrastructure.Parsing;

/// <summary>Parses https://itch.io/tags?page=N.</summary>
public static partial class TagsPageParser
{
    public static TagCatalogPage Parse(string html)
    {
        var doc = ParseHelpers.ParseHtml(html);
        var entries = new List<TagCatalogEntry>();
        foreach (var cell in doc.QuerySelectorAll("li.tag_cell"))
        {
            var title = cell.QuerySelector("a.tag_title");
            var slug = ParseHelpers.FilterSlugFromHref(ParseHelpers.AbsoluteUrl(title?.GetAttribute("href") ?? ""));
            if (slug is null) continue;

            var related = new List<string>();
            foreach (var a in cell.QuerySelectorAll(".related_tags a[href]"))
            {
                var href = a.GetAttribute("href") ?? "";
                if (!href.StartsWith("/games/", StringComparison.Ordinal)) continue;
                foreach (var part in href["/games/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
                    if (part != slug && !related.Contains(part)) related.Add(part);
            }

            entries.Add(new TagCatalogEntry
            {
                Slug = slug,
                Name = title.Text(),
                ProjectsCount = ParseHelpers.ParseCount(cell.QuerySelector(".games_count strong")?.TextContent),
                ThisWeekCount = ParseHelpers.ParseCount(cell.QuerySelector(".games_count a")?.TextContent),
                Description = cell.QuerySelector(".tag_description").Text() is { Length: > 0 } d ? d : null,
                RelatedSlugs = related,
            });
        }

        var (current, total) = ParsePager(doc.QuerySelector(".pager_label")?.TextContent);
        var all = doc.QuerySelectorAll("select.tag_selector option[value^='/games/']")
            .Select(o => new LinkRef(o.GetAttribute("value")!["/games/".Length..], ParseHelpers.Clean(o.TextContent)))
            .Where(l => l.Slug.StartsWith("tag-", StringComparison.Ordinal))
            .DistinctBy(l => l.Slug)
            .ToList();

        return new TagCatalogPage { Entries = entries, CurrentPage = current, TotalPages = total, AllTags = all };
    }

    public static (int Current, int Total) ParsePager(string? label)
    {
        var m = PagerRegex().Match(label ?? "");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (1, 1);
    }

    [GeneratedRegex(@"Page\s+(\d+)\s+of\s+(\d+)")]
    private static partial Regex PagerRegex();
}
