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

    public const string BlockNotice =
        "itch.io перестал отдавать страницы (проверка или код 400). Пройдите проверку в окне Chrome: сбор подождёт и повторит запрос, а не будет пропускать следующие.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SolveTimeout = TimeSpan.FromMinutes(3);

    private const int TabCount = 4;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _fetchSlots = new(TabCount, TabCount);
    private readonly System.Threading.Channels.Channel<CdpSession> _navTabs =
        System.Threading.Channels.Channel.CreateUnbounded<CdpSession>();
    private readonly string _profile = Path.Combine(paths.Root, "browser-profile");
    private readonly string _pidFile = Path.Combine(paths.Root, "browser.pid");
    private Process? _process;
    private CdpSession? _session;
    private int _navCount;
    private string? _browserName;
    private bool _announced;

    public string? BrowserName => _browserName;

    /// <summary>Brings the crawl browser forward and opens itch.io, where a check can be solved.</summary>
    public async Task<bool> OpenForUserAsync(CancellationToken ct)
    {
        if (!await EnsureAsync(ct)) return false;
        await _gate.WaitAsync(ct);
        try
        {
            await _session!.CommandAsync("Page.bringToFront", new { }, ct);
            await LoadAsync(_session, "https://itch.io/", ct);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserPage?> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            if (!await EnsureAsync(ct)) return null;
            BrowserPage? page = null;
            if (IsDataUrl(url) && _navCount > 0)
            {
                await _fetchSlots.WaitAsync(ct);
                try { page = await FetchDataAsync(url, ct); }
                finally { _fetchSlots.Release(); }
                if (page is not null && !ItchHttpClient.IsBlocked(page.Status, page.Body)) return page;
            }

            var ownTab = _navCount > 0;
            var tab = ownTab ? await RentTabAsync(ct) : _session!;
            if (!ownTab) await _gate.WaitAsync(ct);
            try
            {
                if (page is null && IsDataUrl(url)) page = await FetchDataAsync(url, ct);
                // A fetch that already came back 400 must not be opened in the window: Chrome then
                // shows a white page with nothing to click, and itch.io stays unusable.
                var fetchedBlock = page is not null && page.Status is 400 or 429;
                if (!fetchedBlock && (page is null || ItchHttpClient.IsBlocked(page.Status, page.Body)))
                    page = await LoadAsync(tab, url, ct);
                var loaded = page ?? throw new IOException("Страница не загрузилась");
                if (ItchHttpClient.IsChallengeBody(loaded.Body))
                {
                    status.SetNotice(SolveNotice);
                    log.LogWarning("Cloudflare check in the Chrome window for {Url}. Waiting up to {Minutes} min for it to be solved", url, SolveTimeout.TotalMinutes);
                    var solved = await WaitForSolutionAsync(tab, ct);
                    status.SetNotice(null);
                    if (solved is not null) page = solved;
                }
                else if (page.Status is 400 or 429)
                {
                    status.SetNotice(BlockNotice);
                    if (!fetchedBlock) await LeaveErrorPageAsync(tab, ct);
                }
                return page;
            }
            finally
            {
                if (ownTab) ReturnTab(tab);
                else _gate.Release();
            }
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
    }

    private async Task<CdpSession> RentTabAsync(CancellationToken ct) => await _navTabs.Reader.ReadAsync(ct);

    private void ReturnTab(CdpSession tab) => _navTabs.Writer.TryWrite(tab);

    private async Task<bool> EnsureAsync(CancellationToken ct)
    {
        if (_session is { IsOpen: true }) return true;
        await _gate.WaitAsync(ct);
        try
        {
            if (_session is { IsOpen: true }) return true;
            return await StartBrowserAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> StartBrowserAsync(CancellationToken ct)
    {
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
        _session = await ConnectTabAsync(ws, ct);
        _navCount = await OpenNavTabsAsync(port, ct);
        if (!_announced)
        {
            _announced = true;
            log.LogInformation("itch.io requests go through {Browser}. A check, if it appears, is solved in that window", _browserName);
        }
        return true;
    }

    private async Task<int> OpenNavTabsAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var opened = 0;
        for (var i = 1; i < TabCount; i++)
        {
            try
            {
                using var response = await http.PutAsync($"http://127.0.0.1:{port}/json/new?about:blank", null, ct);
                if (!response.IsSuccessStatusCode) break;
                var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
                var socket = node?["webSocketDebuggerUrl"]?.GetValue<string>();
                if (string.IsNullOrEmpty(socket)) break;
                ReturnTab(await ConnectTabAsync(socket, ct));
                opened++;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                log.LogDebug(ex, "Extra browser tab was not created");
                break;
            }
        }
        return opened;
    }

    private static async Task<CdpSession> ConnectTabAsync(string webSocketUrl, CancellationToken ct)
    {
        var session = await CdpSession.ConnectAsync(webSocketUrl, ct);
        await session.CommandAsync("Network.enable", new { }, ct);
        await session.CommandAsync("Page.enable", new { }, ct);
        return session;
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

    /// <summary>
    /// Catalog JSON must be requested as a fetch, not a page navigation: a navigation asks for HTML
    /// and itch.io then ignores format=json, which the parser reports as "'&lt;' is an invalid start".
    /// </summary>
    private async Task<BrowserPage?> FetchDataAsync(string url, CancellationToken ct)
    {
        if (!IsDataUrl(url)) return null;
        var href = await LocationAsync(_session!, ct);
        if (href is null || !href.StartsWith("https://itch.io/", StringComparison.OrdinalIgnoreCase))
        {
            var home = await LoadAsync(_session!, "https://itch.io/", ct);
            if (ItchHttpClient.IsChallengeBody(home.Body)) return home;
        }

        var expression = "(async () => { try { const res = await fetch(" + JsonSerializer.Serialize(url)
            + ", { credentials: 'include', redirect: 'follow', headers: { 'Accept': 'application/json, text/javascript, */*;q=0.1', 'X-Requested-With': 'XMLHttpRequest' } });"
            + " return { status: res.status, url: res.url, body: await res.text() }; }"
            + " catch (e) { return { status: 0, url: " + JsonSerializer.Serialize(url) + ", body: String(e) }; } })()";
        var result = await _session!.CommandAsync("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true }, ct);
        if (result["exceptionDetails"] is not null) return null;
        var value = result["result"]?["value"];
        var status = value?["status"]?.GetValue<int>() ?? 0;
        var body = value?["body"]?.GetValue<string>() ?? "";
        var finalUrl = value?["url"]?.GetValue<string>() ?? url;
        if (status == 0) return null;
        return new BrowserPage(status, body, finalUrl);
    }

    private static bool IsDataUrl(string url) =>
        url.Contains("format=json", StringComparison.OrdinalIgnoreCase)
        || url.Contains(".json", StringComparison.OrdinalIgnoreCase)
        || url.Contains(".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A bare HTTP 400 is a white page with nothing to click. Leave it for the itch.io home,
    /// where a check — if itch.io is showing one — can actually be solved.
    /// </summary>
    private async Task LeaveErrorPageAsync(CdpSession session, CancellationToken ct)
    {
        try
        {
            await session.CommandAsync("Page.navigate", new JsonObject { ["url"] = "https://itch.io/" }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogDebug(ex, "Could not leave the HTTP 400 page");
        }
    }

    private async Task<BrowserPage> LoadAsync(CdpSession session, string url, CancellationToken ct)
    {
        session.Drain();
        var args = new JsonObject { ["url"] = url };
        if (!url.Contains("://itch.io/", StringComparison.OrdinalIgnoreCase))
            args["referrer"] = "https://itch.io/";
        var nav = await session.CommandAsync("Page.navigate", args, ct);
        if (nav["errorText"] is JsonNode error)
            throw new IOException(error.GetValue<string>());

        var frameId = nav["frameId"]?.GetValue<string>();
        var hit = await ReadDocumentAsync(session, PageTimeout, ct, frameId) ?? throw new IOException("Страница не загрузилась");
        var body = await BodyAsync(session, hit.RequestId, ct);
        var finalUrl = await LocationAsync(session, ct) ?? hit.Url;
        return new BrowserPage(hit.Status, body, finalUrl);
    }

    private async Task<BrowserPage?> WaitForSolutionAsync(CdpSession session, CancellationToken ct)
    {
        var frameId = await MainFrameIdAsync(session, ct);
        var deadline = DateTime.UtcNow + SolveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var wait = deadline - DateTime.UtcNow;
            var hit = await ReadDocumentAsync(session, wait, ct, frameId, requireLoad: true);
            if (hit is null) return null;
            var body = await BodyAsync(session, hit.RequestId, ct);
            if (!ItchHttpClient.IsChallengeBody(body))
            {
                var finalUrl = await LocationAsync(session, ct) ?? hit.Url;
                log.LogInformation("Cloudflare check solved in the browser");
                return new BrowserPage(hit.Status, body, finalUrl);
            }
        }
        return null;
    }

    private async Task<DocumentHit?> ReadDocumentAsync(CdpSession session, TimeSpan timeout, CancellationToken ct, string? frameId, bool requireLoad = false)
    {
        var deadline = DateTime.UtcNow + timeout;
        DocumentHit? hit = null;
        var loaded = false;
        while (DateTime.UtcNow < deadline)
        {
            var ev = await session.NextEventAsync(deadline - DateTime.UtcNow, ct);
            if (ev is null) break;
            if (ev.Value.Method == "Network.responseReceived" && DocumentOf(ev.Value.Params, frameId) is { } doc)
                hit = doc;
            else if (ev.Value.Method == "Page.loadEventFired")
                loaded = true;
            if (hit is not null && loaded && hit.Status is < 300 or >= 400) return hit;
        }
        return requireLoad ? null : hit;
    }

    private static async Task<string?> MainFrameIdAsync(CdpSession session, CancellationToken ct)
    {
        var tree = await session.CommandAsync("Page.getFrameTree", new { }, ct);
        return tree["frameTree"]?["frame"]?["id"]?.GetValue<string>();
    }

    /// <summary>
    /// The main frame only. A game description often embeds another title via https://itch.io/embed/…,
    /// and that widget is its own document: it has a title, but no game id. Taking it as the page
    /// makes the parser report "id not found" for a page that loaded fine.
    /// </summary>
    private static DocumentHit? DocumentOf(JsonNode p, string? frameId)
    {
        if (frameId is not null && p["frameId"]?.GetValue<string>() is { } eventFrame && eventFrame != frameId)
            return null;
        if (p["type"]?.GetValue<string>() != "Document") return null;
        var response = p["response"];
        var status = response?["status"]?.GetValue<int>() ?? 0;
        if (status is >= 300 and < 400) return null;
        var requestId = p["requestId"]?.GetValue<string>();
        var url = response?["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(url)) return null;
        if (!url.Contains("://itch.io/", StringComparison.OrdinalIgnoreCase) && !url.Contains(".itch.io/", StringComparison.OrdinalIgnoreCase))
            return null;
        if (IsEmbedDocument(url)) return null;
        return new DocumentHit(requestId, status, url);
    }

    private static bool IsEmbedDocument(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.AbsolutePath.Equals("/embed", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> BodyAsync(CdpSession session, string requestId, CancellationToken ct)
    {
        try
        {
            var result = await session.CommandAsync("Network.getResponseBody", new { requestId }, ct);
            var body = result["body"]?.GetValue<string>() ?? "";
            if (result["base64Encoded"]?.GetValue<bool>() == true)
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            if (body.Length > 0) return body;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            log.LogDebug(ex, "Response body was not available from the network log");
        }
        return await EvaluateAsync(session, "document.documentElement ? document.documentElement.outerHTML : ''", ct) ?? "";
    }

    private async Task<string?> LocationAsync(CdpSession session, CancellationToken ct) =>
        await EvaluateAsync(session, "location.href", ct);

    private async Task<string?> EvaluateAsync(CdpSession session, string expression, CancellationToken ct)
    {
        var result = await session.CommandAsync("Runtime.evaluate", new { expression, returnByValue = true }, ct);
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
        _navCount = 0;
        while (_navTabs.Reader.TryRead(out var tab)) tab.Dispose();
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

    /// <summary>
    /// Closes the crawl window and deletes its profile. The next request starts a clean browser,
    /// without the cookies itch.io answered with HTTP 400.
    /// </summary>
    public async Task ResetSessionAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Disconnect();
            KillPrevious();
            if (!Directory.Exists(_profile)) return;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(_profile, recursive: true);
                    log.LogInformation("Crawl browser profile deleted");
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 8)
                {
                    await Task.Delay(250, ct);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => Disconnect();

    private sealed record DocumentHit(string RequestId, int Status, string Url);

    private sealed class CdpSession : IDisposable
    {
        private readonly SemaphoreSlim _send = new(1, 1);
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
            await _send.WaitAsync(ct);
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _send.Release();
            }
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
