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

            var tcpOk = await TestTcpConnectAsync(Global.Loopback, port, "1.1.1.1", 443);
            details["tcp_1.1.1.1:443"] = tcpOk ? "OK" : "FAIL";

            if (!tcpOk)
            {
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Fail,
                    "TCP connection through proxy failed — node may be down", sw.Elapsed, details);
            }

            var tlsResult = await TestTlsHandshakeAsync("google.com", 443);
            details["tls_google.com:443"] = tlsResult.Ok ? "OK" : $"FAIL: {tlsResult.Error}";

            if (!tlsResult.Ok)
            {
                var diagnosis = DiagnoseTlsFailure(tlsResult.Error);
                details["tls_diagnosis"] = diagnosis;
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Warning,
                    $"TLS handshake failed — {diagnosis}", sw.Elapsed, details);
            }

            var httpOk = await TestHttpGenerate204Async();
            details["http_generate_204"] = httpOk ? "OK" : "FAIL";

            if (!httpOk)
            {
                sw.Stop();
                return new HealthCheckResult("Outbound", HealthCheckStatus.Warning,
                    "HTTP 204 check failed —出口 may be reset or rate limited", sw.Elapsed, details);
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
            return "SNI/Certificate error — check Reality PublicKey or SNI config";
        if (error.Contains("remote", StringComparison.OrdinalIgnoreCase))
            return "Remote host rejected connection — node may be blocked by CDN";
        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "TLS handshake timeout — network or node unreachable";
        return $"TLS error: {error}";
    }

    private static async Task<bool> TestTcpConnectAsync(string proxyHost, int proxyPort, string targetHost, int targetPort)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(proxyHost, proxyPort);
            var stream = client.GetStream();

            var connectRequest = $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\n\r\n";
            var requestBytes = Encoding.ASCII.GetBytes(connectRequest);
            await stream.WriteAsync(requestBytes);

            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            return response.Contains("200");
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool Ok, string Error)> TestTlsHandshakeAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);

            using var sslStream = new SslStream(
                client.GetStream(),
                false,
                (sender, cert, chain, errors) => true);

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            });

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<bool> TestHttpGenerate204Async()
    {
        try
        {
            var urls = new[]
            {
                "https://www.google.com/generate_204",
                "https://www.gstatic.com/generate_204",
                "https://cp.cloudflare.com/generate_204"
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            foreach (var url in urls)
            {
                try
                {
                    var response = await http.GetAsync(url);
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
