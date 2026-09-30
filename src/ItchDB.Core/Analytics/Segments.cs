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

    public static string KindName(SegmentKind kind) => kind switch
    {
        SegmentKind.All => "Всё",
        SegmentKind.Genre => "Жанр",
        SegmentKind.Tag => "Тег",
        SegmentKind.Engine => "Движок",
        SegmentKind.Platform => "Платформа",
        SegmentKind.Price => "Цена",
        SegmentKind.Duration => "Длительность",
        SegmentKind.Status => "Статус",
        SegmentKind.Multiplayer => "Мультиплеер",
        SegmentKind.Input => "Управление",
        SegmentKind.Language => "Язык",
        SegmentKind.Accessibility => "Доступность",
        SegmentKind.Ai => "ИИ",
        SegmentKind.Jam => "Джемы",
        _ => kind.ToString(),
    };

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

    public static string DisplayName(TagRow t)
    {
        if (ItchCatalog.GenreFromSlug(t.Slug) is { } g) return g.RussianName;
        if (t.Kind == TagKind.Duration && ItchCatalog.Durations.FirstOrDefault(d => d.Slug == t.Slug) is { } d) return d.Name;
        return t.Name.Length > 0 ? t.Name : ItchCatalog.NameFromSlug(t.Slug);
    }

    /// <summary>The 18 itch.io genres (membership through genre filters and game pages).</summary>
    public static List<SegmentDef> Genres(AnalyticsDataset d) =>
        ItchCatalog.Genres.Select(g => d.Tag(g.Slug) is { } t
            ? FromTag(t, g.RussianName, SegmentKind.Genre)
            : new SegmentDef { Key = g.Slug, Name = g.RussianName, Kind = SegmentKind.Genre, FilterKey = g.Slug, Predicate = x => x.Genre == g.Slug })
        .ToList();

    /// <summary>Tags of a kind with at least <paramref name="minMembers"/> known games.</summary>
    public static List<SegmentDef> OfKind(AnalyticsDataset d, TagKind kind, int minMembers, bool excludeGenres = true) =>
        d.Tags.Values
            .Where(t => t.Kind == kind && d.Members(t.Id).Count >= minMembers && !(excludeGenres && ItchCatalog.IsGenreSlug(t.Slug)))
            .Select(t => FromTag(t))
            .ToList();

    public static List<SegmentDef> Platforms() =>
        ItchCatalog.PlatformFilters.Select(p => new SegmentDef
        {
            Key = p.Slug, Name = p.Name, Kind = SegmentKind.Platform, FilterKey = p.Slug,
            Predicate = g => (g.Platforms & p.Flag) != 0,
        }).ToList();

    public static List<SegmentDef> Prices() =>
    [
        new() { Key = "price-free", Name = "Бесплатные", Kind = SegmentKind.Price, FilterKey = "free", Predicate = g => g.Price == PriceModel.Free },
        new() { Key = "price-nyp", Name = "Своя цена (от $0)", Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.NameYourPrice },
        new() { Key = "price-paid", Name = "Платные", Kind = SegmentKind.Price, FilterKey = "store", Predicate = g => g.Price == PriceModel.Paid },
        new() { Key = "price-paid-5", Name = "Платные до $5", Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.Paid && g.PriceCents <= 500 },
        new() { Key = "price-paid-15", Name = "Платные $5–15", Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.Paid && g.PriceCents is > 500 and <= 1500 },
        new() { Key = "price-paid-more", Name = "Платные дороже $15", Kind = SegmentKind.Price, Predicate = g => g.Price == PriceModel.Paid && g.PriceCents > 1500 },
    ];

    public static List<SegmentDef> Statuses() =>
        ItchCatalog.Statuses.Select(s => new SegmentDef
        {
            Key = "status-" + s.Status.ToString().ToLowerInvariant(), Name = s.Name, Kind = SegmentKind.Status,
            Predicate = g => g.Status == s.Status,
        }).ToList();

    public static List<SegmentDef> AiUsages() =>
    [
        new() { Key = "ai-none", Name = "Без генеративного ИИ", Kind = SegmentKind.Ai, Predicate = g => g.Ai == AiUsage.NoAi },
        new() { Key = "ai-used", Name = "С генеративным ИИ", Kind = SegmentKind.Ai, Predicate = g => g.Ai == AiUsage.UsesAi },
        new() { Key = "jam-entry", Name = "Сделаны на джем", Kind = SegmentKind.Jam, Predicate = g => g.IsJamEntry },
    ];

    /// <summary>Everything the formats table on the developer page compares.</summary>
    public static List<SegmentDef> Formats(AnalyticsDataset d, int minMembers)
    {
        var list = new List<SegmentDef>();
        list.AddRange(Platforms());
        list.AddRange(Prices());
        list.AddRange(OfKind(d, TagKind.Duration, minMembers));
        list.AddRange(OfKind(d, TagKind.Multiplayer, minMembers));
        list.AddRange(OfKind(d, TagKind.Input, minMembers));
        list.AddRange(Statuses());
        list.AddRange(AiUsages());
        return list;
    }

    public static IEnumerable<GameRow> Members(AnalyticsDataset d, SegmentDef s, AnalyticsFilter f)
    {
        IEnumerable<GameRow> source = s.TagId is { } id ? d.Members(id) : d.Games;
        if (s.TagId is null && s.Predicate is { } p) source = source.Where(p);
        return source.Where(f.Includes);
    }
}
