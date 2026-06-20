using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck;

public interface IDiagnosisRule
{
    bool Matches(HealthCheckReport report);
    string Suggestion { get; }
    int Priority { get; }
}

public static class DiagnosisEngine
{
    private static readonly List<IDiagnosisRule> Rules =
    [
        new TunNotfoundRule(),
        new TunNoIpRule(),
        new TunNoRouteRule(),
        new DnsLeakRule(),
        new DnsSystemFailedRule(),
        new DnsAllFailedRule(),
        new RoutingLoopRule(),
        new RoutingProxyFailedRule(),
        new RoutingDirectFailedRule(),
        new OutboundTcpFailedRule(),
        new OutboundTlsFailedRule(),
        new OutboundHttpFailedRule(),
        new WebsiteAllFailedBut204Rule(),
        new WebsiteSomeFailedRule(),
        new QualityPoorRule(),
        new CrossLayerTunBypassRule(),
        new CrossLayerDnsButOutboundOkRule(),
        new CrossLayer204ButWebsiteFailRule(),
    ];

    public static List<string> Diagnose(HealthCheckReport report)
    {
        var findings = new List<string>();
        var matched = Rules
            .Where(r => r.Matches(report))
            .OrderBy(r => r.Priority)
            .Take(5)
            .ToList();

        foreach (var rule in matched)
        {
            findings.Add(rule.Suggestion);
        }

        return findings;
    }
}

#region TUN Interface Rules

public class TunNotfoundRule : IDiagnosisRule
{
    public int Priority => 10;
    public string Suggestion => "[TUN] Wintun adapter not found — v2rayN may not have admin privileges\n  → Right-click v2rayN → Run as administrator";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("not found");
    }
}

public class TunNoIpRule : IDiagnosisRule
{
    public int Priority => 11;
    public string Suggestion => "[TUN] Adapter exists but has no IP — TUN inbound may not have started\n  → Check Xray/sing-box core log for TUN startup errors";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("no IPv4");
    }
}

public class TunNoRouteRule : IDiagnosisRule
{
    public int Priority => 12;
    public string Suggestion => "[TUN] No default route — auto_route or strict_route may be disabled\n  → Enable auto_route in TUN settings";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("no default route");
    }
}

#endregion

#region DNS Rules

public class DnsLeakRule : IDiagnosisRule
{
    public int Priority => 20;
    public string Suggestion => "[DNS] DNS leak detected — system DNS resolves differently from DoH\n  → TUN DNS may not be hijacking system queries\n  → Check if dns.hijack is enabled in routing rules";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("DNS");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("leak");
    }
}

public class DnsSystemFailedRule : IDiagnosisRule
{
    public int Priority => 21;
    public string Suggestion => "[DNS] System DNS failed — TUN may not be intercepting DNS port 53\n  → Verify dns.inbound.port is set and routing hijacks UDP:53";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("DNS");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("System DNS");
    }
}

public class DnsAllFailedRule : IDiagnosisRule
{
    public int Priority => 22;
    public string Suggestion => "[DNS] Complete DNS failure — no name resolution possible\n  → Check if Xray core is running and TUN inbound is active\n  → Verify DNS server addresses in config";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("DNS");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("All DNS");
    }
}

#endregion

#region Routing Rules

public class RoutingLoopRule : IDiagnosisRule
{
    public int Priority => 30;
    public string Suggestion => "[Routing] Routing loop detected — proxy traffic is being routed back into TUN\n  → Add proxy server IP to route exclusion list\n  → Check if bypass self is enabled";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Routing");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("loop");
    }
}

public class RoutingProxyFailedRule : IDiagnosisRule
{
    public int Priority => 31;
    public string Suggestion => "[Routing] Proxy domains unreachable — outbound chain may be broken\n  → Check if the selected node is alive\n  → Verify geoip.dat / geosite.dat are loaded";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Routing");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("Proxy domains");
    }
}

public class RoutingDirectFailedRule : IDiagnosisRule
{
    public int Priority => 32;
    public string Suggestion => "[Routing] Direct domains unreachable — routing may be forcing everything through proxy\n  → Check if direct outbound is configured\n  → Verify geosite:cn is in direct rule";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Routing");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("Direct domains");
    }
}

#endregion

#region Outbound Rules

public class OutboundTcpFailedRule : IDiagnosisRule
{
    public int Priority => 40;
    public string Suggestion => "[Outbound] TCP connect failed — proxy node may be down or port blocked\n  → Try switching to a different node\n  → Check if ISP blocks the node port";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Outbound");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("TCP");
    }
}

public class OutboundTlsFailedRule : IDiagnosisRule
{
    public int Priority => 41;
    public string Suggestion => "[Outbound] TLS handshake failed — SNI or certificate issue\n  → Verify serverName matches node config\n  → For Reality: check publicKey and shortId\n  → For TLS: check if certificate is valid";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Outbound");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("TLS");
    }
}

public class OutboundHttpFailedRule : IDiagnosisRule
{
    public int Priority => 42;
    public string Suggestion => "[Outbound] HTTP 204 failed —出口 may be rate-limited or reset\n  → Node may be under heavy load\n  → Try a different test URL";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Outbound");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("204");
    }
}

#endregion

#region Website Rules

public class WebsiteAllFailedBut204Rule : IDiagnosisRule
{
    public int Priority => 50;
    public string Suggestion => "[Website] All websites unreachable despite 204 PASS — possible MTU/fragment issue\n  → Try reducing MTU to 1280 or 1400\n  → Check if HTTP/2 is causing compatibility issues\n  → Disable QUIC if ISP blocks UDP";

    public bool Matches(HealthCheckReport report)
    {
        var web = report.GetResult("Website Access");
        var outb = report.GetResult("Outbound");
        return web is { Status: HealthCheckStatus.Fail }
            && outb is { Status: HealthCheckStatus.Pass };
    }
}

public class WebsiteSomeFailedRule : IDiagnosisRule
{
    public int Priority => 51;
    public string Suggestion => "[Website] Some sites unreachable — partial outage may indicate CDN-specific issues";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Website Access");
        return r is { Status: HealthCheckStatus.Warning };
    }
}

#endregion

#region Quality Rules

public class QualityPoorRule : IDiagnosisRule
{
    public int Priority => 60;
    public string Suggestion => "[Quality] Poor quality — high latency or packet loss\n  → Try a geographically closer node\n  → Check if ISP is throttling proxy traffic";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Quality");
        if (r is not { Status: HealthCheckStatus.Fail or HealthCheckStatus.Warning }) return false;
        if (r.Details != null && r.Details.TryGetValue("health_score", out var scoreObj) && scoreObj is int score)
            return score < 50;
        return false;
    }
}

#endregion

#region Cross-Layer Rules

public class CrossLayerTunBypassRule : IDiagnosisRule
{
    public int Priority => 100;
    public string Suggestion => "[Cross-layer] Outbound works but TUN is not active — traffic is bypassing TUN\n  → System may not be using TUN for routing\n  → Check if another proxy is overriding system settings";

    public bool Matches(HealthCheckReport report)
    {
        var tun = report.GetResult("TUN Interface");
        var outb = report.GetResult("Outbound");
        return tun is { Status: HealthCheckStatus.Fail or HealthCheckStatus.Error }
            && outb is { Status: HealthCheckStatus.Pass };
    }
}

public class CrossLayerDnsButOutboundOkRule : IDiagnosisRule
{
    public int Priority => 101;
    public string Suggestion => "[Cross-layer] DNS fails but outbound works — DNS layer is broken but proxy is alive\n  → Most likely DNS hijack not working or fake-ip conflict";

    public bool Matches(HealthCheckReport report)
    {
        var dns = report.GetResult("DNS");
        var outb = report.GetResult("Outbound");
        return dns is { Status: HealthCheckStatus.Fail or HealthCheckStatus.Error }
            && outb is { Status: HealthCheckStatus.Pass };
    }
}

public class CrossLayer204ButWebsiteFailRule : IDiagnosisRule
{
    public int Priority => 102;
    public string Suggestion => "[Cross-layer] 204 passes but websites fail — MTU/fragment/HTTP2 issue\n  → Reduce TUN MTU to 1280\n  → Try disabling HTTP/2 in browser";

    public bool Matches(HealthCheckReport report)
    {
        var web = report.GetResult("Website Access");
        var outb = report.GetResult("Outbound");
        return web is { Status: HealthCheckStatus.Fail }
            && outb is { Status: HealthCheckStatus.Pass };
    }
}

#endregion
