using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class OutboundCheck
{
    public async Task<HealthCheckResult> CheckAsync(int? proxyPort = null)
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();

        try
        {
            var port = proxyPort ?? AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (port <= 0)
            {
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Error,
                    "No local proxy port available", sw.Elapsed, details);
            }

            details["proxy_port"] = port;

            var tcpOk = await TestSocks5ConnectAsync(Global.Loopback, port, "1.1.1.1", 443);
            details["tcp_1.1.1.1:443"] = tcpOk ? "OK" : "FAIL";

            if (!tcpOk)
            {
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Fail,
                    "TCP connection through proxy failed - node may be down", sw.Elapsed, details);
            }

            var tlsResult = await TestTlsHandshakeViaSocksAsync(Global.Loopback, port, "google.com", 443);
            details["tls_google.com:443"] = tlsResult.Ok ? "OK" : $"FAIL: {tlsResult.Error}";

            if (!tlsResult.Ok)
            {
                var diagnosis = DiagnoseTlsFailure(tlsResult.Error);
                details["tls_diagnosis"] = diagnosis;
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Warning,
                    $"TLS handshake failed - {diagnosis}", sw.Elapsed, details);
            }

            var httpOk = await TestHttpGenerate204ViaSocksAsync(port);
            details["http_generate_204"] = httpOk ? "OK" : "FAIL";

            if (!httpOk)
            {
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Warning,
                    "HTTP 204 check failed - outbound may be reset or rate limited", sw.Elapsed, details);
            }

            sw.Stop();
            return new HealthCheckResult("Outbound", HealthCheckStatus.Pass,
                "All outbound checks passed", sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("Outbound", HealthCheckStatus.Error,
                $"Outbound check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static string DiagnoseTlsFailure(string error)
    {
        if (error.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return "SNI/Certificate error - check Reality PublicKey or SNI config";
        if (error.Contains("remote", StringComparison.OrdinalIgnoreCase))
            return "Remote host rejected connection - node may be blocked by CDN";
        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "TLS handshake timeout - network or node unreachable";
        return $"TLS error: {error}";
    }

    /// <summary>
    /// SOCKS5 CONNECT tunnel (no-auth). Owns the underlying TcpClient.
    /// </summary>
    internal sealed class Socks5Tunnel : IAsyncDisposable, IDisposable
    {
        private readonly TcpClient _client;
        public NetworkStream Stream { get; }

        private Socks5Tunnel(TcpClient client, NetworkStream stream)
        {
            _client = client;
            Stream = stream;
        }

        public static async Task<Socks5Tunnel?> OpenAsync(
            string proxyHost, int proxyPort, string targetHost, int targetPort,
            CancellationToken ct = default)
        {
            var client = new TcpClient();
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(proxyHost, proxyPort, connectCts.Token).ConfigureAwait(false);
                var stream = client.GetStream();

                // greeting: VER=5, NMETHODS=1, METHOD=0x00 (no auth)
                await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, connectCts.Token).ConfigureAwait(false);
                var methodResp = new byte[2];
                if (await ReadExactAsync(stream, methodResp, connectCts.Token).ConfigureAwait(false) != 2
                    || methodResp[0] != 0x05 || methodResp[1] != 0x00)
                {
                    client.Dispose();
                    return null;
                }

                var hostBytes = Encoding.ASCII.GetBytes(targetHost);
                if (hostBytes.Length is 0 or > 255)
                {
                    client.Dispose();
                    return null;
                }

                // CONNECT: VER CMD RSV ATYP(DOMAIN) LEN HOST PORT
                var req = new byte[4 + 1 + hostBytes.Length + 2];
                req[0] = 0x05;
                req[1] = 0x01;
                req[2] = 0x00;
                req[3] = 0x03;
                req[4] = (byte)hostBytes.Length;
                Buffer.BlockCopy(hostBytes, 0, req, 5, hostBytes.Length);
                req[5 + hostBytes.Length] = (byte)(targetPort >> 8);
                req[6 + hostBytes.Length] = (byte)(targetPort & 0xFF);
                await stream.WriteAsync(req, connectCts.Token).ConfigureAwait(false);

                var header = new byte[4];
                if (await ReadExactAsync(stream, header, connectCts.Token).ConfigureAwait(false) != 4
                    || header[0] != 0x05 || header[1] != 0x00)
                {
                    client.Dispose();
                    return null;
                }

                var atyp = header[3];
                int addrLen;
                if (atyp == 0x01)
                {
                    addrLen = 4;
                }
                else if (atyp == 0x04)
                {
                    addrLen = 16;
                }
                else if (atyp == 0x03)
                {
                    var lenBuf = new byte[1];
                    if (await ReadExactAsync(stream, lenBuf, connectCts.Token).ConfigureAwait(false) != 1)
                    {
                        client.Dispose();
                        return null;
                    }
                    addrLen = lenBuf[0];
                }
                else
                {
                    client.Dispose();
                    return null;
                }

                var skip = new byte[addrLen + 2];
                if (await ReadExactAsync(stream, skip, connectCts.Token).ConfigureAwait(false) != skip.Length)
                {
                    client.Dispose();
                    return null;
                }

                return new Socks5Tunnel(client, stream);
            }
            catch
            {
                client.Dispose();
                return null;
            }
        }

        private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
                if (n <= 0)
                {
                    return offset;
                }
                offset += n;
            }
            return offset;
        }

        public void Dispose()
        {
            try { Stream.Dispose(); } catch { }
            try { _client.Dispose(); } catch { }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<bool> TestSocks5ConnectAsync(string proxyHost, int proxyPort, string targetHost, int targetPort)
    {
        try
        {
            await using var tunnel = await Socks5Tunnel.OpenAsync(proxyHost, proxyPort, targetHost, targetPort);
            return tunnel != null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool Ok, string Error)> TestTlsHandshakeViaSocksAsync(
        string proxyHost, int proxyPort, string host, int port)
    {
        try
        {
            await using var tunnel = await Socks5Tunnel.OpenAsync(proxyHost, proxyPort, host, port);
            if (tunnel == null)
            {
                return (false, "SOCKS5 CONNECT failed");
            }

            using var sslStream = new SslStream(tunnel.Stream, leaveInnerStreamOpen: true, (_, _, _, _) => true);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                    | System.Security.Authentication.SslProtocols.Tls13
            });

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<bool> TestHttpGenerate204ViaSocksAsync(int socksPort)
    {
        try
        {
            var urls = new[]
            {
                "https://www.google.com/generate_204",
                "https://www.gstatic.com/generate_204",
                "https://cp.cloudflare.com/generate_204"
            };

            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{socksPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AllowAutoRedirect = false,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            foreach (var url in urls)
            {
                try
                {
                    var response = await http.GetAsync(url);
                    if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
