using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ItchDB.Infrastructure.Http;

public sealed record ApiGame(long Id, string Title, string? Url, int Views, int Downloads, int Purchases, bool Published);

public sealed class ItchApiException(string message) : Exception(message);

/// <summary>The official itch.io API: stats of the key owner's games, which itch.io shows to nobody else.</summary>
public sealed class ItchApiClient(HttpClient http)
{
    private const string MyGamesUrl = "https://api.itch.io/profile/games";

    public async Task<List<ApiGame>> MyGamesAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MyGamesUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ItchApiException("itch.io отклонил ключ API. Создайте новый на itch.io/user/settings/api-keys.");
        if (!response.IsSuccessStatusCode)
            throw new ItchApiException($"itch.io API ответил HTTP {(int)response.StatusCode}.");
        return ParseMyGames(body);
    }

    public static List<ApiGame> ParseMyGames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            throw new ItchApiException("itch.io API: " + string.Join("; ", errors.EnumerateArray().Select(e => e.ToString())));
        if (!root.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array) return [];

        var result = new List<ApiGame>();
        foreach (var g in games.EnumerateArray())
        {
            if (!g.TryGetProperty("id", out var id) || !id.TryGetInt64(out var gameId)) continue;
            result.Add(new ApiGame(
                gameId,
                Str(g, "title") ?? gameId.ToString(),
                Str(g, "url"),
                Int(g, "views_count"),
                Int(g, "downloads_count"),
                Int(g, "purchases_count"),
                g.TryGetProperty("published", out var p) && p.ValueKind == JsonValueKind.True));
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
