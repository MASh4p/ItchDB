using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Core.Analytics;

public enum SegmentKind
{
    All,
    Genre,
    Tag,
    Engine,
    Platform,
    Price,
    Duration,
    Status,
    Multiplayer,
    Input,
    Language,
    Accessibility,
    Ai,
    Jam,
}

/// <summary>A group of games: a genre, a tag, a platform, a price model...</summary>
public sealed class SegmentDef
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required SegmentKind Kind { get; init; }
    public int? TagId { get; init; }
    public Func<GameRow, bool>? Predicate { get; init; }

    /// <summary>itch.io filter slug whose "(N results)" counts the whole segment; "" for the catalog.</summary>
    public string? FilterKey { get; init; }

    public override string ToString() => Name;
}

public static class Segments
{
    public static readonly SegmentDef All = new()
    {
        Key = "all", Name = "Весь itch.io", Kind = SegmentKind.All, FilterKey = "", Predicate = _ => true,
    };

    public static SegmentDef AllOf(UiLanguage? language = null) => new()
    {
        Key = "all",
        Name = CatalogNames.Pick(Lang(language), "Весь itch.io", "All of itch.io"),
        Kind = SegmentKind.All,
        FilterKey = "",
        Predicate = _ => true,
    };

    public static string KindName(SegmentKind kind) => KindName(kind, null);

    public static string KindName(SegmentKind kind, UiLanguage? language) => CatalogNames.SegmentKindName(kind, Lang(language));

    private static UiLanguage Lang(UiLanguage? language) => language ?? TextLang.Current;

    public static SegmentKind FromTagKind(TagKind kind) => kind switch
    {
        TagKind.Genre => SegmentKind.Genre,
        TagKind.Engine => SegmentKind.Engine,
        TagKind.Duration => SegmentKind.Duration,
        TagKind.Language => SegmentKind.Language,
        TagKind.Input => SegmentKind.Input,
        TagKind.Accessibility => SegmentKind.Accessibility,
        TagKind.Multiplayer => SegmentKind.Multiplayer,
        TagKind.Platform => SegmentKind.Platform,
        _ => SegmentKind.Tag,
    };

    public static SegmentDef FromTag(TagRow t, string? name = null, SegmentKind? kind = null) => new()
    {
        Key = t.Slug,
        Name = name ?? DisplayName(t),
        Kind = kind ?? (ItchCatalog.IsGenreSlug(t.Slug) ? SegmentKind.Genre : FromTagKind(t.Kind)),
        TagId = t.Id,
        FilterKey = t.Slug,
    };

    public static string DisplayName(TagRow t) => DisplayName(t, null);

    public static string DisplayName(TagRow t, UiLanguage? language)
    {
        var lang = Lang(language);
        if (ItchCatalog.GenreFromSlug(t.Slug) is { } g) return CatalogNames.Genre(g, lang);
        if (t.Kind == TagKind.Duration) return CatalogNames.Duration(t.Slug, lang);
        return t.Name.Length > 0 ? t.Name : ItchCatalog.NameFromSlug(t.Slug);
    }

    /// <summary>The 18 itch.io genres (membership through genre filters and game pages).</summary>
    public static List<SegmentDef> Genres(AnalyticsDataset d, UiLanguage? language = null)
    {
        var lang = Lang(language);
        return ItchCatalog.Genres.Select(g =>
        {
            var name = CatalogNames.Genre(g, lang);
            return d.Tag(g.Slug) is { } t
                ? FromTag(t, name, SegmentKind.Genre)
                : new SegmentDef { Key = g.Slug, Name = name, Kind = SegmentKind.Genre, FilterKey = g.Slug, Predicate = x => x.Genre == g.Slug };
        }).ToList();
    }

    /// <summary>Tags of a kind with at least <paramref name="minMembers"/> known games.</summary>
    public static List<SegmentDef> OfKind(AnalyticsDataset d, TagKind kind, int minMembers, bool excludeGenres = true, UiLanguage? language = null)
    {
        var lang = Lang(language);
        return d.Tags.Values
            .Where(t => t.Kind == kind && d.Members(t.Id).Count >= minMembers && !(excludeGenres && ItchCatalog.IsGenreSlug(t.Slug)))
            .Select(t => FromTag(t, DisplayName(t, lang)))
            .ToList();
    }

    public static List<SegmentDef> Platforms(UiLanguage? language = null)
    {
        var lang = Lang(language);
        return ItchCatalog.PlatformFilters.Select(p => new SegmentDef
        {
            Key = p.Slug, Name = CatalogNames.Platform(p.Slug, lang), Kind = SegmentKind.Platform, FilterKey = p.Slug,
            Predicate = g => (g.Platforms & p.Flag) != 0,
        }).ToList();
    }

    public static List<SegmentDef> Prices(UiLanguage? language = null)
    {
        var lang = Lang(language);
        return
        [
            new() { Key = "price-free", Name = CatalogNames.PriceKey("price-free", lang), Kind = SegmentKind.Price, FilterKey = "free", Predicate = g => g.Price == PriceModel.Free },
            new() { Key = "price-nyp", Name = CatalogNames.PriceKey("price-nyp", lang), Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.NameYourPrice },
            new() { Key = "price-paid", Name = CatalogNames.PriceKey("price-paid", lang), Kind = SegmentKind.Price, FilterKey = "store", Predicate = g => g.Price == PriceModel.Paid },
            new() { Key = "price-paid-5", Name = CatalogNames.PriceKey("price-paid-5", lang), Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.Paid && g.PriceCents <= 500 },
            new() { Key = "price-paid-15", Name = CatalogNames.PriceKey("price-paid-15", lang), Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.Paid && g.PriceCents is > 500 and <= 1500 },
            new() { Key = "price-paid-more", Name = CatalogNames.PriceKey("price-paid-more", lang), Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.Paid && g.PriceCents > 1500 },
        ];
    }

    public static List<SegmentDef> Statuses(UiLanguage? language = null)
    {
        var lang = Lang(language);
        return ItchCatalog.Statuses.Select(s => new SegmentDef
        {
            Key = "status-" + s.Status.ToString().ToLowerInvariant(), Name = CatalogNames.Status(s.Status, lang), Kind = SegmentKind.Status,
            Predicate = g => g.Status == s.Status,
        }).ToList();
    }

    public static List<SegmentDef> AiUsages(UiLanguage? language = null)
    {
        var lang = Lang(language);
        return
        [
            new() { Key = "ai-none", Name = CatalogNames.PriceKey("ai-none", lang), Kind = SegmentKind.Ai, Predicate = g => g.Ai == AiUsage.NoAi },
            new() { Key = "ai-used", Name = CatalogNames.PriceKey("ai-used", lang), Kind = SegmentKind.Ai, Predicate = g => g.Ai == AiUsage.UsesAi },
            new() { Key = "jam-entry", Name = CatalogNames.PriceKey("jam-entry", lang), Kind = SegmentKind.Jam, Predicate = g => g.IsJamEntry },
        ];
    }

    /// <summary>Everything the formats table on the developer page compares.</summary>
    public static List<SegmentDef> Formats(AnalyticsDataset d, int minMembers, UiLanguage? language = null)
    {
        var lang = Lang(language);
        var list = new List<SegmentDef>();
        list.AddRange(Platforms(lang));
        list.AddRange(Prices(lang));
        list.AddRange(OfKind(d, TagKind.Duration, minMembers, language: lang));
        list.AddRange(OfKind(d, TagKind.Multiplayer, minMembers, language: lang));
        list.AddRange(OfKind(d, TagKind.Input, minMembers, language: lang));
        list.AddRange(Statuses(lang));
        list.AddRange(AiUsages(lang));
        return list;
    }

    public static IEnumerable<GameRow> Members(AnalyticsDataset d, SegmentDef s, AnalyticsFilter f)
    {
        IEnumerable<GameRow> source = s.TagId is { } id ? d.Members(id) : d.Games;
        if (s.TagId is null && s.Predicate is { } p) source = source.Where(p);
        return source.Where(f.Includes);
    }
}
