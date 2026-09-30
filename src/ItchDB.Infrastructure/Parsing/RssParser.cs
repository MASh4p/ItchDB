using System.Text.RegularExpressions;
using System.Xml.Linq;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Infrastructure.Parsing;

/// <summary>Parses browse RSS feeds (/games/newest.xml, /games/featured.xml, ...).</summary>
public static partial class RssParser
{
    public static List<RssGameItem> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var items = new List<RssGameItem>();
        foreach (var item in doc.Descendants("item"))
        {
            var link = item.Element("link")?.Value.Trim() ?? item.Element("guid")?.Value.Trim();
            if (string.IsNullOrEmpty(link)) continue;

            var rawTitle = item.Element("title")?.Value ?? "";
            var plain = item.Element("plainTitle")?.Value;
            var brackets = BracketRegex().Matches(rawTitle).Select(m => m.Groups[1].Value.Trim()).ToList();
            var (cents, _) = ParseHelpers.ParseMoney(item.Element("price")?.Value);

            var platforms = Platforms.None;
            var pl = item.Element("platforms");
            if (pl is not null)
            {
                foreach (var e in pl.Elements())
                {
                    if (!e.Value.Equals("yes", StringComparison.OrdinalIgnoreCase)) continue;
                    platforms |= e.Name.LocalName switch
                    {
                        "html" => Platforms.Web,
                        "windows" => Platforms.Windows,
                        "osx" => Platforms.MacOS,
                        "linux" => Platforms.Linux,
                        "android" => Platforms.Android,
                        "ios" => Platforms.IOS,
                        _ => Platforms.None,
                    };
                }
            }
            foreach (var b in brackets)
            {
                platforms |= b switch
                {
                    "Windows" => Platforms.Windows,
                    "macOS" => Platforms.MacOS,
                    "Linux" => Platforms.Linux,
                    "Android" => Platforms.Android,
                    "iOS" => Platforms.IOS,
                    "HTML5" or "Play in browser" => Platforms.Web,
                    _ => Platforms.None,
                };
            }

            items.Add(new RssGameItem
            {
                Url = link,
                Title = !string.IsNullOrWhiteSpace(plain) ? plain.Trim() : BracketRegex().Replace(rawTitle, "").Trim(),
                ImageUrl = item.Element("imageurl")?.Value.Trim(),
                PriceCents = cents,
                Currency = item.Element("currency")?.Value.Trim(),
                PubDate = ParseHelpers.ParseLooseDate(item.Element("pubDate")?.Value),
                CreateDate = ParseHelpers.ParseLooseDate(item.Element("createDate")?.Value),
                UpdateDate = ParseHelpers.ParseLooseDate(item.Element("updateDate")?.Value),
                Platforms = platforms,
                GenreName = brackets.Select(ItchCatalog.GenreFromName).FirstOrDefault(g => g is not null)?.EnglishName,
                Description = ParseHelpers.Clean(TagRegex().Replace(item.Element("description")?.Value ?? "", " ")),
            });
        }
        return items;
    }

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
}
