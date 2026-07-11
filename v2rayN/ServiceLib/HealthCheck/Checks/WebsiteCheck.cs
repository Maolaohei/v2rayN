using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class WebsiteCheck
{
    private static readonly (string Url, string ExpectedTitle)[] TestSites =
    [
        ("https://www.google.com", "Google"),
        ("https://github.com", "GitHub"),
        ("https://www.cloudflare.com", "Cloudflare")
    ];

    public async Task<HealthCheckResult> CheckAsync(int? proxyPort = null, bool tunEnabled = true)
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();
        var passCount = 0;

        try
        {
            var port = proxyPort ?? AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            details["proxy_port"] = port;
            details["probe_mode"] = "socks5";

            if (tunEnabled)
            {
                var tunVerified = await VerifyTrafficGoesThroughTunAsync(details);
                if (!tunVerified)
                {
                    details["tun_verification"] = "FAILED - traffic may be bypassing TUN (self-excluded)";
                }
            }
            else
            {
                details["tun_verification"] = "Skipped (TUN off / proxy-path mode)";
            }

            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(8),
                AllowAutoRedirect = true,
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
                DefaultRequestHeaders =
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" }
                }
            };

            foreach (var (url, expectedTitle) in TestSites)
            {
                var siteName = new Uri(url).Host.Replace("www.", "");
                try
                {
                    var siteSw = Stopwatch.StartNew();
                    var response = await http.GetAsync(url);
                    siteSw.Stop();

                    var html = await response.Content.ReadAsStringAsync();
                    var title = ExtractTitle(html);
                    var statusCode = (int)response.StatusCode;

                    details[$"{siteName}_status"] = statusCode;
                    details[$"{siteName}_title"] = title ?? "(empty)";
                    details[$"{siteName}_time_ms"] = siteSw.ElapsedMilliseconds;

                    if (statusCode >= 200 && statusCode < 400 &&
                        title != null && title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        details[$"{siteName}_result"] = "PASS";
                        passCount++;
                    }
                    else if (statusCode >= 200 && statusCode < 400)
                    {
                        // Some sites block bots / change titles; HTTP success still counts soft-pass
                        details[$"{siteName}_result"] = "PASS (HTTP ok, title mismatch)";
                        passCount++;
                    }
                    else
                    {
                        details[$"{siteName}_result"] = $"FAIL (status={statusCode})";
                    }
                }
                catch (TaskCanceledException)
                {
                    details[$"{siteName}_result"] = "FAIL (timeout)";
                }
                catch (Exception ex)
                {
                    details[$"{siteName}_result"] = $"FAIL: {ex.Message}";
                }
            }

            details["pass_count"] = passCount;
            details["total_count"] = TestSites.Length;

            var tunFailed = details.TryGetValue("tun_verification", out var tv)
                            && tv.ToString()!.StartsWith("FAILED", StringComparison.OrdinalIgnoreCase);

            if (passCount == TestSites.Length)
            {
                var status = tunFailed ? HealthCheckStatus.Warning : HealthCheckStatus.Pass;
                var msg = tunFailed
                    ? $"All {TestSites.Length} sites accessible via SOCKS, but TUN bypass suspected"
                    : $"All {TestSites.Length} sites accessible via SOCKS";
                sw.Stop();
                return new HealthCheckResult("Website Access", status, msg, sw.Elapsed, details);
            }

            if (passCount == 0)
            {
                sw.Stop();
                return new HealthCheckResult("Website Access", HealthCheckStatus.Fail,
                    "No websites accessible via SOCKS - possible MTU/fragment/QUIC issue", sw.Elapsed, details);
            }

            sw.Stop();
            return new HealthCheckResult("Website Access", HealthCheckStatus.Warning,
                $"{passCount}/{TestSites.Length} sites accessible via SOCKS", sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("Website Access", HealthCheckStatus.Error,
                $"Website check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static async Task<bool> VerifyTrafficGoesThroughTunAsync(Dictionary<string, object> details)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                details["tun_verification"] = "Skipped (non-Windows)";
                return true;
            }

            // Direct TCP (not via SOCKS) to see which local interface OS chooses
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync("1.1.1.1", 443, cts.Token);
            var localEp = client.Client.LocalEndPoint as IPEndPoint;
            if (localEp == null) return true;

            var localIp = localEp.Address.ToString();
            details["test_source_ip"] = localIp;

            // Match local IP against TUN adapter unicast addresses
            var tun = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(ni =>
                ni.Name.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
                ni.Name.Contains(Global.V2rayTunName, StringComparison.OrdinalIgnoreCase) ||
                ni.Name.Contains(Global.SingboxTunName, StringComparison.OrdinalIgnoreCase) ||
                ni.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase));

            if (tun == null)
            {
                details["traffic_on_tun"] = false;
                details["tun_verification"] = "FAILED - TUN adapter not found while verifying source IP";
                return false;
            }

            var tunIps = tun.GetIPProperties().UnicastAddresses
                .Select(u => u.Address.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            details["tun_adapter"] = tun.Name;
            details["tun_ips"] = string.Join(", ", tunIps.Take(4));

            var isOnTun = tunIps.Contains(localIp);
            details["traffic_on_tun"] = isOnTun;
            if (isOnTun)
            {
                details["tun_verification"] = "OK - source IP belongs to TUN adapter";
            }
            return isOnTun;
        }
        catch (Exception ex)
        {
            details["tun_verification"] = $"Skipped (verify error: {ex.Message})";
            return true;
        }
    }

    private static string? ExtractTitle(string html)
    {
        var idx = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = html.IndexOf('>', idx);
        if (start < 0) return null;

        var end = html.IndexOf("</title", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;

        return html.Substring(start + 1, end - start - 1).Trim();
    }
}
