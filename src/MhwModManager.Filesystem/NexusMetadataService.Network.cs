using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed partial class NexusMetadataService
{
    private static HttpClient CreatePreviewHttpClient()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ConfigureNetworkClient(CreatePreviewHandler());
    }

    internal static SocketsHttpHandler CreatePreviewHandler()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectPublicPreviewAsync
        };
    }

    private static HttpClient CreateNexusHttpClient()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ConfigureNetworkClient(CreateNexusHandler());
    }
    internal static HttpClientHandler CreateNexusHandler()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
    }

    private static HttpClient ConfigureNetworkClient(HttpMessageHandler handler)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Universal-Mod-Manager/8.8.0");
        return client;
    }

    private static async ValueTask<Stream> ConnectPublicPreviewAsync(
        SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var endpoint = context.DnsEndPoint;
        IPAddress[] addresses;
        if (IPAddress.TryParse(endpoint.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
        }

        if (addresses.Length == 0)
            throw new HttpRequestException("Remote preview destination did not resolve.");

        foreach (var address in addresses)
        {
            if (!IsPublicNetworkAddress(address))
                throw new HttpRequestException("Remote preview destination resolved to a non-public network address.");
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw new HttpRequestException("Unable to connect to an allowed remote preview destination.", lastError);
    }

    internal static bool IsPublicNetworkAddress(IPAddress address)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return false;

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIpv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicIpv6(address),
            _ => false
        };
    }
    private static bool IsPublicIpv4(byte[] bytes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (bytes[0] is 0 or 10 or 127 || bytes[0] >= 224) return false;
        if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return false;
        if (bytes[0] == 169 && bytes[1] == 254) return false;
        if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return false;
        if (bytes[0] == 192 && bytes[1] == 168) return false;
        if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) return false;
        if (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) return false;
        if (bytes[0] == 198 && bytes[1] is 18 or 19) return false;
        if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return false;
        if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return false;
        return true;
    }

    private static bool IsPublicIpv6(IPAddress address)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast) return false;
        var bytes = address.GetAddressBytes();
        if ((bytes[0] & 0xfe) == 0xfc) return false;
        if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0xc0) return false;
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return false;
        var firstTwelveAreZero = true;
        for (var i = 0; i < 12; i++)
        {
            if (bytes[i] != 0)
            {
                firstTwelveAreZero = false;
                break;
            }
        }
        return !firstTwelveAreZero;
    }
}
