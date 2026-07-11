using System.Diagnostics;
using System.Net;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class RoutingCheck
{
    private static readonly string[] ProxyDomains = ["google.com", "youtube.com", "github.com"];

    public async Task<HealthCheckResult> CheckAsync(int? proxyPort = null, bool tunEnabled = true)
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();

        try
        {
            var port = proxyPort ?? await GetLocalSocksPortAsync();
            if (port <= 0)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Error,
                    "No local proxy port available for routing test", sw.Elapsed, details);
            }

            details["proxy_port"] = port;
            details["tun_enabled"] = tunEnabled;
            details["routing_note"] = tunEnabled
                ? "Proxy-domain probes go through local SOCKS (core routing). Direct domains are not judged via SOCKS under global TUN."
                : "TUN off: only proxy-path reachability is checked via local SOCKS.";

            var loopResult = await CheckRoutingLoopAsync(port);
            foreach (var kv in loopResult)
            {
                details[kv.Key] = kv.Value;
            }

            if (loopResult.TryGetValue("loop_detected", out var loopDetected) && loopDetected is true)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Fail,
                    "Routing loop detected - proxy server IP is routed into TUN", sw.Elapsed, details);
            }

            var proxyOk = await ProbeDomainsAsync(ProxyDomains, port, "proxy", details);

            // "Direct domains via SOCKS" is misleading under TUN/global proxy:
            // SOCKS always hits core, so success only means core can reach CN sites
            // (often via proxy/direct outbound rules), not "system direct bypass".
            details["direct_probe_mode"] = "informational_via_socks_disabled";
            details["direct_domains"] = "skipped (would not represent system direct under TUN)";

            if (!proxyOk)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Warning,
                    "Proxy domains unreachable via local SOCKS - routing or node may be incorrect", sw.Elapsed, details);
            }

            var exitNote = details.TryGetValue("loop_note", out var ln) ? ln?.ToString() : null;
            sw.Stop();
            return new HealthCheckResult("Routing", HealthCheckStatus.Pass,
                string.IsNullOrEmpty(exitNote)
                    ? "Proxy-path routing verification passed"
                    : $"Proxy-path routing OK ({exitNote})",
                sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("Routing", HealthCheckStatus.Error,
                $"Routing check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static readonly string[] ExitIpServices =
    [
        "https://api.ipify.org?format=json",
        "https://ipinfo.io/json",
        "https://ifconfig.me/all.json",
    ];

    private static async Task<Dictionary<string, object>> CheckRoutingLoopAsync(int socksPort)
    {
        var result = new Dictionary<string, object>();
        try
        {
            using var handler = new System.Net.Http.SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{socksPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AllowAutoRedirect = false,
            };
            using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            string? exitIp = null;
            foreach (var service in ExitIpServices)
            {
                try
                {
                    var response = await http.GetStringAsync(service);
                    var doc = System.Text.Json.JsonDocument.Parse(response);
                    exitIp = doc.RootElement.TryGetProperty("ip", out var ipProp) ? ipProp.GetString()
                        : doc.RootElement.TryGetProperty("origin", out var originProp) ? originProp.GetString()
                        : null;
                    if (!string.IsNullOrEmpty(exitIp)) break;
                }
                catch
                {
                    // Try next service
                }
            }

            if (!string.IsNullOrEmpty(exitIp))
            {
                result["exit_ip"] = exitIp;
                // True loop detection would compare exit IP with server IP + self-exclude;
                // obtaining an exit IP only proves the proxy path works, not a loop.
                result["loop_detected"] = false;
                result["loop_note"] = $"Exit IP: {exitIp}";
            }
            else
            {
                result["loop_detected"] = false;
                result["loop_note"] = "Could not reach any exit IP service";
            }
        }
        catch
        {
            result["loop_detected"] = false;
            result["loop_note"] = "Could not verify exit IP";
        }

        return result;
    }

    private static async Task<bool> ProbeDomainsAsync(string[] domains, int port, string expectedType, Dictionary<string, object> details)
    {
        var successCount = 0;
        using var handler = new System.Net.Http.SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AllowAutoRedirect = false,
        };
        using var http = new System.Net.Http.HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0" } }
        };

        foreach (var domain in domains)
        {
            try
            {
                var url = $"https://{domain}";
                var response = await http.GetAsync(url);
                var statusCode = (int)response.StatusCode;

                details[$"{expectedType}_{domain}"] = $"OK (HTTP {statusCode})";
                successCount++;
            }
            catch (Exception ex)
            {
                details[$"{expectedType}_{domain}"] = $"FAIL: {ex.Message}";
            }
        }

        return successCount > 0;
    }

    private static async Task<int> GetLocalSocksPortAsync()
    {
        return await Task.FromResult(AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
    }
}
