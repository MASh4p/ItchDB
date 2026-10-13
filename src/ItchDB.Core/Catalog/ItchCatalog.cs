using ItchDB.Core.Domain;

namespace ItchDB.Core.Catalog;

public sealed record GenreInfo(string Slug, string EnglishName, string RussianName);

public sealed record FacetValue(string Slug, string Name);

/// <summary>Static vocabulary of itch.io browse filters.</summary>
public static class ItchCatalog
{
    public const string BaseUrl = "https://itch.io";
    public const int PageSize = 36;
    public const int MaxPages = 200;
    public const int FirstYear = 2013;

    public static readonly IReadOnlyList<GenreInfo> Genres =
    [
        new("genre-action", "Action", "Экшен"),
        new("genre-adventure", "Adventure", "Приключения"),
        new("tag-card-game", "Card Game", "Карточные"),
        new("tag-educational", "Educational", "Обучающие"),
        new("tag-fighting", "Fighting", "Файтинги"),
        new("tag-interactive-fiction", "Interactive Fiction", "Интерактивная литература"),
        new("genre-platformer", "Platformer", "Платформеры"),
        new("genre-puzzle", "Puzzle", "Головоломки"),
        new("tag-racing", "Racing", "Гонки"),
        new("tag-rhythm", "Rhythm", "Ритм-игры"),
        new("genre-rpg", "Role Playing", "RPG"),
        new("genre-shooter", "Shooter", "Шутеры"),
        new("genre-simulation", "Simulation", "Симуляторы"),
        new("genre-sports", "Sports", "Спорт"),
        new("genre-strategy", "Strategy", "Стратегии"),
        new("tag-survival", "Survival", "Выживание"),
        new("genre-visual-novel", "Visual Novel", "Визуальные новеллы"),
        new("genre-other", "Other", "Другое"),
    ];

    private static readonly Dictionary<string, GenreInfo> GenreByName =
        Genres.ToDictionary(g => g.EnglishName, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, GenreInfo> GenreBySlug =
        Genres.ToDictionary(g => g.Slug, StringComparer.OrdinalIgnoreCase);

    public static GenreInfo? GenreFromName(string? englishName) =>
        englishName is not null && GenreByName.TryGetValue(englishName.Trim(), out var g) ? g : null;

    public static GenreInfo? GenreFromSlug(string? slug) =>
        slug is not null && GenreBySlug.TryGetValue(slug, out var g) ? g : null;

    public static bool IsGenreSlug(string slug) => GenreBySlug.ContainsKey(slug);

    public static readonly IReadOnlyList<(Platforms Flag, string Slug, string Name)> PlatformFilters =
    [
        (Platforms.Web, "platform-web", "Браузер"),
        (Platforms.Windows, "platform-windows", "Windows"),
        (Platforms.MacOS, "platform-osx", "macOS"),
        (Platforms.Linux, "platform-linux", "Linux"),
        (Platforms.Android, "platform-android", "Android"),
        (Platforms.IOS, "platform-ios", "iOS"),
    ];

    public static readonly IReadOnlyList<FacetValue> Durations =
    [
        new("duration-seconds", "Несколько секунд"),
        new("duration-minutes", "Несколько минут"),
        new("duration-half-hour", "Около получаса"),
        new("duration-hour", "Около часа"),
        new("duration-hours", "Несколько часов"),
        new("duration-days", "Дни и больше"),
    ];

    public static readonly IReadOnlyList<(GameStatus Status, string Name)> Statuses =
    [
        (GameStatus.Released, "Выпущена"),
        (GameStatus.InDevelopment, "В разработке"),
        (GameStatus.Prototype, "Прототип"),
        (GameStatus.OnHold, "Заморожена"),
        (GameStatus.Canceled, "Отменена"),
    ];

    public static GameStatus ParseStatus(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "released" => GameStatus.Released,
        "in development" => GameStatus.InDevelopment,
        "prototype" => GameStatus.Prototype,
        "on hold" => GameStatus.OnHold,
        "canceled" or "cancelled" => GameStatus.Canceled,
        _ => GameStatus.Unknown,
    };

    public static string StatusName(GameStatus status) =>
        Statuses.FirstOrDefault(s => s.Status == status).Name ?? "Неизвестно";

    public static string PriceModelName(PriceModel model) => model switch
    {
        PriceModel.Free => "Бесплатно",
        PriceModel.NameYourPrice => "Своя цена",
        PriceModel.Paid => "Платно",
        _ => "Неизвестно",
    };

    /// <summary>Tags treated as adult content in addition to membership in /games/nsfw lists.</summary>
    public static readonly HashSet<string> NsfwTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "tag-nsfw", "tag-adult", "tag-erotic", "tag-eroge", "tag-hentai", "tag-porn", "tag-lewd",
        "tag-sexual-content", "tag-nudity", "tag-fetish", "tag-sex", "tag-18", "tag-adult-content",
        "tag-porn-game", "tag-yaoi-nsfw", "tag-nsfw-18", "tag-r18", "tag-smut",
    };

    public static TagKind KindFromSlug(string slug)
    {
        if (IsGenreSlug(slug) && slug.StartsWith("genre-", StringComparison.Ordinal)) return TagKind.Genre;
        if (slug.StartsWith("tag-", StringComparison.Ordinal)) return TagKind.Tag;
        if (slug.StartsWith("genre-", StringComparison.Ordinal)) return TagKind.Genre;
        if (slug.StartsWith("made-with-", StringComparison.Ordinal)) return TagKind.Engine;
        if (slug.StartsWith("duration-", StringComparison.Ordinal)) return TagKind.Duration;
        if (slug.StartsWith("lang-", StringComparison.Ordinal)) return TagKind.Language;
        if (slug.StartsWith("input-", StringComparison.Ordinal)) return TagKind.Input;
        if (slug.StartsWith("accessibility-", StringComparison.Ordinal)) return TagKind.Accessibility;
        if (slug.StartsWith("multiplayer-", StringComparison.Ordinal)) return TagKind.Multiplayer;
        if (slug.StartsWith("platform-", StringComparison.Ordinal)) return TagKind.Platform;
        return TagKind.Other;
    }

    public static string KindName(TagKind kind) => CatalogNames.TagKindName(kind, TextLang.Current);

    /// <summary>Human readable fallback for a slug without a stored name: "tag-pixel-art" -> "pixel art".</summary>
    public static string NameFromSlug(string slug)
    {
        var genre = GenreFromSlug(slug);
        if (genre is not null) return genre.EnglishName;
        var dash = slug.IndexOf('-');
        var tail = slug.StartsWith("made-with-", StringComparison.Ordinal) ? slug["made-with-".Length..]
            : dash >= 0 ? slug[(dash + 1)..] : slug;
        return tail.Replace('-', ' ');
    }

    public static string GameUrlKey(string url)
    {
        var u = url.Trim();
        if (u.EndsWith('/')) u = u[..^1];
        if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u[7..];
        var q = u.IndexOfAny(['?', '#']);
        if (q >= 0) u = u[..q];
        return u.ToLowerInvariant();
    }

    /// <summary>"https://adamgryu.itch.io/a-short-hike" -> "adamgryu".</summary>
    public static string? UsernameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        if (!host.EndsWith(".itch.io", StringComparison.Ordinal)) return null;
        var name = host[..^".itch.io".Length];
        return name.Length == 0 || name == "www" ? null : name;
    }
}
