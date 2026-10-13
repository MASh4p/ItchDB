using System.Globalization;
using ItchDB.Core;

namespace ItchDB.Web.Services;

/// <summary>Russian number, date and duration formatting used across the UI.</summary>
public static class Fmt
{
    public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static bool En => TextLang.Current == UiLanguage.En;
    private static CultureInfo C => En ? CultureInfo.GetCultureInfo("en-US") : Ru;

    public static string N(long v) => v.ToString("N0", C);

    public static string N(long? v) => v is null ? "—" : N(v.Value);

    public static string N(double? v, int digits = 1) => v is null || double.IsNaN(v.Value) ? "—" : v.Value.ToString("N" + digits, C);

    /// <summary>Compact form for big counts: 1.2k, 3.4M or 1,2 тыс., 3,4 млн.</summary>
    public static string Short(double? v)
    {
        if (v is null || double.IsNaN(v.Value)) return "—";
        var x = v.Value;
        if (En)
        {
            return Math.Abs(x) switch
            {
                >= 1_000_000 => (x / 1_000_000).ToString("0.#", C) + "M",
                >= 10_000 => (x / 1_000).ToString("0", C) + "k",
                >= 1_000 => (x / 1_000).ToString("0.#", C) + "k",
                _ => x.ToString(x % 1 == 0 ? "0" : "0.#", C),
            };
        }
        return Math.Abs(x) switch
        {
            >= 1_000_000 => (x / 1_000_000).ToString("0.#", C) + " млн",
            >= 10_000 => (x / 1_000).ToString("0", C) + " тыс.",
            >= 1_000 => (x / 1_000).ToString("0.#", C) + " тыс.",
            _ => x.ToString(x % 1 == 0 ? "0" : "0.#", C),
        };
    }

    public static string Pct(double? share, int digits = 0) =>
        share is null || double.IsNaN(share.Value) ? "—" : (share.Value * 100).ToString("N" + digits, C) + "%";

    public static string Signed(double? v, int digits = 0, string suffix = "")
    {
        if (v is null || double.IsNaN(v.Value)) return "—";
        var s = v.Value.ToString("N" + digits, C);
        return (v.Value > 0 ? "+" : "") + s + suffix;
    }

    public static string Times(double? v) => v is null || double.IsNaN(v.Value) ? "—" : "×" + v.Value.ToString(v.Value >= 10 ? "0" : "0.0#", C);

    public static string Money(int? cents, string? currency)
    {
        if (cents is null) return "—";
        if (cents == 0) return En ? "free" : "бесплатно";
        var sym = currency switch { "USD" or null => "$", "EUR" => "€", "GBP" => "£", "JPY" => "¥", _ => currency + " " };
        return sym + (cents.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    public static string Local(DateTime? utc, string format = "dd.MM.yyyy HH:mm") => utc is null ? "—" : ToLocal(utc.Value).ToString(format, C);

    public static string Date(DateTime? utc) => Local(utc, "dd.MM.yyyy");

    public static string Date(DateOnly? day) => day is null ? "—" : day.Value.ToString("dd.MM.yyyy", C);

    public static string Ago(DateTime? utc)
    {
        if (utc is null) return En ? "never" : "никогда";
        var d = DateTime.UtcNow - utc.Value;
        if (d < TimeSpan.Zero) return (En ? "in " : "через ") + Duration(-d);
        if (d.TotalMinutes < 1) return En ? "just now" : "только что";
        if (d.TotalHours < 1) return En ? $"{(int)d.TotalMinutes} min ago" : $"{(int)d.TotalMinutes} мин назад";
        if (d.TotalDays < 1) return En ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalHours} ч назад";
        if (d.TotalDays < 60)
        {
            var days = (int)d.TotalDays;
            return En ? $"{days} {(days == 1 ? "day" : "days")} ago" : $"{days} {Plural(days, "день", "дня", "дней")} назад";
        }
        return Date(utc);
    }

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (En)
        {
            if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} s";
            if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min";
            if (t.TotalHours < 48) return $"{(int)t.TotalHours} h {t.Minutes:00} min";
            var days = (int)t.TotalDays;
            return $"{days} {(days == 1 ? "day" : "days")}";
        }
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} с";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} мин";
        if (t.TotalHours < 48) return $"{(int)t.TotalHours} ч {t.Minutes:00} мин";
        return $"{(int)t.TotalDays} {Plural((int)t.TotalDays, "день", "дня", "дней")}";
    }

    public static string Countdown(DateTime untilUtc)
    {
        var t = untilUtc - DateTime.UtcNow;
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    public static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        if (n is >= 11 and <= 19) return many;
        return (n % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    public static string Count(int n, string one, string few, string many) => $"{N(n)} {Plural(n, one, few, many)}";

    public static string Platforms(ItchDB.Core.Domain.Platforms p)
    {
        if (p == ItchDB.Core.Domain.Platforms.None) return "—";
        var parts = new List<string>(6);
        if (p.HasFlag(ItchDB.Core.Domain.Platforms.Web)) parts.Add("Web");
        if (p.HasFlag(ItchDB.Core.Domain.Platforms.Windows)) parts.Add("Win");
        if (p.HasFlag(ItchDB.Core.Domain.Platforms.MacOS)) parts.Add("Mac");
        if (p.HasFlag(ItchDB.Core.Domain.Platforms.Linux)) parts.Add("Linux");
        if (p.HasFlag(ItchDB.Core.Domain.Platforms.Android)) parts.Add("Android");
        if (p.HasFlag(ItchDB.Core.Domain.Platforms.IOS)) parts.Add("iOS");
        return string.Join(" · ", parts);
    }

    public static string Price(ItchDB.Core.Analytics.GameRow g) => g.Price switch
    {
        ItchDB.Core.Domain.PriceModel.Free => En ? "free" : "бесплатно",
        ItchDB.Core.Domain.PriceModel.NameYourPrice => g.PriceCents > 0 ? (En ? $"from {Money(g.PriceCents, null)}" : $"от {Money(g.PriceCents, null)}") : (En ? "name your price" : "своя цена"),
        ItchDB.Core.Domain.PriceModel.Paid => Money(g.PriceCents, null) + (g.SalePercent > 0 ? $" (−{g.SalePercent}%)" : ""),
        _ => "—",
    };

    public static string Stars(double? avg) => avg is null ? "—" : avg.Value.ToString("0.00", C) + "★";

    public static string Status(ItchDB.Core.Domain.GameStatus s) => s switch
    {
        ItchDB.Core.Domain.GameStatus.Released => En ? "released" : "выпущена",
        ItchDB.Core.Domain.GameStatus.InDevelopment => En ? "in development" : "в разработке",
        ItchDB.Core.Domain.GameStatus.Prototype => En ? "prototype" : "прототип",
        ItchDB.Core.Domain.GameStatus.OnHold => En ? "on hold" : "заморожена",
        ItchDB.Core.Domain.GameStatus.Canceled => En ? "canceled" : "отменена",
        _ => "—",
    };
}
