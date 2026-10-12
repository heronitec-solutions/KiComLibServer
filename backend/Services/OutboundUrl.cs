using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Rejects import URLs that point at the server itself, a private network, or link-local metadata.
    /// Hostnames are resolved before the request, and again for every redirect.
    /// </summary>
    public static class OutboundUrl
    {
        public const string PrivateAddressError = "The URL points at a private or local address";

        public static string? SyntaxError(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "url is required";
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return "Only http and https URLs can be imported";
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return "The URL must not contain credentials";
            }

            if (string.IsNullOrWhiteSpace(uri.IdnHost) || uri.IsLoopback)
            {
                return PrivateAddressError;
            }

            string host = uri.IdnHost.Trim().TrimEnd('.');
            if (IsBlockedName(host))
            {
                return PrivateAddressError;
            }

            if (IPAddress.TryParse(host, out IPAddress? literal) && IsBlockedAddress(literal))
            {
                return PrivateAddressError;
            }

            return null;
        }

        public static async Task<string?> ScreenAsync(string url, CancellationToken cancellationToken)
        {
            string? syntax = SyntaxError(url);
            if (syntax != null)
            {
                return syntax;
            }

            Uri uri = new(url.Trim());
            string host = uri.IdnHost.Trim().TrimEnd('.');
            if (IPAddress.TryParse(host, out _))
            {
                return null;
            }

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            }
            catch (Exception)
            {
                return "The URL host could not be resolved";
            }

            if (addresses.Length == 0 || addresses.Any(IsBlockedAddress))
            {
                return PrivateAddressError;
            }

            return null;
        }

        public static async Task<HttpResponseMessage> GetAsync(
            HttpClient client,
            string url,
            Action<HttpRequestMessage> configure,
            CancellationToken cancellationToken,
            int maxHops = 4)
        {
            string current = url.Trim();
            for (int hop = 0; ; hop++)
            {
                string? error = await ScreenAsync(current, cancellationToken);
                if (error != null)
                {
                    throw new OutboundUrlException(error);
                }

                using HttpRequestMessage request = new(HttpMethod.Get, current);
                configure(request);
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (HttpRequestException exception) when (IsPrivateAddressFailure(exception))
                {
                    throw new OutboundUrlException(PrivateAddressError);
                }
                int status = (int)response.StatusCode;
                bool redirect = status is 301 or 302 or 303 or 307 or 308;
                if (!redirect || hop >= maxHops)
                {
                    return response;
                }

                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location == null)
                {
                    throw new OutboundUrlException("The URL redirected without a destination");
                }

                current = location.IsAbsoluteUri
                    ? location.AbsoluteUri
                    : new Uri(new Uri(current), location).AbsoluteUri;
            }
        }

        public static bool IsBlockedName(string host)
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
                || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase)
                || host.Equals("metadata.google.com", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool IsBlockedAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                {
                    return true;
                }

                byte[] bytes = address.GetAddressBytes();
                // Unique local fc00::/7
                return (bytes[0] & 0xFE) == 0xFC;
            }

            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                return true;
            }

            byte[] v4 = address.GetAddressBytes();
            if (v4[0] == 0 || v4[0] == 10 || v4[0] == 127)
            {
                return true;
            }

            if (v4[0] == 100 && v4[1] >= 64 && v4[1] <= 127)
            {
                return true;
            }

            if (v4[0] == 169 && v4[1] == 254)
            {
                return true;
            }

            if (v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31)
            {
                return true;
            }

            if (v4[0] == 192 && v4[1] == 168)
            {
                return true;
            }

            if (v4[0] == 192 && v4[1] == 0 && v4[2] == 0)
            {
                return true;
            }

            if (v4[0] == 192 && v4[1] == 0 && v4[2] == 2)
            {
                return true;
            }

            if (v4[0] == 198 && v4[1] == 51 && v4[2] == 100)
            {
                return true;
            }

            if (v4[0] == 203 && v4[1] == 0 && v4[2] == 113)
            {
                return true;
            }

            if (v4[0] >= 224)
            {
                return true;
            }

            return false;
        }

        private static bool IsPrivateAddressFailure(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current.Message == PrivateAddressError)
                {
                    return true;
                }
            }

            return false;
        }

        public static string? RejectResolved(string host, IReadOnlyList<IPAddress> addresses)
        {
            if (IsBlockedName(host) || addresses.Count == 0 || addresses.Any(IsBlockedAddress))
            {
                return PrivateAddressError;
            }

            return null;
        }

        public static SocketsHttpHandler CreatePinnedHandler(DecompressionMethods decompression)
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = decompression,
                ConnectCallback = PinnedConnectAsync
            };
        }

        private static async ValueTask<Stream> PinnedConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            string host = context.DnsEndPoint.Host;
            int port = context.DnsEndPoint.Port;
            IPAddress[] addresses;
            if (IPAddress.TryParse(host, out IPAddress? literal))
            {
                addresses = new[] { literal };
            }
            else
            {
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                }
                catch (Exception exception)
                {
                    throw new HttpRequestException("The URL host could not be resolved", exception);
                }
            }

            string? rejected = RejectResolved(host, addresses);
            if (rejected != null)
            {
                throw new HttpRequestException(rejected);
            }

            Exception? last = null;
            foreach (IPAddress address in addresses)
            {
                Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    last = exception;
                    socket.Dispose();
                }
            }

            throw new HttpRequestException("The URL host could not be connected", last);
        }
    }

    public sealed class OutboundUrlException : Exception
    {
        public OutboundUrlException(string message)
            : base(message)
        {
        }
    }
}
