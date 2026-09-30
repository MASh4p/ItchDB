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

    public void ImportCfClearance(string value)
    {
        var cookie = new Cookie("cf_clearance", value.Trim(), "/", ".itch.io")
        {
            Secure = true,
            HttpOnly = true,
            Expires = DateTime.UtcNow.AddDays(30),
        };
        Container.Add(cookie);
        Save(force: true);
    }

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
