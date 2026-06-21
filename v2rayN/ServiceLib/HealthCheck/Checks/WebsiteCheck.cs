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

    public async Task<HealthCheckResult> CheckAsync()
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();
        var passCount = 0;

        try
        {
            var tunVerified = await VerifyTrafficGoesThroughTunAsync(details);
            if (!tunVerified)
            {
                details["tun_verification"] = "FAILED — traffic may be bypassing TUN (self-excluded)";
            }

            using var http = new HttpClient
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

            var tunFailed = details.TryGetValue("tun_verification", out var tv) && tv.ToString()!.StartsWith("FAILED");

            if (passCount == TestSites.Length)
            {
                var status = tunFailed ? HealthCheckStatus.Warning : HealthCheckStatus.Pass;
                var msg = tunFailed
                    ? $"All {TestSites.Length} sites accessible, but TUN bypass suspected"
                    : $"All {TestSites.Length} sites accessible";
                sw.Stop();
                return new HealthCheckResult("Website Access", status, msg, sw.Elapsed, details);
            }

            if (passCount == 0)
            {
                sw.Stop();
                return new HealthCheckResult("Website Access", HealthCheckStatus.Fail,
                    "No websites accessible — possible MTU/fragment/QUIC issue", sw.Elapsed, details);
            }

            sw.Stop();
            return new HealthCheckResult("Website Access", HealthCheckStatus.Warning,
                $"{passCount}/{TestSites.Length} sites accessible", sw.Elapsed, details);
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

            using var client = new TcpClient();
            await client.ConnectAsync("1.1.1.1", 443);
            var localEp = client.Client.LocalEndPoint as IPEndPoint;
            if (localEp == null) return true;

            var localIp = localEp.Address.ToString();
            details["test_source_ip"] = localIp;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "route",
                Arguments = $"print {localIp}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return true;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var tunInterfaceNames = new[] { "wintun", "TUN", "sing", "tun" };
            var isOnTun = tunInterfaceNames.Any(name =>
                output.Contains(name, StringComparison.OrdinalIgnoreCase));

            details["traffic_on_tun"] = isOnTun;
            return isOnTun;
        }
        catch
        {
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
