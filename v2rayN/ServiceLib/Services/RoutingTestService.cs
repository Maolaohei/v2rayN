using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Google.Protobuf;

namespace ServiceLib.Services;

public class GeoDataManager
{
    private static Dictionary<string, List<string>>? _geositeDomains;
    private static Dictionary<string, List<(byte[] Ip, int Prefix)>>? _geoipCidrs;
    private static readonly object _lock = new();
    private static bool _loaded;
    private static string? _geositePath;
    private static string? _geoipPath;
    private static int _geositeEntryCount;
    private static int _geoipEntryCount;

    public static string? LastError { get; private set; }

    public static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            LastError = null;

            var basePath = AppContext.BaseDirectory;
            _geositePath = FindGeoFile(basePath, "geosite.dat");
            _geoipPath = FindGeoFile(basePath, "geoip.dat");

            if (_geositePath != null) LoadGeosite(_geositePath);
            if (_geoipPath != null) LoadGeoip(_geoipPath);

            LastError = (_geositePath == null ? "geosite.dat not found; " : "") +
                        (_geoipPath == null ? "geoip.dat not found; " : "") +
                        $"geosite entries: {_geositeEntryCount}, geoip entries: {_geoipEntryCount}";
        }
    }

    public static void Reload()
    {
        lock (_lock)
        {
            _loaded = false;
            _geositeDomains = null;
            _geoipCidrs = null;
            _geositeEntryCount = 0;
            _geoipEntryCount = 0;
        }
        EnsureLoaded();
    }

    private static string? FindGeoFile(string basePath, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(basePath, "bin", fileName),
            Path.Combine(basePath, "bin", "xray", fileName),
            Path.Combine(basePath, fileName),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }

    private static void LoadGeosite(string path)
    {
        _geositeDomains = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var data = File.ReadAllBytes(path);
            var input = new CodedInputStream(data);

            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0) break;

                var fieldNumber = WireFormat.GetTagFieldNumber(tag);
                var wireType = WireFormat.GetTagWireType(tag);

                if (fieldNumber == 1 && wireType == WireFormat.WireType.LengthDelimited)
                {
                    var siteData = input.ReadBytes();
                    ParseGeoSiteEntry(siteData);
                }
                else
                {
                    input.SkipLastField();
                }
            }
        }
        catch (Exception ex)
        {
            LastError = $"geosite parse error: {ex.Message}";
        }
    }

    private static void ParseGeoSiteEntry(ByteString siteData)
    {
        var input = new CodedInputStream(siteData.ToByteArray());
        var countryCode = "";
        var domains = new List<string>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (fieldNumber)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    countryCode = input.ReadString();
                    break;
                case 2 when wireType == WireFormat.WireType.LengthDelimited:
                    var domainData = input.ReadBytes();
                    var domainEntry = ParseDomainEntry(domainData);
                    if (domainEntry != null) domains.Add(domainEntry);
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        if (!string.IsNullOrEmpty(countryCode))
        {
            _geositeDomains![countryCode] = domains;
            _geositeEntryCount++;
        }
    }

    private static string? ParseDomainEntry(ByteString domainData)
    {
        var input = new CodedInputStream(domainData.ToByteArray());
        var type = 0;
        var value = "";

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (fieldNumber)
            {
                case 1 when wireType == WireFormat.WireType.Varint:
                    type = (int)input.ReadUInt32();
                    break;
                case 2 when wireType == WireFormat.WireType.LengthDelimited:
                    value = input.ReadString();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        if (string.IsNullOrEmpty(value)) return null;

        return type switch
        {
            0 => $"plain:{value}",
            1 => $"regex:{value}",
            2 => $"domain:{value}",
            3 => $"full:{value}",
            _ => $"plain:{value}"
        };
    }

    private static void LoadGeoip(string path)
    {
        _geoipCidrs = new Dictionary<string, List<(byte[] Ip, int Prefix)>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var data = File.ReadAllBytes(path);
            var input = new CodedInputStream(data);

            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                if (tag == 0) break;

                var fieldNumber = WireFormat.GetTagFieldNumber(tag);
                var wireType = WireFormat.GetTagWireType(tag);

                if (fieldNumber == 1 && wireType == WireFormat.WireType.LengthDelimited)
                {
                    var ipData = input.ReadBytes();
                    ParseGeoIPEntry(ipData);
                }
                else
                {
                    input.SkipLastField();
                }
            }
        }
        catch (Exception ex)
        {
            LastError = (LastError ?? "") + $"geoip parse error: {ex.Message}";
        }
    }

    private static void ParseGeoIPEntry(ByteString ipData)
    {
        var input = new CodedInputStream(ipData.ToByteArray());
        var countryCode = "";
        var cidrs = new List<(byte[] Ip, int Prefix)>();

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (fieldNumber)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    countryCode = input.ReadString();
                    break;
                case 2 when wireType == WireFormat.WireType.LengthDelimited:
                    var cidrData = input.ReadBytes();
                    var cidr = ParseCIDREntry(cidrData);
                    if (cidr.HasValue) cidrs.Add(cidr.Value);
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        if (!string.IsNullOrEmpty(countryCode) && cidrs.Count > 0)
        {
            _geoipCidrs![countryCode] = cidrs;
            _geoipEntryCount++;
        }
    }

    private static (byte[] Ip, int Prefix)? ParseCIDREntry(ByteString cidrData)
    {
        var input = new CodedInputStream(cidrData.ToByteArray());
        byte[]? ip = null;
        var prefix = 0;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (fieldNumber)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    ip = input.ReadBytes().ToByteArray();
                    break;
                case 2 when wireType == WireFormat.WireType.Varint:
                    var rawPrefix = input.ReadUInt32();
                    prefix = rawPrefix > 128 ? -1 : (int)rawPrefix;
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        if (ip == null) return null;
        if ((ip.Length != 4 && ip.Length != 16) || prefix < 0) return null;
        if (ip.Length == 4 && prefix > 32) return null;
        if (ip.Length == 16 && prefix > 128) return null;
        return (ip, prefix);
    }

    public static bool IsDomainInGeosite(string countryCode, string domain)
    {
        EnsureLoaded();
        if (_geositeDomains == null) return false;

        if (!_geositeDomains.TryGetValue(countryCode, out var rules)) return false;

        var domainLower = domain.ToLowerInvariant();
        foreach (var rule in rules)
        {
            if (rule.StartsWith("plain:"))
            {
                var v = rule["plain:".Length..].ToLowerInvariant();
                if (domainLower == v || domainLower.EndsWith("." + v)) return true;
            }
            else if (rule.StartsWith("full:"))
            {
                var v = rule["full:".Length..].ToLowerInvariant();
                if (domainLower == v) return true;
            }
            else if (rule.StartsWith("domain:"))
            {
                var v = rule["domain:".Length..].ToLowerInvariant();
                if (domainLower == v || domainLower.EndsWith("." + v)) return true;
            }
            else if (rule.StartsWith("regex:"))
            {
                try
                {
                    if (Regex.IsMatch(domain, rule["regex:".Length..], RegexOptions.IgnoreCase | RegexOptions.Compiled)) return true;
                }
                catch { }
            }
        }

        return false;
    }

    public static bool IsIpInGeoip(string countryCode, IPAddress addr)
    {
        EnsureLoaded();
        if (_geoipCidrs == null) return false;

        if (!_geoipCidrs.TryGetValue(countryCode, out var cidrs)) return false;

        var targetBytes = addr.GetAddressBytes();

        foreach (var (ip, prefix) in cidrs)
        {
            if (IsInCidr(targetBytes, ip, prefix)) return true;
        }

        return false;
    }

    public static bool IsPrivateIp(IPAddress addr)
    {
        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 127) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            return false;
        }

        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (addr.Equals(IPAddress.IPv6Loopback)) return true;
            if (addr.IsIPv6LinkLocal) return true;
            if (addr.IsIPv6SiteLocal) return true;
            var bytes = addr.GetAddressBytes();
            if (bytes[0] == 0xfc || bytes[0] == 0xfd) return true;
            return false;
        }

        return false;
    }

    private static bool IsInCidr(byte[] targetBytes, byte[] networkBytes, int prefixLen)
    {
        if (targetBytes.Length != networkBytes.Length) return false;
        var maxPrefix = targetBytes.Length * 8;
        if (prefixLen < 0 || prefixLen > maxPrefix) return false;

        var fullBytes = prefixLen / 8;
        var remainBits = prefixLen % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (targetBytes[i] != networkBytes[i]) return false;
        }

        if (remainBits > 0 && fullBytes < targetBytes.Length)
        {
            unchecked
            {
                var mask = (byte)(0xFF << (8 - remainBits));
                if ((targetBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                    return false;
            }
        }

        return true;
    }

    public static string GetDiagnosticInfo()
    {
        EnsureLoaded();
        var geositeCount = _geositeDomains?.Count ?? 0;
        var geoipCount = _geoipCidrs?.Count ?? 0;
        var cnDomains = _geositeDomains?.ContainsKey("cn") == true ? _geositeDomains["cn"].Count : 0;
        var cnCidrs = _geoipCidrs?.ContainsKey("cn") == true ? _geoipCidrs["cn"].Count : 0;
        return $"geosite.dat: {_geositePath ?? "NOT FOUND"}, entries: {geositeCount}, CN domains: {cnDomains}\n" +
               $"geoip.dat: {_geoipPath ?? "NOT FOUND"}, entries: {geoipCount}, CN cidrs: {cnCidrs}\n" +
               $"Error: {LastError ?? "none"}";
    }
}

public static class RuleTestMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public static List<RuleTestResult> TestAllRules(string input, List<RulesItem> rules)
    {
        var results = new List<RuleTestResult>();
        var inputLower = input.ToLowerInvariant();
        var isFirstMatch = true;

        foreach (var rule in rules)
        {
            if (rule.Enabled == false)
            {
                results.Add(new RuleTestResult { Rule = rule, Matched = false, MatchField = "", IsFirstMatch = false });
                continue;
            }

            var hasDomainRule = rule.Domain is { Count: > 0 };
            var hasIpRule = rule.Ip is { Count: > 0 };
            var hasPortRule = !string.IsNullOrEmpty(rule.Port);
            var hasProcessRule = rule.Process is { Count: > 0 };

            var domainMatched = hasDomainRule && IsDomainMatch(inputLower, rule.Domain);
            var ipMatched = hasIpRule && IsIpMatch(input, rule.Ip);
            var portMatched = hasPortRule && IsPortMatch(input, rule.Port);
            var processMatched = hasProcessRule && IsProcessMatch(inputLower, rule.Process);

            var activeFields = (hasDomainRule ? 1 : 0) + (hasIpRule ? 1 : 0) + (hasPortRule ? 1 : 0) + (hasProcessRule ? 1 : 0);
            var matchedCount = (domainMatched ? 1 : 0) + (ipMatched ? 1 : 0) + (portMatched ? 1 : 0) + (processMatched ? 1 : 0);

            var matched = activeFields > 0 && matchedCount == activeFields;

            var matchedFields = new List<string>();
            if (domainMatched) matchedFields.Add("domain");
            if (ipMatched) matchedFields.Add("ip");
            if (portMatched) matchedFields.Add("port");
            if (processMatched) matchedFields.Add("process");

            var isFirst = matched && isFirstMatch;
            if (matched && isFirstMatch)
                isFirstMatch = false;

            results.Add(new RuleTestResult
            {
                Rule = rule,
                Matched = matched,
                MatchField = matched ? string.Join(", ", matchedFields) : "",
                IsFirstMatch = isFirst
            });
        }

        return results;
    }

    public static bool IsDomainMatch(string inputLower, List<string>? domains)
    {
        if (domains == null || domains.Count == 0) return false;

        foreach (var rule in domains)
        {
            var r = rule.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(r)) continue;

            if (r.StartsWith("geosite:"))
            {
                var code = rule["geosite:".Length..];
                if (GeoDataManager.IsDomainInGeosite(code, inputLower)) return true;
                continue;
            }

            if (r.StartsWith("regexp:"))
            {
                try
                {
                    if (CachedRegexMatch(inputLower, r["regexp:".Length..])) return true;
                }
                catch { }
                continue;
            }

            if (r.StartsWith("domain:"))
            {
                var domain = r["domain:".Length..];
                if (inputLower == domain || inputLower.EndsWith("." + domain)) return true;
                continue;
            }

            if (r.StartsWith("plain:"))
            {
                var domain = r["plain:".Length..];
                if (inputLower == domain || inputLower.EndsWith("." + domain)) return true;
                continue;
            }

            if (r.StartsWith("regex:"))
            {
                try
                {
                    if (CachedRegexMatch(inputLower, r["regex:".Length..])) return true;
                }
                catch { }
                continue;
            }

            if (r.StartsWith("keyword:"))
            {
                if (inputLower.Contains(r["keyword:".Length..])) return true;
                continue;
            }

            if (r.StartsWith("full:"))
            {
                if (inputLower == r["full:".Length..]) return true;
                continue;
            }

            if (r.StartsWith("dotless:"))
            {
                if (!inputLower.Contains('.') && !int.TryParse(inputLower, out _)) return true;
                continue;
            }

            if (inputLower == r || inputLower.EndsWith("." + r))
                return true;
        }

        return false;
    }

    public static bool IsIpMatch(string input, List<string>? ips)
    {
        if (ips == null || ips.Count == 0) return false;

        if (!IPAddress.TryParse(input, out var targetAddr)) return false;

        foreach (var rule in ips)
        {
            var r = rule.Trim();
            if (string.IsNullOrEmpty(r)) continue;

            if (r.StartsWith("geoip:"))
            {
                var geoType = r["geoip:".Length..];
                if (string.Equals(geoType, "private", StringComparison.OrdinalIgnoreCase) && GeoDataManager.IsPrivateIp(targetAddr))
                    return true;
                if (GeoDataManager.IsIpInGeoip(geoType, targetAddr))
                    return true;
                continue;
            }

            if (r.Contains('/'))
            {
                try
                {
                    var parts = r.Split('/');
                    var networkAddr = IPAddress.Parse(parts[0]);
                    var prefixLen = int.Parse(parts[1]);
                    if (IsInCidr(targetAddr, networkAddr, prefixLen)) return true;
                }
                catch { }
                continue;
            }

            if (IPAddress.TryParse(r, out var ruleAddr) && targetAddr.Equals(ruleAddr))
                return true;
        }

        return false;
    }

    public static bool IsPortMatch(string input, string? portRule)
    {
        if (string.IsNullOrEmpty(portRule)) return false;
        if (!int.TryParse(input, out var port)) return false;

        foreach (var r in portRule.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (r.Contains('-'))
            {
                var parts = r.Split('-');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out var from)
                    && int.TryParse(parts[1].Trim(), out var to)
                    && port >= from && port <= to)
                    return true;
                continue;
            }

            if (int.TryParse(r, out var rulePort) && port == rulePort)
                return true;
        }

        return false;
    }

    public static bool IsProcessMatch(string inputLower, List<string>? processes)
    {
        if (processes == null || processes.Count == 0) return false;

        foreach (var rule in processes)
        {
            var r = rule.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(r)) continue;

            if (inputLower == r || inputLower.EndsWith("\\" + r))
                return true;
        }

        return false;
    }

    private static bool CachedRegexMatch(string input, string pattern)
    {
        var regex = _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        return regex.IsMatch(input);
    }

    private static bool IsInCidr(IPAddress target, IPAddress network, int prefixLen)
    {
        var targetBytes = target.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        if (targetBytes.Length != networkBytes.Length) return false;
        if (prefixLen < 0 || prefixLen > targetBytes.Length * 8) return false;

        var fullBytes = prefixLen / 8;
        var remainBits = prefixLen % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (targetBytes[i] != networkBytes[i]) return false;
        }

        if (remainBits > 0 && fullBytes < targetBytes.Length)
        {
            unchecked
            {
                var mask = (byte)(0xFF << (8 - remainBits));
                if ((targetBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                    return false;
            }
        }

        return true;
    }
}

public class RuleTestResult
{
    public RulesItem Rule { get; set; } = new();
    public bool Matched { get; set; }
    public string MatchField { get; set; } = "";
    public bool IsFirstMatch { get; set; }
}
