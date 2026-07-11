using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck;

public interface IDiagnosisRule
{
    bool Matches(HealthCheckReport report);
    string SuggestionEn { get; }
    string SuggestionZh { get; }
    int Priority { get; }
}

public static class DiagnosisEngine
{
    private static readonly List<IDiagnosisRule> Rules =
    [
        new TunNotfoundRule(),
        new TunNoIpRule(),
        new TunNoRouteRule(),
        new DnsSystemFailedRule(),
        new DnsAllFailedRule(),
        new RoutingLoopRule(),
        new RoutingProxyFailedRule(),
        new OutboundTcpFailedRule(),
        new OutboundTlsFailedRule(),
        new OutboundHttpFailedRule(),
        new WebsiteAllFailedBut204Rule(),
        new WebsiteSomeFailedRule(),
        new QualityPoorRule(),
        new CrossLayerTunBypassRule(),
        new CrossLayerDnsButOutboundOkRule(),
        new CrossLayer204ButWebsiteFailRule(),
        new NonTunModeRule(),
    ];

    public static List<string> Diagnose(HealthCheckReport report, string? locale = null)
    {
        var isZh = !string.IsNullOrEmpty(locale) && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        // Fall back to UI culture if locale not passed
        if (string.IsNullOrEmpty(locale))
        {
            isZh = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("zh", StringComparison.OrdinalIgnoreCase);
        }

        var findings = new List<string>();
        var matched = Rules
            .Where(r => r.Matches(report))
            .OrderBy(r => r.Priority)
            .Take(5)
            .ToList();

        foreach (var rule in matched)
        {
            findings.Add(isZh ? rule.SuggestionZh : rule.SuggestionEn);
        }

        return findings;
    }
}

#region TUN Interface Rules

public class TunNotfoundRule : IDiagnosisRule
{
    public int Priority => 10;
    public string SuggestionEn => "[TUN] Wintun adapter not found - v2rayN may not have admin privileges\n  -> Right-click v2rayN -> Run as administrator";
    public string SuggestionZh => "[TUN] 未找到 Wintun 适配器 - 可能未以管理员运行\n  -> 右键 v2rayN -> 以管理员身份运行";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}

public class TunNoIpRule : IDiagnosisRule
{
    public int Priority => 11;
    public string SuggestionEn => "[TUN] Adapter exists but has no IP - TUN inbound may not have started\n  -> Check Xray/sing-box core log for TUN startup errors";
    public string SuggestionZh => "[TUN] 适配器存在但无 IP - TUN 入站可能未启动\n  -> 检查 Xray/sing-box 核心日志中的 TUN 启动错误";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("no IPv4", StringComparison.OrdinalIgnoreCase);
    }
}

public class TunNoRouteRule : IDiagnosisRule
{
    public int Priority => 12;
    public string SuggestionEn => "[TUN] No default route - auto_route or strict_route may be disabled\n  -> Enable auto_route in TUN settings";
    public string SuggestionZh => "[TUN] 无默认路由 - auto_route/strict_route 可能关闭\n  -> 在 TUN 设置中启用 auto_route";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("no default route", StringComparison.OrdinalIgnoreCase);
    }
}

public class NonTunModeRule : IDiagnosisRule
{
    public int Priority => 15;
    public string SuggestionEn => "[Mode] TUN is not enabled - report covers proxy-path checks only\n  -> Enable TUN for full interface/website/quality diagnosis";
    public string SuggestionZh => "[模式] 当前未开启 TUN - 本次仅检查代理链路\n  -> 开启 TUN 后可诊断接口/网站/质量完整项";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("TUN Interface");
        if (r?.Details != null && r.Details.TryGetValue("mode", out var mode) && mode?.ToString() == "non_tun")
            return true;
        return r is { Status: HealthCheckStatus.Skipped } and { Summary: var s }
            && s.Contains("not enabled", StringComparison.OrdinalIgnoreCase);
    }
}

#endregion

#region DNS Rules

public class DnsSystemFailedRule : IDiagnosisRule
{
    public int Priority => 21;
    public string SuggestionEn => "[DNS] System DNS failed - TUN may not be intercepting DNS port 53\n  -> Verify dns inbound and routing hijack for UDP:53";
    public string SuggestionZh => "[DNS] 系统 DNS 失败 - TUN 可能未劫持 53 端口\n  -> 检查 DNS 入站与 UDP:53 路由劫持";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("DNS");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("System DNS", StringComparison.OrdinalIgnoreCase);
    }
}

public class DnsAllFailedRule : IDiagnosisRule
{
    public int Priority => 22;
    public string SuggestionEn => "[DNS] Complete DNS failure - no name resolution possible\n  -> Check if core is running and DNS servers are reachable";
    public string SuggestionZh => "[DNS] DNS 完全失败 - 无法解析域名\n  -> 检查核心是否运行及 DNS 服务器是否可达";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("DNS");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("All DNS", StringComparison.OrdinalIgnoreCase);
    }
}

#endregion

#region Routing Rules

public class RoutingLoopRule : IDiagnosisRule
{
    public int Priority => 30;
    public string SuggestionEn => "[Routing] Routing loop detected - proxy traffic is being routed back into TUN\n  -> Add proxy server IP to route exclusion / enable bypass self";
    public string SuggestionZh => "[路由] 检测到路由环路 - 代理流量被回灌进 TUN\n  -> 将节点 IP 加入路由排除 / 启用绕过自身";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Routing");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("loop", StringComparison.OrdinalIgnoreCase);
    }
}

public class RoutingProxyFailedRule : IDiagnosisRule
{
    public int Priority => 31;
    public string SuggestionEn => "[Routing] Proxy domains unreachable via local SOCKS - outbound chain may be broken\n  -> Check selected node, geoip/geosite data, and routing rules";
    public string SuggestionZh => "[路由] 经本地 SOCKS 无法访问代理域名 - 出站链路可能异常\n  -> 检查当前节点、geoip/geosite 与路由规则";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Routing");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("Proxy domains", StringComparison.OrdinalIgnoreCase);
    }
}

#endregion

#region Outbound Rules

public class OutboundTcpFailedRule : IDiagnosisRule
{
    public int Priority => 40;
    public string SuggestionEn => "[Outbound] TCP/SOCKS connect failed - proxy node may be down or port blocked\n  -> Switch node or check ISP port blocking";
    public string SuggestionZh => "[出站] TCP/SOCKS 连接失败 - 节点可能宕机或端口被封\n  -> 切换节点，或检查运营商是否封锁端口";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Outbound");
        return r is { Status: HealthCheckStatus.Fail } and { Summary: var s } && s.Contains("TCP", StringComparison.OrdinalIgnoreCase);
    }
}

public class OutboundTlsFailedRule : IDiagnosisRule
{
    public int Priority => 41;
    public string SuggestionEn => "[Outbound] TLS handshake failed - SNI or certificate issue\n  -> Verify serverName / Reality publicKey+shortId / certificate";
    public string SuggestionZh => "[出站] TLS 握手失败 - SNI 或证书问题\n  -> 检查 serverName / Reality publicKey+shortId / 证书配置";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Outbound");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("TLS", StringComparison.OrdinalIgnoreCase);
    }
}

public class OutboundHttpFailedRule : IDiagnosisRule
{
    public int Priority => 42;
    public string SuggestionEn => "[Outbound] HTTP 204 failed - outbound may be rate-limited or reset\n  -> Node may be under load; try another test URL/node";
    public string SuggestionZh => "[出站] HTTP 204 失败 - 出站可能被限速或重置\n  -> 节点可能负载较高，可换测试 URL/节点";

    public bool Matches(HealthCheckReport report)
    {
        var r = report.GetResult("Outbound");
        return r is { Status: HealthCheckStatus.Warning } and { Summary: var s } && s.Contains("204", StringComparison.OrdinalIgnoreCase);
    }
}

#endregion

#region Website Rules

public class WebsiteAllFailedBut204Rule : IDiagnosisRule
{
    public int Priority => 50;
    public string SuggestionEn => "[Website] All websites unreachable despite outbound PASS - possible MTU/fragment issue\n  -> Try MTU 1280/1400; check HTTP/2 or QUIC interference";
    public string SuggestionZh => "[网站] 出站通过但网站全不可达 - 可能是 MTU/分片问题\n  -> 尝试 MTU 1280/1400；检查 HTTP/2 或 QUIC 干扰";

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
    public string SuggestionEn => "[Website] Some sites unreachable - partial outage may indicate CDN-specific issues";
    public string SuggestionZh => "[网站] 部分站点不可达 - 可能是特定 CDN 问题";

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
    public string SuggestionEn => "[Quality] Poor quality - high latency or packet loss\n  -> Try a closer node; check ISP throttling";
    public string SuggestionZh => "[质量] 质量较差 - 高延迟或丢包\n  -> 尝试更近节点；检查运营商是否限速";

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
    public string SuggestionEn => "[Cross-layer] Outbound works but TUN is not active - traffic may bypass TUN\n  -> Check system routing / another proxy overriding settings";
    public string SuggestionZh => "[跨层] 出站正常但 TUN 未激活 - 流量可能绕过 TUN\n  -> 检查系统路由 / 是否有其他代理覆盖设置";

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
    public string SuggestionEn => "[Cross-layer] DNS fails but outbound works - DNS layer broken while proxy is alive\n  -> Check DNS hijack / fake-ip conflicts";
    public string SuggestionZh => "[跨层] DNS 失败但出站正常 - DNS 层异常而代理仍可用\n  -> 检查 DNS 劫持 / fake-ip 冲突";

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
    public string SuggestionEn => "[Cross-layer] 204 passes but websites fail - MTU/fragment/HTTP2 issue\n  -> Reduce TUN MTU to 1280; try disabling HTTP/2";
    public string SuggestionZh => "[跨层] 204 通过但网站失败 - MTU/分片/HTTP2 问题\n  -> 将 TUN MTU 降到 1280；尝试关闭 HTTP/2";

    public bool Matches(HealthCheckReport report)
    {
        var web = report.GetResult("Website Access");
        var outb = report.GetResult("Outbound");
        return web is { Status: HealthCheckStatus.Fail }
            && outb is { Status: HealthCheckStatus.Pass };
    }
}

#endregion
