using System.Text.Json;
using System.Text.RegularExpressions;
using ItchDB.Core.Domain;

namespace ItchDB.Infrastructure.Parsing;

public static partial class JamParsers
{
    /// <summary>Parses /jams/past, /jams/upcoming, /jams/in-progress list pages.</summary>
    public static JamListPage ParseList(string html)
    {
        var doc = ParseHelpers.ParseHtml(html);
        var jams = new List<JamCell>();
        foreach (var cell in doc.QuerySelectorAll(".jam_grid_widget .jam"))
        {
            var link = cell.QuerySelector(".primary_info h3 a[href]") ?? cell.QuerySelector("h3 a[href]");
            var slugMatch = JamHrefRegex().Match(link?.GetAttribute("href") ?? "");
            if (!slugMatch.Success) continue;

            var dateEl = cell.QuerySelector(".date_countdown");
            var dateRow = dateEl?.ParentElement?.TextContent ?? "";
            var phase = dateRow.TrimStart() switch
            {
                var s when s.StartsWith("Ended", StringComparison.OrdinalIgnoreCase) => JamPhase.Ended,
                var s when s.StartsWith("Voting", StringComparison.OrdinalIgnoreCase) => JamPhase.Voting,
                var s when s.StartsWith("Starts", StringComparison.OrdinalIgnoreCase) => JamPhase.Upcoming,
                var s when s.StartsWith("Ends", StringComparison.OrdinalIgnoreCase) => JamPhase.Running,
                _ => JamPhase.Unknown,
            };

            int? joined = null, submissions = null;
            foreach (var stat in cell.QuerySelectorAll(".jam_stats .stat"))
            {
                var text = stat.TextContent;
                var value = ParseHelpers.ParseCount(stat.QuerySelector(".number")?.TextContent);
                if (text.Contains("joined", StringComparison.OrdinalIgnoreCase)) joined = value;
                else if (text.Contains("submission", StringComparison.OrdinalIgnoreCase)) submissions = value;
            }

            jams.Add(new JamCell
            {
                Slug = slugMatch.Groups[1].Value,
                Title = link.Text(),
                Hosts = string.Join(", ", cell.QuerySelectorAll(".hosted_by a").Select(a => a.Text())) is { Length: > 0 } h ? h : null,
                CoverUrl = cell.QuerySelector(".jam_cover")?.GetAttribute("data-background_image"),
                Phase = phase,
                Date = ParseHelpers.ParseLooseDate(dateEl?.GetAttribute("title")) ?? ParseHelpers.ParseLooseDate(dateEl?.TextContent),
                Joined = joined,
                Submissions = submissions,
                IsRanked = cell.QuerySelector(".jam_ranked") is not null,
                IsFeatured = cell.QuerySelector(".featured_flag") is not null,
            });
        }

        var (current, total) = TagsPageParser.ParsePager(doc.QuerySelector(".pager_label")?.TextContent);
        return new JamListPage { Jams = jams, CurrentPage = current, TotalPages = total };
    }

    /// <summary>Parses a jam overview page, /jam/{slug}.</summary>
    public static JamPageData ParseJamPage(string html)
    {
        var doc = ParseHelpers.ParseHtml(html);
        long? id = null;
        DateTime? start = null, end = null, voting = null;
        var m = ViewJamRegex().Match(html);
        if (m.Success)
        {
            try
            {
                using var json = JsonDocument.Parse(m.Groups[1].Value);
                var r = json.RootElement;
                if (r.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var v)) id = v;
                start = ParseHelpers.ParseLooseDate(r.TryGetProperty("start_date", out var s) ? s.GetString() : null);
                end = ParseHelpers.ParseLooseDate(r.TryGetProperty("end_date", out var e) ? e.GetString() : null);
                voting = ParseHelpers.ParseLooseDate(r.TryGetProperty("voting_end_date", out var ve) ? ve.GetString() : null);
            }
            catch (JsonException)
            {
            }
        }

        int? entries = null, ratings = null, joined = null;
        foreach (var box in doc.QuerySelectorAll(".stats_container .stat_box"))
        {
            var label = box.QuerySelector(".stat_label").Text().ToLowerInvariant();
            var value = ParseHelpers.ParseCount(box.GetAttribute("title")) ?? ParseHelpers.ParseCount(box.QuerySelector(".stat_value")?.TextContent);
            if (label.StartsWith("entr", StringComparison.Ordinal) || label.StartsWith("submission", StringComparison.Ordinal)) entries = value;
            else if (label.StartsWith("rating", StringComparison.Ordinal)) ratings = value;
            else if (label.StartsWith("joined", StringComparison.Ordinal)) joined = value;
        }

        return new JamPageData
        {
            ItchJamId = id,
            Title = doc.QuerySelector(".jam_title_header a").Text() is { Length: > 0 } t ? t : null,
            Hosts = string.Join(", ", doc.QuerySelectorAll(".jam_host_header a[href*='.itch.io']").Select(a => a.Text())) is { Length: > 0 } h ? h : null,
            StartAt = start,
            EndAt = end,
            VotingEndAt = voting,
            Entries = entries,
            Ratings = ratings,
            Joined = joined,
        };
    }

    /// <summary>Parses https://itch.io/jam/{id}/entries.json.</summary>
    public static List<JamEntryData> ParseEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<JamEntryData>();
        if (!doc.RootElement.TryGetProperty("jam_games", out var games) || games.ValueKind != JsonValueKind.Array) return list;
        foreach (var entry in games.EnumerateArray())
        {
            if (!entry.TryGetProperty("game", out var g) || !g.TryGetProperty("id", out var gid) || !gid.TryGetInt64(out var gameId)) continue;
            var platforms = Platforms.None;
            if (g.TryGetProperty("platforms", out var pl) && pl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pl.EnumerateArray())
                {
                    platforms |= p.GetString() switch
                    {
                        "web" => Platforms.Web,
                        "windows" => Platforms.Windows,
                        "osx" => Platforms.MacOS,
                        "linux" => Platforms.Linux,
                        "android" => Platforms.Android,
                        "ios" => Platforms.IOS,
                        _ => Platforms.None,
                    };
                }
            }
            JsonElement user = default;
            var hasUser = g.TryGetProperty("user", out user) && user.ValueKind == JsonValueKind.Object;
            list.Add(new JamEntryData
            {
                GameId = gameId,
                Title = Str(g, "title") ?? "",
                Url = Str(g, "url") ?? "",
                CoverUrl = Str(g, "cover"),
                ShortText = Str(g, "short_text"),
                Platforms = platforms,
                AuthorId = hasUser && user.TryGetProperty("id", out var uid) && uid.TryGetInt64(out var u) ? u : null,
                AuthorName = hasUser ? Str(user, "name") : null,
                AuthorUrl = hasUser ? Str(user, "url") : null,
                RatingCount = entry.TryGetProperty("rating_count", out var rc) && rc.TryGetInt32(out var r) ? r : null,
                Coolness = entry.TryGetProperty("coolness", out var c) && c.TryGetDouble(out var cv) ? cv : null,
                SubmittedAt = ParseHelpers.ParseLooseDate(Str(entry, "created_at")),
            });
        }
        return list;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex(@"^/jam/([a-z0-9\-_]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex JamHrefRegex();

    [GeneratedRegex(@"I\.ViewJam\([^,]+,\s*(\{[^{}]*\})\)")]
    private static partial Regex ViewJamRegex();
}
