using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class TunInterfaceCheck
{
    public async Task<HealthCheckResult> CheckAsync()
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();
        try
        {
            var tunAdapter = await FindTunAdapterAsync();
            if (tunAdapter == null)
            {
                details["adapter"] = "Not found";
                sw.Stop();
                return new HealthCheckResult("TUN Interface", HealthCheckStatus.Fail,
                    "TUN adapter not found", sw.Elapsed, details);
            }

            details["adapter"] = tunAdapter.Name;
            details["status"] = tunAdapter.OperationalStatus.ToString();

            if (tunAdapter.OperationalStatus != OperationalStatus.Up)
            {
                sw.Stop();
                return new HealthCheckResult("TUN Interface", HealthCheckStatus.Fail,
                    $"TUN adapter is {tunAdapter.OperationalStatus}", sw.Elapsed, details);
            }

            if (!OperatingSystem.IsWindows())
            {
                sw.Stop();
                return new HealthCheckResult("TUN Interface", HealthCheckStatus.Pass,
                    $"TUN adapter: {tunAdapter.Name} ({tunAdapter.OperationalStatus})", sw.Elapsed, details);
            }

            var hasIpv4 = false;
            string? tunIpv4 = null;
            var tunIfIndex = -1;
            try
            {
                tunIfIndex = tunAdapter.GetIPProperties().GetIPv4Properties().Index;
                details["if_index"] = tunIfIndex;
            }
            catch
            {
                details["if_index"] = "n/a";
            }

            foreach (var uni in tunAdapter.GetIPProperties().UnicastAddresses)
            {
                if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    hasIpv4 = true;
                    tunIpv4 = uni.Address.ToString();
                    details["ipv4"] = tunIpv4;
                    break;
                }
            }

            var routeInfo = await DetectDefaultRouteOnTunAsync(tunIfIndex, tunIpv4);
            foreach (var kv in routeInfo.Details)
            {
                details[kv.Key] = kv.Value;
            }

            details["has_ipv4"] = hasIpv4;
            details["has_default_route"] = routeInfo.HasDefaultRoute;

            if (!hasIpv4)
            {
                sw.Stop();
                return new HealthCheckResult("TUN Interface", HealthCheckStatus.Fail,
                    "TUN adapter has no IPv4 address", sw.Elapsed, details);
            }

            if (!routeInfo.HasDefaultRoute)
            {
                sw.Stop();
                return new HealthCheckResult("TUN Interface", HealthCheckStatus.Warning,
                    "TUN adapter has no default route (strict_route may be off)", sw.Elapsed, details);
            }

            sw.Stop();
            return new HealthCheckResult("TUN Interface", HealthCheckStatus.Pass,
                $"TUN adapter OK: {tunAdapter.Name}", sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("TUN Interface", HealthCheckStatus.Error,
                $"Check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static async Task<NetworkInterface?> FindTunAdapterAsync()
    {
        return await Task.FromResult(
            NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni =>
                    ni.Name.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
                    ni.Name.Contains(Global.V2rayTunName, StringComparison.OrdinalIgnoreCase) ||
                    ni.Name.Contains(Global.SingboxTunName, StringComparison.OrdinalIgnoreCase) ||
                    ni.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record RouteDetectResult(bool HasDefaultRoute, Dictionary<string, object> Details);

    /// <summary>
    /// Windows `route print` IPv4 active routes typically look like:
    /// Network Destination  Netmask  Gateway  Interface  Metric
    /// 0.0.0.0              0.0.0.0  x.x.x.x  y.y.y.y    n
    /// Interface column is an IP address (not ifIndex). Match against TUN IPv4.
    /// Fallback: match ifIndex when present as a numeric field.
    /// </summary>
    private static async Task<RouteDetectResult> DetectDefaultRouteOnTunAsync(int tunIfIndex, string? tunIpv4)
    {
        var details = new Dictionary<string, object>();
        var routes = await GetDefaultRoutesAsync();
        details["default_route_count"] = routes.Count;

        if (routes.Count == 0)
        {
            details["route_parse"] = "no default routes parsed";
            return new RouteDetectResult(false, details);
        }

        // Prefer matching Interface column to TUN IPv4
        if (!string.IsNullOrEmpty(tunIpv4))
        {
            var byIp = routes.Where(r =>
                string.Equals(r.InterfaceField, tunIpv4, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byIp.Count > 0)
            {
                details["route_match"] = "interface_ip";
                details["matched_gateway"] = byIp[0].Gateway;
                details["matched_metric"] = byIp[0].Metric;
                details["matched_interface"] = byIp[0].InterfaceField;
                return new RouteDetectResult(true, details);
            }
        }

        // Fallback: numeric interface index somewhere in line fields
        if (tunIfIndex > 0)
        {
            var byIdx = routes.Where(r => r.InterfaceIndex == tunIfIndex).ToList();
            if (byIdx.Count > 0)
            {
                details["route_match"] = "if_index";
                details["matched_gateway"] = byIdx[0].Gateway;
                details["matched_metric"] = byIdx[0].Metric;
                details["matched_interface"] = byIdx[0].InterfaceField;
                return new RouteDetectResult(true, details);
            }
        }

        // Show a sample for diagnostics
        var sample = routes.Take(3).Select(r =>
            $"{r.Destination}/{r.Mask} gw={r.Gateway} if={r.InterfaceField} metric={r.Metric}");
        details["route_sample"] = string.Join(" | ", sample);
        details["route_match"] = "none";
        return new RouteDetectResult(false, details);
    }

    private static async Task<List<RouteEntry>> GetDefaultRoutesAsync()
    {
        return await Task.Run(() =>
        {
            var routes = new List<RouteEntry>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "route",
                    Arguments = "print 0.0.0.0",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return routes;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                foreach (var raw in output.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // Expect at least: dest mask gateway interface [metric]
                    if (parts.Length < 4) continue;
                    if (parts[0] != "0.0.0.0") continue;
                    if (parts.Length >= 5 && parts[1] != "0.0.0.0")
                    {
                        // still allow if mask missing unusual formats
                    }

                    var dest = parts[0];
                    var mask = parts.Length > 1 ? parts[1] : "";
                    var gateway = parts.Length > 2 ? parts[2] : "";
                    var iface = parts.Length > 3 ? parts[3] : "";
                    var metric = 0;
                    if (parts.Length > 4 && int.TryParse(parts[^1], out var m))
                    {
                        metric = m;
                    }

                    var ifIndex = 0;
                    // Some builds put ifIndex instead of IP in interface field
                    if (int.TryParse(iface, out var idxOnly))
                    {
                        ifIndex = idxOnly;
                    }
                    else if (parts.Length >= 5)
                    {
                        // occasionally: dest mask gw ifIndex metric
                        if (int.TryParse(parts[3], out var midIdx) && !iface.Contains('.'))
                        {
                            ifIndex = midIdx;
                        }
                    }

                    routes.Add(new RouteEntry
                    {
                        Destination = dest,
                        Mask = mask,
                        Gateway = gateway,
                        InterfaceField = iface,
                        InterfaceIndex = ifIndex,
                        Metric = metric
                    });
                }
            }
            catch
            {
            }
            return routes;
        });
    }

    private class RouteEntry
    {
        public string Destination { get; set; } = "";
        public string Mask { get; set; } = "";
        public string Gateway { get; set; } = "";
        public string InterfaceField { get; set; } = "";
        public int InterfaceIndex { get; set; }
        public int Metric { get; set; }
    }
}
