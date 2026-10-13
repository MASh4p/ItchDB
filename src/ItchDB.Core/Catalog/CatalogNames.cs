using ItchDB.Core.Analytics;
using ItchDB.Core.Domain;

namespace ItchDB.Core.Catalog;

/// <summary>User-facing catalog labels in Russian and English. Stored tag names from itch.io stay as published.</summary>
public static class CatalogNames
{
    public static string Pick(UiLanguage language, string russian, string english) =>
        language == UiLanguage.En ? english : russian;

    public static string Genre(GenreInfo genre, UiLanguage language) =>
        language == UiLanguage.En ? genre.EnglishName : genre.RussianName;

    public static string SegmentKindName(SegmentKind kind, UiLanguage language) => kind switch
    {
        SegmentKind.All => Pick(language, "Всё", "All"),
        SegmentKind.Genre => Pick(language, "Жанр", "Genre"),
        SegmentKind.Tag => Pick(language, "Тег", "Tag"),
        SegmentKind.Engine => Pick(language, "Движок", "Engine"),
        SegmentKind.Platform => Pick(language, "Платформа", "Platform"),
        SegmentKind.Price => Pick(language, "Цена", "Price"),
        SegmentKind.Duration => Pick(language, "Длительность", "Length"),
        SegmentKind.Status => Pick(language, "Статус", "Status"),
        SegmentKind.Multiplayer => Pick(language, "Мультиплеер", "Multiplayer"),
        SegmentKind.Input => Pick(language, "Управление", "Input"),
        SegmentKind.Language => Pick(language, "Язык", "Language"),
        SegmentKind.Accessibility => Pick(language, "Доступность", "Accessibility"),
        SegmentKind.Ai => Pick(language, "ИИ", "AI"),
        SegmentKind.Jam => Pick(language, "Джемы", "Jams"),
        _ => kind.ToString(),
    };

    public static string TagKindName(TagKind kind, UiLanguage language) => kind switch
    {
        TagKind.Tag => Pick(language, "Тег", "Tag"),
        TagKind.Genre => Pick(language, "Жанр", "Genre"),
        TagKind.Engine => Pick(language, "Движок", "Engine"),
        TagKind.Duration => Pick(language, "Длительность сессии", "Session length"),
        TagKind.Language => Pick(language, "Язык", "Language"),
        TagKind.Input => Pick(language, "Управление", "Input"),
        TagKind.Accessibility => Pick(language, "Доступность", "Accessibility"),
        TagKind.Multiplayer => Pick(language, "Мультиплеер", "Multiplayer"),
        TagKind.Platform => Pick(language, "Платформа", "Platform"),
        _ => Pick(language, "Прочее", "Other"),
    };

    public static string Platform(string slug, UiLanguage language) => slug switch
    {
        "platform-web" => Pick(language, "Браузер", "Browser"),
        "platform-windows" => "Windows",
        "platform-osx" => "macOS",
        "platform-linux" => "Linux",
        "platform-android" => "Android",
        "platform-ios" => "iOS",
        _ => ItchCatalog.NameFromSlug(slug),
    };

    public static string Duration(string slug, UiLanguage language) => slug switch
    {
        "duration-seconds" => Pick(language, "Несколько секунд", "A few seconds"),
        "duration-minutes" => Pick(language, "Несколько минут", "A few minutes"),
        "duration-half-hour" => Pick(language, "Около получаса", "About half an hour"),
        "duration-hour" => Pick(language, "Около часа", "About an hour"),
        "duration-hours" => Pick(language, "Несколько часов", "A few hours"),
        "duration-days" => Pick(language, "Дни и больше", "Days or more"),
        _ => ItchCatalog.NameFromSlug(slug),
    };

    public static string Status(GameStatus status, UiLanguage language) => status switch
    {
        GameStatus.Released => Pick(language, "Выпущена", "Released"),
        GameStatus.InDevelopment => Pick(language, "В разработке", "In development"),
        GameStatus.Prototype => Pick(language, "Прототип", "Prototype"),
        GameStatus.OnHold => Pick(language, "Заморожена", "On hold"),
        GameStatus.Canceled => Pick(language, "Отменена", "Canceled"),
        _ => "—",
    };

    public static string PriceKey(string key, UiLanguage language) => key switch
    {
        "price-free" => Pick(language, "Бесплатные", "Free"),
        "price-nyp" => Pick(language, "Своя цена (от $0)", "Name your price (from $0)"),
        "price-paid" => Pick(language, "Платные", "Paid"),
        "price-paid-5" => Pick(language, "Платные до $5", "Paid up to $5"),
        "price-paid-15" => Pick(language, "Платные $5–15", "Paid $5–15"),
        "price-paid-more" => Pick(language, "Платные дороже $15", "Paid over $15"),
        "ai-none" => Pick(language, "Без генеративного ИИ", "No generative AI"),
        "ai-used" => Pick(language, "С генеративным ИИ", "Uses generative AI"),
        "jam-entry" => Pick(language, "Сделаны на джем", "Made in a jam"),
        _ => key,
    };
}
