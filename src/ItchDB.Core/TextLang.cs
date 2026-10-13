namespace ItchDB.Core;

public enum UiLanguage
{
    Ru,
    En,
}

/// <summary>
/// UI language. <see cref="Set"/> is per execution (tests stay isolated).
/// <see cref="SetApp"/> is the process language the Blazor UI falls back to,
/// because a render often starts on a fresh execution context.
/// </summary>
public static class TextLang
{
    private static readonly AsyncLocal<UiLanguage?> Local = new();
    private static UiLanguage _app = UiLanguage.Ru;

    public static UiLanguage App => _app;

    /// <summary>
    /// When set, <see cref="Current"/> follows <see cref="SetApp"/> and ignores a stale per-execution value.
    /// The Blazor circuit keeps an old value across renders; tests leave this off and use <see cref="Set"/>.
    /// </summary>
    public static bool PreferApp { get; set; }

    public static UiLanguage Current => PreferApp ? _app : (Local.Value ?? _app);

    public static void Set(UiLanguage language) => Local.Value = language;

    public static void SetApp(UiLanguage language) => _app = language;

    public static UiLanguage Parse(string? value) =>
        string.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ? UiLanguage.En : UiLanguage.Ru;

    public static string Code(UiLanguage language) => language == UiLanguage.En ? "en" : "ru";
}
