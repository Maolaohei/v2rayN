using System.Diagnostics;
using System.Net.NetworkInformation;
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
            var hasDefaultRoute = false;
            foreach (var uni in tunAdapter.GetIPProperties().UnicastAddresses)
            {
                if (uni.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    hasIpv4 = true;
                    details["ipv4"] = uni.Address.ToString();
                    break;
                }
            }

            var routes = await GetRoutesAsync();
            hasDefaultRoute = routes.Any(r =>
                r.DestinationAddress == "0.0.0.0" &&
                r.InterfaceIndex == tunAdapter.GetIPProperties().GetIPv4Properties().Index);

            details["has_ipv4"] = hasIpv4;
            details["has_default_route"] = hasDefaultRoute;

            if (!hasIpv4)
            {
                sw.Stop();
                return new HealthCheckResult("TUN Interface", HealthCheckStatus.Fail,
                    "TUN adapter has no IPv4 address", sw.Elapsed, details);
            }

            if (!hasDefaultRoute)
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

    private static async Task<List<RouteEntry>> GetRoutesAsync()
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

                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 && parts[0].Contains('.'))
                    {
                        routes.Add(new RouteEntry
                        {
                            DestinationAddress = parts[0],
                            NetworkMask = parts[1],
                            Gateway = parts[2],
                            InterfaceIndex = int.TryParse(parts[3], out var idx) ? idx : 0
                        });
                    }
                }
            }
            catch { }
            return routes;
        });
    }

    private class RouteEntry
    {
        public string DestinationAddress { get; set; } = "";
        public string NetworkMask { get; set; } = "";
        public string Gateway { get; set; } = "";
        public int InterfaceIndex { get; set; }
    }
}
