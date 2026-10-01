using System.Text.RegularExpressions;

namespace ItchDB.Infrastructure.Http;

/// <summary>
/// Client hints a Chrome user-agent promises. Cloudflare compares them with the browser that solved the challenge.
/// </summary>
internal static partial class BrowserHeaders
{
    public static void Apply(HttpRequestMessage request, string? userAgent, string accept)
    {
        if (string.IsNullOrWhiteSpace(userAgent) || ChromeVersion().Match(userAgent) is not { Success: true } chrome)
            return;

        var version = chrome.Groups[1].Value;
        request.Headers.TryAddWithoutValidation("sec-ch-ua",
            $"\"Chromium\";v=\"{version}\", \"Google Chrome\";v=\"{version}\", \"Not.A/Brand\";v=\"24\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"" + Platform(userAgent) + "\"");
        if (!accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)) return;

        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "document");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "navigate");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "none");
        request.Headers.TryAddWithoutValidation("sec-fetch-user", "?1");
        request.Headers.TryAddWithoutValidation("upgrade-insecure-requests", "1");
    }

    private static string Platform(string userAgent)
    {
        if (userAgent.Contains("Windows", StringComparison.Ordinal)) return "Windows";
        if (userAgent.Contains("Mac OS", StringComparison.Ordinal)) return "macOS";
        if (userAgent.Contains("Linux", StringComparison.Ordinal)) return "Linux";
        if (userAgent.Contains("Android", StringComparison.Ordinal)) return "Android";
        return "Windows";
    }

    [GeneratedRegex(@"Chrome/(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ChromeVersion();
}
