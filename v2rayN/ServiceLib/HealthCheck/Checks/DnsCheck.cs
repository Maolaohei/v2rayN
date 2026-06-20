using System.Diagnostics;
using System.Net;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class DnsCheck
{
    private static readonly string[] TestDomains = ["google.com", "baidu.com", "cloudflare.com"];

    public async Task<HealthCheckResult> CheckAsync()
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();

        try
        {
            var systemResults = await ResolveViaSystemAsync();
            var doh1Results = await ResolveViaDohAsync("1.1.1.1");
            var doh2Results = await ResolveViaDohAsync("8.8.8.8");

            var allDomainsOk = true;
            var systemFailed = false;
            var dohFailed = false;

            foreach (var domain in TestDomains)
            {
                var sysOk = systemResults.TryGetValue(domain, out var sysIps) && sysIps.Count > 0;
                var doh1Ok = doh1Results.TryGetValue(domain, out var doh1Ips) && doh1Ips.Count > 0;
                var doh2Ok = doh2Results.TryGetValue(domain, out var doh2Ips) && doh2Ips.Count > 0;

                details[$"system_{domain}"] = sysOk ? string.Join(", ", sysIps!.Take(2)) : "FAIL";
                details[$"doh1_{domain}"] = doh1Ok ? string.Join(", ", doh1Ips!.Take(2)) : "FAIL";
                details[$"doh2_{domain}"] = doh2Ok ? string.Join(", ", doh2Ips!.Take(2)) : "FAIL";

                if (!sysOk) systemFailed = true;
                if (!doh1Ok && !doh2Ok) dohFailed = true;
                if (!sysOk && !doh1Ok && !doh2Ok) allDomainsOk = false;
            }

            if (!allDomainsOk)
            {
                sw.Stop();
                return new HealthCheckResult("DNS", HealthCheckStatus.Fail,
                    "All DNS resolution methods failed for some domains", sw.Elapsed, details);
            }

            if (systemFailed && !dohFailed)
            {
                details["conclusion"] = "System DNS failed but DoH works — TUN DNS may not be hijacking system DNS";
                sw.Stop();
                return new HealthCheckResult("DNS", HealthCheckStatus.Warning,
                    "System DNS resolution failed (DoH works)", sw.Elapsed, details);
            }

            if (!systemFailed && dohFailed)
            {
                details["conclusion"] = "System DNS works but DoH failed — DoH endpoint may be blocked";
                sw.Stop();
                return new HealthCheckResult("DNS", HealthCheckStatus.Warning,
                    "DoH resolution failed (system DNS works)", sw.Elapsed, details);
            }

            var leakResult = CheckDnsLeak(systemResults, doh1Results);
            foreach (var kv in leakResult)
            {
                details[kv.Key] = kv.Value;
            }

            if (leakResult.TryGetValue("leak_detected", out var leakStr) && (bool)leakStr)
            {
                details["conclusion"] = "System DNS and DoH return different IPs — possible DNS leak";
                sw.Stop();
                return new HealthCheckResult("DNS", HealthCheckStatus.Warning,
                    "DNS leak detected (system != DoH)", sw.Elapsed, details);
            }

            sw.Stop();
            return new HealthCheckResult("DNS", HealthCheckStatus.Pass,
                "All DNS resolution methods working, no leak detected", sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("DNS", HealthCheckStatus.Error,
                $"DNS check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static Dictionary<string, object> CheckDnsLeak(
        Dictionary<string, List<string>> system,
        Dictionary<string, List<string>> doh)
    {
        var result = new Dictionary<string, object>();
        var leakDetected = false;

        foreach (var domain in TestDomains)
        {
            if (system.TryGetValue(domain, out var sysIps) && doh.TryGetValue(domain, out var dohIps)
                && sysIps.Count > 0 && dohIps.Count > 0)
            {
                var sysSet = new HashSet<string>(sysIps);
                var dohSet = new HashSet<string>(dohIps);
                var common = sysSet.Intersect(dohSet).Any();

                if (!common)
                {
                    leakDetected = true;
                    result[$"leak_{domain}"] = $"system={sysIps.First()}, doh={dohIps.First()}";
                }
                else
                {
                    result[$"leak_{domain}"] = "consistent";
                }
            }
        }

        result["leak_detected"] = leakDetected;
        return result;
    }

    private static async Task<Dictionary<string, List<string>>> ResolveViaSystemAsync()
    {
        var results = new Dictionary<string, List<string>>();
        foreach (var domain in TestDomains)
        {
            try
            {
                var ips = await Dns.GetHostAddressesAsync(domain);
                results[domain] = ips.Select(ip => ip.ToString()).ToList();
            }
            catch
            {
                results[domain] = [];
            }
        }
        return results;
    }

    private static async Task<Dictionary<string, List<string>>> ResolveViaDohAsync(string dohServer)
    {
        var results = new Dictionary<string, List<string>>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        foreach (var domain in TestDomains)
        {
            try
            {
                var url = $"https://{dohServer}/dns-query?name={domain}&type=A";
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/dns-json");

                var response = await http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var ips = ParseDohResponse(json);
                results[domain] = ips;
            }
            catch
            {
                results[domain] = [];
            }
        }
        return results;
    }

    private static List<string> ParseDohResponse(string json)
    {
        var ips = new List<string>();
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Answer", out var answers))
            {
                foreach (var answer in answers.EnumerateArray())
                {
                    if (answer.TryGetProperty("data", out var data))
                    {
                        var ip = data.GetString();
                        if (!string.IsNullOrEmpty(ip) && ip.Contains('.'))
                        {
                            ips.Add(ip);
                        }
                    }
                }
            }
        }
        catch { }
        return ips;
    }
}
