namespace ItchDB.Infrastructure.Parsing;

/// <summary>Parses https://itch.io/profile/{username}.</summary>
public static class ProfileParser
{
    public static ProfileData Parse(string html)
    {
        var doc = ParseHelpers.ParseHtml(html);
        int? followers = null, following = null;
        foreach (var box in doc.QuerySelectorAll(".stats_container .stat_box"))
        {
            var label = box.QuerySelector(".stat_label").Text().ToLowerInvariant();
            var exact = ParseHelpers.ParseCount(box.GetAttribute("title"));
            var shown = ParseHelpers.ParseCount(box.QuerySelector(".stat_value")?.TextContent);
            if (label.StartsWith("follower", StringComparison.Ordinal)) followers = exact ?? shown;
            else if (label.StartsWith("following", StringComparison.Ordinal)) following = exact ?? shown;
        }

        var carousel = doc.QuerySelector(".game_carousel_widget");
        return new ProfileData
        {
            DisplayName = doc.QuerySelector(".stat_header_widget h2").Text() is { Length: > 0 } n ? n : null,
            Followers = followers,
            Following = following,
            RegisteredAt = ParseHelpers.ParseItchDate(doc.QuerySelector(".user_data abbr[title]")?.GetAttribute("title")),
            CreatorGames = carousel?.QuerySelectorAll(".game_cell").Length ?? 0,
        };
    }
}
