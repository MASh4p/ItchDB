using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ItchDB.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace ItchDB.Infrastructure.Http;

public sealed record BrowserPage(int Status, string Body, string FinalUrl);

/// <summary>
/// Loads itch.io pages through a real Chrome or Edge window. Cloudflare ties its check to the browser,
/// so a cookie copied into the app's own HTTP client is rejected; the same check passes in Chrome itself.
/// </summary>
public sealed class BrowserFetcher(DataPaths paths, CookieStore cookies, NetworkStatus status, ILogger<BrowserFetcher> log) : IDisposable
{
    public const string SolveNotice =
        "В окне Chrome проверка Cloudflare. Если она не исчезла сама, пройдите её там — сбор продолжится и пауза не понадобится.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SolveTimeout = TimeSpan.FromMinutes(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _profile = Path.Combine(paths.Root, "browser-profile");
    private readonly string _pidFile = Path.Combine(paths.Root, "browser.pid");
    private Process? _process;
    private CdpSession? _session;
    private string? _browserName;
    private bool _announced;

    public string? BrowserName => _browserName;

    public async Task<BrowserPage?> TryGetAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!await EnsureAsync(ct)) return null;
            await SyncCookiesAsync(ct);
            var page = await LoadAsync(url, ct);
            if (!ItchHttpClient.IsChallengeBody(page.Body)) return page;

            status.SetNotice(SolveNotice);
            log.LogWarning("Cloudflare check in the Chrome window for {Url}. Waiting up to {Minutes} min for it to be solved", url, SolveTimeout.TotalMinutes);
            var solved = await WaitForSolutionAsync(ct);
            status.SetNotice(null);
            return solved ?? page;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Browser fetch failed for {Url}; using the plain HTTP client", url);
            Disconnect();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> EnsureAsync(CancellationToken ct)
    {
        if (_session is { IsOpen: true }) return true;
        var exe = FindBrowser();
        if (exe is null)
        {
            log.LogWarning("Chrome and Edge were not found; itch.io requests stay on the plain HTTP client");
            return false;
        }

        _browserName = Path.GetFileNameWithoutExtension(exe);
        Directory.CreateDirectory(_profile);
        KillPrevious();
        var port = FreePort();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
        };
        psi.ArgumentList.Add($"--remote-debugging-port={port}");
        psi.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        psi.ArgumentList.Add($"--user-data-dir={_profile}");
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        psi.ArgumentList.Add("--disable-sync");
        psi.ArgumentList.Add("--window-size=1100,800");
        psi.ArgumentList.Add("about:blank");
        _process = Process.Start(psi) ?? throw new IOException($"Не удалось запустить {_browserName}");
        File.WriteAllText(_pidFile, _process.Id.ToString());

        var ws = await WaitForPageSocketAsync(port, ct);
        _session = await CdpSession.ConnectAsync(ws, ct);
        await _session.CommandAsync("Network.enable", new { }, ct);
        await _session.CommandAsync("Page.enable", new { }, ct);
        if (!_announced)
        {
            _announced = true;
            log.LogInformation("itch.io requests go through {Browser}. A check, if it appears, is solved in that window", _browserName);
        }
        return true;
    }

    private async Task SyncCookiesAsync(CancellationToken ct)
    {
        foreach (Cookie cookie in cookies.Container.GetAllCookies())
        {
            if (cookie.Expired || string.IsNullOrEmpty(cookie.Name)) continue;
            await _session!.CommandAsync("Network.setCookie", new
            {
                name = cookie.Name,
                value = cookie.Value,
                domain = string.IsNullOrEmpty(cookie.Domain) ? ".itch.io" : cookie.Domain,
                path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
                secure = true,
                httpOnly = cookie.HttpOnly,
                url = "https://itch.io/",
            }, ct);
        }
    }

    private async Task<BrowserPage> LoadAsync(string url, CancellationToken ct)
    {
        _session!.Drain();
        var nav = await _session.CommandAsync("Page.navigate", new { url }, ct);
        if (nav["errorText"] is JsonNode error)
            throw new IOException(error.GetValue<string>());

        var hit = await ReadDocumentAsync(PageTimeout, ct) ?? throw new IOException("Страница не загрузилась");
        var body = await BodyAsync(hit.RequestId, ct);
        var finalUrl = await LocationAsync(ct) ?? hit.Url;
        return new BrowserPage(hit.Status, body, finalUrl);
    }

    private async Task<BrowserPage?> WaitForSolutionAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + SolveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var wait = deadline - DateTime.UtcNow;
            var hit = await ReadDocumentAsync(wait, ct, requireLoad: true);
            if (hit is null) return null;
            var body = await BodyAsync(hit.RequestId, ct);
            if (!ItchHttpClient.IsChallengeBody(body))
            {
                var finalUrl = await LocationAsync(ct) ?? hit.Url;
                log.LogInformation("Cloudflare check solved in the browser");
                return new BrowserPage(hit.Status, body, finalUrl);
            }
        }
        return null;
    }

    private async Task<DocumentHit?> ReadDocumentAsync(TimeSpan timeout, CancellationToken ct, bool requireLoad = false)
    {
        var deadline = DateTime.UtcNow + timeout;
        DocumentHit? hit = null;
        var loaded = false;
        while (DateTime.UtcNow < deadline)
        {
            var ev = await _session!.NextEventAsync(deadline - DateTime.UtcNow, ct);
            if (ev is null) break;
            if (ev.Value.Method == "Network.responseReceived" && DocumentOf(ev.Value.Params) is { } doc)
                hit = doc;
            else if (ev.Value.Method == "Page.loadEventFired")
                loaded = true;
            if (hit is not null && loaded && hit.Status is < 300 or >= 400) return hit;
        }
        return requireLoad ? null : hit;
    }

    private static DocumentHit? DocumentOf(JsonNode p)
    {
        if (p["type"]?.GetValue<string>() != "Document") return null;
        var response = p["response"];
        var status = response?["status"]?.GetValue<int>() ?? 0;
        if (status is >= 300 and < 400) return null;
        var requestId = p["requestId"]?.GetValue<string>();
        var url = response?["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(url)) return null;
        if (!url.Contains("://itch.io/", StringComparison.OrdinalIgnoreCase) && !url.Contains(".itch.io/", StringComparison.OrdinalIgnoreCase))
            return null;
        return new DocumentHit(requestId, status, url);
    }

    private async Task<string> BodyAsync(string requestId, CancellationToken ct)
    {
        try
        {
            var result = await _session!.CommandAsync("Network.getResponseBody", new { requestId }, ct);
            var body = result["body"]?.GetValue<string>() ?? "";
            if (result["base64Encoded"]?.GetValue<bool>() == true)
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            if (body.Length > 0) return body;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            log.LogDebug(ex, "Response body was not available from the network log");
        }
        return await EvaluateAsync("document.documentElement ? document.documentElement.outerHTML : ''", ct) ?? "";
    }

    private async Task<string?> LocationAsync(CancellationToken ct) =>
        await EvaluateAsync("location.href", ct);

    private async Task<string?> EvaluateAsync(string expression, CancellationToken ct)
    {
        var result = await _session!.CommandAsync("Runtime.evaluate", new { expression, returnByValue = true }, ct);
        return result["result"]?["value"]?.GetValue<string>();
    }

    private static async Task<string> WaitForPageSocketAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", ct);
                var page = JsonNode.Parse(json)!.AsArray().FirstOrDefault(n => n?["type"]?.GetValue<string>() == "page");
                var socket = page?["webSocketDebuggerUrl"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(socket)) return socket;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
            }
            await Task.Delay(250, ct);
        }
        throw new IOException("Браузер не открыл отладочный порт");
    }

    public static string? FindBrowser()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            "/usr/bin/google-chrome",
            "/usr/bin/chromium",
            "/usr/bin/microsoft-edge",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void KillPrevious()
    {
        if (!File.Exists(_pidFile) || !int.TryParse(File.ReadAllText(_pidFile), out var pid)) return;
        try
        {
            using var existing = Process.GetProcessById(pid);
            if (existing.ProcessName is "chrome" or "msedge" or "chromium" && !existing.HasExited)
                existing.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
    }

    private void Disconnect()
    {
        _session?.Dispose();
        _session = null;
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Browser process had already exited");
        }
        _process = null;
    }

    public void Dispose() => Disconnect();

    private sealed record DocumentHit(string RequestId, int Status, string Url);

    private sealed class CdpSession : IDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly CancellationTokenSource _done = new();
        private readonly ConcurrentPending _pending = new();
        private readonly System.Threading.Channels.Channel<(string Method, JsonNode Params)> _events =
            System.Threading.Channels.Channel.CreateUnbounded<(string, JsonNode)>();
        private int _nextId;
        private readonly Task _readLoop;

        public bool IsOpen => _socket.State == WebSocketState.Open;

        private CdpSession(ClientWebSocket socket)
        {
            _socket = socket;
            _readLoop = Task.Run(ReadLoop);
        }

        public static async Task<CdpSession> ConnectAsync(string url, CancellationToken ct)
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(url), ct);
            return new CdpSession(socket);
        }

        public void Drain()
        {
            while (_events.Reader.TryRead(out _)) { }
        }

        public async Task<(string Method, JsonNode Params)?> NextEventAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (timeout <= TimeSpan.Zero) return null;
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, _done.Token);
            wait.CancelAfter(timeout);
            try
            {
                return await _events.Reader.ReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        public async Task<JsonNode> CommandAsync(string method, object args, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _nextId);
            var done = _pending.Create(id);
            var payload = JsonSerializer.Serialize(new { id, method, @params = args }, Json);
            var bytes = Encoding.UTF8.GetBytes(payload);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(60));
            var message = await done.Task.WaitAsync(wait.Token);
            if (message["error"]?["message"] is JsonNode error)
                throw new InvalidOperationException(error.GetValue<string>());
            return message["result"] ?? new JsonObject();
        }

        private async Task ReadLoop()
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (!_done.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, _done.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var node = JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                    if (node is null) continue;
                    if (node["id"] is JsonNode idNode && idNode.GetValue<int>() is var id && _pending.TryTake(id, out var pending))
                        pending.TrySetResult(node);
                    else if (node["method"]?.GetValue<string>() is { } method)
                        _events.Writer.TryWrite((method, node["params"] ?? new JsonObject()));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
            {
            }
            finally
            {
                _pending.CancelAll();
            }
        }

        public void Dispose()
        {
            _done.Cancel();
            try { _socket.Abort(); } catch (WebSocketException) { }
            _socket.Dispose();
            _done.Dispose();
        }
    }

    private sealed class ConcurrentPending
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> _items = new();

        public TaskCompletionSource<JsonNode> Create(int id)
        {
            var source = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            _items[id] = source;
            return source;
        }

        public bool TryTake(int id, out TaskCompletionSource<JsonNode> source) => _items.TryRemove(id, out source!);

        public void CancelAll()
        {
            foreach (var item in _items) item.Value.TrySetCanceled();
            _items.Clear();
        }
    }
}
