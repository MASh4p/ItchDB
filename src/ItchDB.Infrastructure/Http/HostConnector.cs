using System.Net;
using System.Net.Sockets;

namespace ItchDB.Infrastructure.Http;

/// <summary>
/// Opens TCP connections trying IPv4 addresses first, each with a short timeout. The default connect goes through
/// the addresses in DNS order, and on networks with broken IPv6 every new game subdomain hangs until the connect timeout.
/// </summary>
internal static class HostConnector
{
    private static readonly TimeSpan PerAddressTimeout = TimeSpan.FromSeconds(7);

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
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
