using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ItchDB.Infrastructure.Http;

/// <summary>
/// Opens TCP connections trying IPv4 addresses first, each with a short timeout. The default connect goes through
/// the addresses in DNS order, and on networks with broken IPv6 every new game subdomain hangs until the connect timeout.
/// </summary>
internal static class HostConnector
{
    private static readonly TimeSpan PerAddressTimeout = TimeSpan.FromSeconds(7);

    public static ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct) =>
        ConnectAsync(context, proxy: null, ct);

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, string? proxy, CancellationToken ct)
    {
        if (!LocalProxy.TryParse(proxy, out var via, out var error))
            throw new HttpRequestException(error);
        var endpoint = context.DnsEndPoint;
        if (via is null) return await ConnectDirectAsync(endpoint, ct);

        var tunnel = await ConnectDirectAsync(new DnsEndPoint(via.Host, via.Port), ct);
        try
        {
            if (via.Scheme == "socks5") await TunnelSocks5Async(tunnel, endpoint.Host, endpoint.Port, via, ct);
            else await TunnelHttpAsync(tunnel, endpoint.Host, endpoint.Port, via, ct);
            return tunnel;
        }
        catch
        {
            await tunnel.DisposeAsync();
            throw;
        }
    }

    private static async Task<Stream> ConnectDirectAsync(DnsEndPoint endpoint, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
        Exception? last = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PerAddressTimeout);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), timeout.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex is OperationCanceledException ? new TimeoutException($"{address}: нет ответа за {PerAddressTimeout.TotalSeconds:0} с") : ex;
            }
        }
        throw new HttpRequestException($"Не удалось подключиться к {endpoint.Host}: {last?.Message ?? "нет адресов"}", last);
    }

    private static async Task TunnelHttpAsync(Stream stream, string host, int port, Uri proxy, CancellationToken ct)
    {
        var target = $"{host}:{port}";
        var request = new StringBuilder()
            .Append("CONNECT ").Append(target).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(target).Append("\r\n");
        if (!string.IsNullOrEmpty(proxy.UserInfo))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(proxy.UserInfo)));
            request.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }
        request.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), ct);

        var header = await ReadUntilAsync(stream, "\r\n\r\n"u8.ToArray(), ct);
        var text = Encoding.ASCII.GetString(header);
        var line = text.Split("\r\n", 2)[0];
        if (line.Length < 12 || line[9] != '2')
            throw new HttpRequestException($"Прокси отклонил соединение: {line}");
    }

    private static async Task TunnelSocks5Async(Stream stream, string host, int port, Uri proxy, CancellationToken ct)
    {
        var user = "";
        var pass = "";
        if (!string.IsNullOrEmpty(proxy.UserInfo))
        {
            var info = Uri.UnescapeDataString(proxy.UserInfo).Split(':', 2);
            user = info[0];
            pass = info.Length > 1 ? info[1] : "";
        }

        var greeting = string.IsNullOrEmpty(user) ? new byte[] { 5, 1, 0 } : new byte[] { 5, 1, 2 };
        await stream.WriteAsync(greeting, ct);
        var hello = await ReadExactAsync(stream, 2, ct);
        if (hello[0] != 5 || hello[1] == 0xFF)
            throw new HttpRequestException("SOCKS-прокси не принял способ входа");
        if (hello[1] == 2)
        {
            var userBytes = Encoding.UTF8.GetBytes(user);
            var passBytes = Encoding.UTF8.GetBytes(pass);
            var auth = new byte[3 + userBytes.Length + passBytes.Length];
            auth[0] = 1;
            auth[1] = (byte)userBytes.Length;
            userBytes.CopyTo(auth, 2);
            auth[2 + userBytes.Length] = (byte)passBytes.Length;
            passBytes.CopyTo(auth, 3 + userBytes.Length);
            await stream.WriteAsync(auth, ct);
            var authReply = await ReadExactAsync(stream, 2, ct);
            if (authReply[1] != 0) throw new HttpRequestException("SOCKS-прокси отклонил логин или пароль");
        }

        var hostBytes = Encoding.ASCII.GetBytes(host);
        if (hostBytes.Length is 0 or > 255) throw new HttpRequestException($"Прокси не принимает имя {host}");
        var req = new byte[7 + hostBytes.Length];
        req[0] = 5;
        req[1] = 1;
        req[2] = 0;
        req[3] = 3;
        req[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(req, 5);
        req[^2] = (byte)(port >> 8);
        req[^1] = (byte)port;
        await stream.WriteAsync(req, ct);

        var head = await ReadExactAsync(stream, 4, ct);
        if (head[1] != 0)
            throw new HttpRequestException($"SOCKS-прокси отклонил соединение, код {head[1]}");
        var extra = head[3] switch
        {
            1 => 4 + 2,
            4 => 16 + 2,
            3 => (await ReadExactAsync(stream, 1, ct))[0] + 2,
            _ => throw new HttpRequestException("Неизвестный адрес в ответе SOCKS-прокси"),
        };
        await ReadExactAsync(stream, extra, ct);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n == 0) throw new HttpRequestException("Прокси закрыл соединение");
            read += n;
        }
        return buffer;
    }

    private static async Task<byte[]> ReadUntilAsync(Stream stream, byte[] marker, CancellationToken ct)
    {
        var buffer = new List<byte>();
        while (buffer.Count < 8192)
        {
            buffer.Add((await ReadExactAsync(stream, 1, ct))[0]);
            if (buffer.Count < marker.Length) continue;
            var matched = true;
            for (var i = 0; i < marker.Length; i++)
            {
                if (buffer[buffer.Count - marker.Length + i] != marker[i])
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return buffer.ToArray();
        }
        throw new HttpRequestException("Прокси не закончил ответ");
    }

    /// <summary>
    /// A refused connection means the host resolves to an address that does not serve itch.io
    /// (typically a provider's block page), so retrying only wastes time.
    /// </summary>
    public static bool IsRefused(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) return true;
        return false;
    }

    /// <summary>The host never answered. Retrying it stalls every other request that is waiting on the same client.</summary>
    public static bool IsConnectFailure(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is TimeoutException or SocketException) return true;
        return false;
    }
}
