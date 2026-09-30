using System.Globalization;

namespace ItchDB.Web.Services;

/// <summary>Russian number, date and duration formatting used across the UI.</summary>
public static class Fmt
{
    public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string N(long v) => v.ToString("N0", Ru);

    public static string N(long? v) => v is null ? "—" : N(v.Value);

    public static string N(double? v, int digits = 1) => v is null || double.IsNaN(v.Value) ? "—" : v.Value.ToString("N" + digits, Ru);

    /// <summary>Compact form for big counts: 1,2 тыс., 3,4 млн.</summary>
    public static string Short(double? v)
    {
        if (v is null || double.IsNaN(v.Value)) return "—";
        var x = v.Value;
        return Math.Abs(x) switch
        {
            >= 1_000_000 => (x / 1_000_000).ToString("0.#", Ru) + " млн",
            >= 10_000 => (x / 1_000).ToString("0", Ru) + " тыс.",
            >= 1_000 => (x / 1_000).ToString("0.#", Ru) + " тыс.",
            _ => x.ToString(x % 1 == 0 ? "0" : "0.#", Ru),
        };
    }

    public static string Pct(double? share, int digits = 0) =>
        share is null || double.IsNaN(share.Value) ? "—" : (share.Value * 100).ToString("N" + digits, Ru) + "%";

    public static string Signed(double? v, int digits = 0, string suffix = "")
    {
        if (v is null || double.IsNaN(v.Value)) return "—";
        var s = v.Value.ToString("N" + digits, Ru);
        return (v.Value > 0 ? "+" : "") + s + suffix;
    }

    public static string Times(double? v) => v is null || double.IsNaN(v.Value) ? "—" : "×" + v.Value.ToString(v.Value >= 10 ? "0" : "0.0#", Ru);

    public static string Money(int? cents, string? currency)
    {
        if (cents is null) return "—";
        if (cents == 0) return "бесплатно";
        var sym = currency switch { "USD" or null => "$", "EUR" => "€", "GBP" => "£", "JPY" => "¥", _ => currency + " " };
        return sym + (cents.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    public static string Local(DateTime? utc, string format = "dd.MM.yyyy HH:mm") => utc is null ? "—" : ToLocal(utc.Value).ToString(format, Ru);

    public static string Date(DateTime? utc) => Local(utc, "dd.MM.yyyy");

    public static string Date(DateOnly? day) => day is null ? "—" : day.Value.ToString("dd.MM.yyyy", Ru);

    public static string Ago(DateTime? utc)
    {
        if (utc is null) return "никогда";
        var d = DateTime.UtcNow - utc.Value;
        if (d < TimeSpan.Zero) return "через " + Duration(-d);
        if (d.TotalMinutes < 1) return "только что";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} мин назад";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} ч назад";
        if (d.TotalDays < 60) return $"{(int)d.TotalDays} {Plural((int)d.TotalDays, "день", "дня", "дней")} назад";
        return Date(utc);
    }

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
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
        ItchDB.Core.Domain.PriceModel.Free => "бесплатно",
        ItchDB.Core.Domain.PriceModel.NameYourPrice => g.PriceCents > 0 ? $"от {Money(g.PriceCents, null)}" : "своя цена",
        ItchDB.Core.Domain.PriceModel.Paid => Money(g.PriceCents, null) + (g.SalePercent > 0 ? $" (−{g.SalePercent}%)" : ""),
        _ => "—",
    };

    public static string Stars(double? avg) => avg is null ? "—" : avg.Value.ToString("0.00", Ru) + "★";

    public static string Status(ItchDB.Core.Domain.GameStatus s) => s switch
    {
        ItchDB.Core.Domain.GameStatus.Released => "выпущена",
        ItchDB.Core.Domain.GameStatus.InDevelopment => "в разработке",
        ItchDB.Core.Domain.GameStatus.Prototype => "прототип",
        ItchDB.Core.Domain.GameStatus.OnHold => "заморожена",
        ItchDB.Core.Domain.GameStatus.Canceled => "отменена",
        _ => "—",
    };
}
