using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class RoutingCheck
{
    private static readonly string[] ProxyDomains = ["google.com", "youtube.com", "github.com"];

    public async Task<HealthCheckResult> CheckAsync(int? proxyPort = null, bool tunEnabled = true, Config? config = null)
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

            var loopResult = await CheckRoutingLoopAsync(port, config, tunEnabled);
            foreach (var kv in loopResult)
            {
                details[kv.Key] = kv.Value;
            }

            if (loopResult.TryGetValue("loop_detected", out var loopDetected) && loopDetected is true)
            {
                sw.Stop();
                return new HealthCheckResult("Routing", HealthCheckStatus.Fail,
                    "Routing loop risk - server IP appears routed into TUN / missing route exclude", sw.Elapsed, details);
            }

            var proxyOk = await ProbeDomainsAsync(ProxyDomains, port, "proxy", details);

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

    private static async Task<Dictionary<string, object>> CheckRoutingLoopAsync(int socksPort, Config? config, bool tunEnabled)
    {
        var result = new Dictionary<string, object>();
        try
        {
            // 1) Resolve current node endpoint
            var server = config != null ? await ConfigHandler.GetDefaultServer(config) : null;
            string? serverHost = server?.Address?.Trim();
            var serverPort = server?.Port ?? 0;
            result["server_host"] = serverHost ?? "";
            result["server_port"] = serverPort;

            IPAddress? serverIp = null;
            if (!string.IsNullOrEmpty(serverHost))
            {
                if (IPAddress.TryParse(serverHost, out var parsed))
                {
                    serverIp = parsed;
                    result["server_ip"] = serverIp.ToString();
                }
                else
                {
                    try
                    {
                        var addrs = await Dns.GetHostAddressesAsync(serverHost);
                        serverIp = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                                   ?? addrs.FirstOrDefault();
                        if (serverIp != null)
                        {
                            result["server_ip"] = serverIp.ToString();
                            result["server_ip_resolved"] = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        result["server_ip_resolve_error"] = ex.Message;
                    }
                }
            }

            // 2) Is server IP present in route exclude list?
            var excludes = config?.TunModeItem?.RouteExcludeAddress ?? [];
            result["route_exclude_count"] = excludes.Count;
            var serverExcluded = false;
            if (serverIp != null)
            {
                var ipStr = serverIp.ToString();
                serverExcluded = excludes.Any(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    (string.Equals(x.Trim(), ipStr, StringComparison.OrdinalIgnoreCase)
                     || x.Trim().StartsWith(ipStr + "/", StringComparison.OrdinalIgnoreCase)
                     || x.Trim().StartsWith(ipStr + " ", StringComparison.OrdinalIgnoreCase)));
                result["server_in_route_exclude"] = serverExcluded;
            }

            // 3) On Windows TUN: does OS choose TUN as source for a direct connect to server IP?
            var sourceOnTun = false;
            if (tunEnabled && OperatingSystem.IsWindows() && serverIp != null && serverPort > 0)
            {
                try
                {
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    // Best-effort connect; even failures can yield a bound local endpoint after connect attempt.
                    try
                    {
                        await tcp.ConnectAsync(serverIp, serverPort, cts.Token);
                    }
                    catch
                    {
                        // ignore connect failures
                    }

                    if (tcp.Client.LocalEndPoint is IPEndPoint localEp)
                    {
                        var localIp = localEp.Address.ToString();
                        result["server_connect_source_ip"] = localIp;
                        sourceOnTun = IsIpOnTunAdapter(localIp);
                        result["server_connect_source_on_tun"] = sourceOnTun;
                    }
                }
                catch (Exception ex)
                {
                    result["server_connect_probe_error"] = ex.Message;
                }
            }

            // 4) Exit IP via SOCKS (proxy path liveness)
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{socksPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AllowAutoRedirect = false,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

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
                }
            }

            if (!string.IsNullOrEmpty(exitIp))
            {
                result["exit_ip"] = exitIp;
                result["loop_note"] = $"Exit IP: {exitIp}";
            }
            else
            {
                result["loop_note"] = "Could not reach any exit IP service";
            }

            // Loop risk heuristic:
            // - TUN on, and
            // - direct connect to server uses TUN source IP, and
            // - server IP is NOT in route exclude list
            // This is a strong signal of "node IP sucked into TUN" (classic loop/blackhole setup).
            var loop = tunEnabled && sourceOnTun && serverIp != null && !serverExcluded;
            result["loop_detected"] = loop;
            if (loop)
            {
                result["loop_note"] = $"Server IP {serverIp} source-on-TUN and not excluded";
            }
            else if (tunEnabled && serverIp != null && serverExcluded)
            {
                result["loop_note"] = (result.TryGetValue("loop_note", out var n) ? n + "; " : "") +
                                      $"server IP excluded ({serverIp})";
            }
        }
        catch
        {
            result["loop_detected"] = false;
            result["loop_note"] = "Could not verify routing loop";
        }

        return result;
    }

    private static bool IsIpOnTunAdapter(string localIp)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var isTun =
                    ni.Name.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
                    ni.Name.Contains(Global.V2rayTunName, StringComparison.OrdinalIgnoreCase) ||
                    ni.Name.Contains(Global.SingboxTunName, StringComparison.OrdinalIgnoreCase) ||
                    ni.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase);
                if (!isTun) continue;

                if (ni.GetIPProperties().UnicastAddresses.Any(u =>
                        string.Equals(u.Address.ToString(), localIp, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static async Task<bool> ProbeDomainsAsync(string[] domains, int port, string expectedType, Dictionary<string, object> details)
    {
        var successCount = 0;
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AllowAutoRedirect = false,
        };
        using var http = new HttpClient(handler)
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
