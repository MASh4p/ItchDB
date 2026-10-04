namespace ItchDB.Infrastructure.Http;

/// <summary>Proxy chosen for this process by the <c>--proxy</c> launch argument. Empty means a direct connection.</summary>
public sealed class LaunchProxy
{
    public string Address { get; set; } = "";

    /// <summary>Reads <c>--proxy url</c> or <c>--proxy=url</c>. No argument leaves the address empty.</summary>
    public static bool TryReadArgument(string[] args, out string address, out string? error)
    {
        address = "";
        error = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? raw = null;
            if (arg.Equals("--proxy", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    error = "После --proxy укажите адрес, например --proxy http://127.0.0.1:10809";
                    return false;
                }
                raw = args[++i];
            }
            else if (arg.StartsWith("--proxy=", StringComparison.OrdinalIgnoreCase))
            {
                raw = arg["--proxy=".Length..];
            }
            else continue;

            if (!LocalProxy.TryParse(raw, out var uri, out error) || uri is null)
            {
                error ??= "Адрес прокси пуст";
                return false;
            }
            address = LocalProxy.Canonical(raw);
        }
        return true;
    }
}

/// <summary>A local HTTP or SOCKS5 proxy address passed at launch.</summary>
public static class LocalProxy
{
    public static bool TryParse(string? text, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var raw = text.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "http://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
        {
            error = "Адрес прокси не распознан. Пример: http://127.0.0.1:10809";
            return false;
        }

        var scheme = parsed.Scheme.Equals("socks", StringComparison.OrdinalIgnoreCase) ? "socks5" : parsed.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https" or "socks5"))
        {
            error = "Нужна схема http или socks5";
            return false;
        }
        if (parsed.Port <= 0)
        {
            error = "Укажите порт, например http://127.0.0.1:10809";
            return false;
        }

        var user = string.IsNullOrEmpty(parsed.UserInfo) ? "" : parsed.UserInfo + "@";
        uri = new Uri($"{scheme}://{user}{parsed.Host}:{parsed.Port}");
        return true;
    }

    /// <summary>Value stored in settings and passed to Chrome. Empty when no proxy is configured.</summary>
    public static string Canonical(string? text)
    {
        if (!TryParse(text, out var uri, out _) || uri is null) return "";
        return uri.ToString().TrimEnd('/');
    }

    /// <summary>Chrome's --proxy-server value, without userinfo (Chrome asks for a password itself).</summary>
    public static string? ForChrome(string? text)
    {
        if (!TryParse(text, out var uri, out _) || uri is null) return null;
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
