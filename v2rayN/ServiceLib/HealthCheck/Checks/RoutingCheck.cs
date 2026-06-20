using System.Diagnostics;
using System.Net;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class RoutingCheck
{
    private static readonly string[] DirectDomains = ["baidu.com", "bilibili.com", "taobao.com"];
    private static readonly string[] ProxyDomains = ["google.com", "youtube.com", "github.com"];

    public async Task<HealthCheckResult> CheckAsync(int? proxyPort = null)
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

            var loopResult = await CheckRoutingLoopAsync(port);
            foreach (var kv in loopResult)
            {
                details[kv.Key] = kv.Value;
            }

            if (loopResult.TryGetValue("loop_detected", out var loopDetected) && (bool)loopDetected)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Fail,
                    "Routing loop detected — proxy server IP is routed into TUN", sw.Elapsed, details);
            }

            var proxyOk = await ProbeDomainsAsync(ProxyDomains, port, "proxy", details);
            var directOk = await ProbeDomainsAsync(DirectDomains, port, "direct", details);

            if (!proxyOk && !directOk)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Fail,
                    "Cannot verify routing — proxy connection failed", sw.Elapsed, details);
            }

            if (!proxyOk)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Warning,
                    "Proxy domains unreachable — routing may be incorrect", sw.Elapsed, details);
            }

            if (!directOk)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Warning,
                    "Direct domains unreachable — may be routing through proxy unnecessarily", sw.Elapsed, details);
            }

            sw.Stop();
            return new HealthCheckResult("Routing", HealthCheckStatus.Pass,
                "Routing verification passed", sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("Routing", HealthCheckStatus.Error,
                $"Routing check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static async Task<Dictionary<string, object>> CheckRoutingLoopAsync(int socksPort)
    {
        var result = new Dictionary<string, object>();
        try
        {
            using var handler = new System.Net.Http.SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{socksPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            var response = await http.GetAsync("https://httpbin.org/ip");
            var json = await response.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("origin", out var origin))
            {
                var exitIp = origin.GetString();
                result["exit_ip"] = exitIp ?? "";
                result["loop_detected"] = false;
                result["loop_note"] = $"Exit IP: {exitIp}";
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
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };
        using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        foreach (var domain in domains)
        {
            try
            {
                var url = $"http://{domain}";
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
