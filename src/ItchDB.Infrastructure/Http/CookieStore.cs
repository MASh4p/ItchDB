using System.Net;
using System.Text.Json;
using ItchDB.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Http;

/// <summary>Cookie jar shared by all itch.io requests and persisted between app runs.</summary>
public sealed class CookieStore
{
    private readonly string _file;
    private readonly ILogger<CookieStore> _log;
    private readonly object _gate = new();
    private DateTime _lastSave = DateTime.MinValue;

    public CookieStore(DataPaths paths, ILogger<CookieStore> log)
    {
        _file = paths.CookiesFile;
        _log = log;
        Load();
    }

    public CookieContainer Container { get; } = new() { PerDomainCapacity = 100 };

    public void ImportCfClearance(string value) => Import(Parse(value));

    public void Import(IReadOnlyList<(string Name, string Value)> pairs)
    {
        foreach (var (name, value) in pairs)
        {
            var cookie = new Cookie(name, value, "/", ".itch.io")
            {
                Secure = true,
                HttpOnly = true,
                Expires = DateTime.UtcNow.AddDays(name == "__cf_bm" ? 1 : 30),
            };
            Container.Add(cookie);
        }
        Save(force: true);
    }

    /// <summary>
    /// Accepts the bare cf_clearance value, a DevTools cookie row, or a Cookie header
    /// (cf_clearance plus __cf_bm and the rest).
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Вставьте значение cookie cf_clearance.");

        raw = raw.Trim().Trim('"');
        var cookieLine = raw.Split('\n', '\r')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase));
        if (cookieLine is not null) raw = cookieLine["cookie:".Length..].Trim();

        var pairs = new List<(string Name, string Value)>();
        if (raw.Contains("cf_clearance=", StringComparison.Ordinal) || raw.Contains(';'))
        {
            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var name = part[..eq].Trim();
                if (name.Contains(' ') || IgnoredAttributes.Contains(name)) continue;
                pairs.Add((name, part[(eq + 1)..].Trim().Trim('"')));
            }
        }
        else
        {
            var cols = raw.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length >= 2 && cols[0] is "cf_clearance" or "__cf_bm")
                pairs.Add((cols[0], cols[1]));
            else
            {
                var value = raw.ReplaceLineEndings("").Trim();
                if (value.Length < 20 || value.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or '"' or '\''))
                    throw new FormatException(
                        "Не найдено значение cf_clearance. Откройте DevTools на itch.io → Application → Cookies → https://itch.io и скопируйте значение cf_clearance. Через консоль (document.cookie) его не видно.");
                pairs.Add(("cf_clearance", value));
            }
        }

        var distinct = pairs.Where(p => p.Value.Length > 0).GroupBy(p => p.Name, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        if (!distinct.Any(p => p.Name == "cf_clearance" && p.Value.Length >= 20))
            throw new FormatException(
                "Не найдено значение cf_clearance. Откройте DevTools на itch.io → Application → Cookies → https://itch.io и скопируйте значение cf_clearance. Через консоль (document.cookie) его не видно.");
        return distinct;
    }

    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Path", "Domain", "Expires", "Max-Age", "SameSite", "Secure", "HttpOnly", "Priority",
    };

    public void Clear()
    {
        foreach (Cookie c in Container.GetAllCookies()) c.Expired = true;
        Save(force: true);
    }

    public int Count => Container.GetAllCookies().Count(c => !c.Expired);

    public void Save(bool force = false)
    {
        lock (_gate)
        {
            if (!force && DateTime.UtcNow - _lastSave < TimeSpan.FromSeconds(30)) return;
            _lastSave = DateTime.UtcNow;
            try
            {
                var list = Container.GetAllCookies()
                    .Where(c => !c.Expired)
                    .Select(c => new StoredCookie(c.Name, c.Value, c.Domain, c.Path, c.Expires == DateTime.MinValue ? null : c.Expires, c.Secure, c.HttpOnly))
                    .ToList();
                File.WriteAllText(_file, JsonSerializer.Serialize(list));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to save cookies");
            }
        }
    }

    private void Load()
    {
        if (!File.Exists(_file)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<StoredCookie>>(File.ReadAllText(_file)) ?? [];
            foreach (var c in list)
            {
                if (c.Expires is { } exp && exp < DateTime.UtcNow) continue;
                var cookie = new Cookie(c.Name, c.Value, c.Path, c.Domain) { Secure = c.Secure, HttpOnly = c.HttpOnly };
                if (c.Expires is { } e) cookie.Expires = e;
                Container.Add(cookie);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load cookies, starting with an empty jar");
        }
    }

    private sealed record StoredCookie(string Name, string Value, string Domain, string Path, DateTime? Expires, bool Secure, bool HttpOnly);
}
