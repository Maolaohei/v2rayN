using System.Net;
using System.Net.NetworkInformation;
using Google.Protobuf;

var geositePath = @"D:\UGit\v2rayN\v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0\bin\geosite.dat";
var geoipPath = @"D:\UGit\v2rayN\v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0\bin\geoip.dat";

if (!File.Exists(geositePath))
{
    Console.WriteLine($"geosite.dat not found at: {geositePath}");
    Console.WriteLine("Searching...");
    var candidates = Directory.GetFiles(@"D:\UGit\v2rayN\v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0", "geosite.dat", SearchOption.AllDirectories);
    foreach (var c in candidates)
    {
        Console.WriteLine($"  Found: {c}");
        geositePath = c;
    }
    if (!File.Exists(geositePath))
    {
        Console.WriteLine("geosite.dat not found anywhere. Aborting.");
        return;
    }
}

Console.WriteLine($"geosite.dat: {new FileInfo(geositePath).Length / 1024}KB ({geositePath})");

// === Load geosite using raw bytes ===
var geositeDomains = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
var geodata = File.ReadAllBytes(geositePath);
var input = new CodedInputStream(geodata);
var totalSites = 0;

while (!input.IsAtEnd)
{
    var tag = input.ReadTag();
    if (tag == 0) break;
    if (WireFormat.GetTagFieldNumber(tag) == 1 && WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
    {
        var siteData = input.ReadBytes().ToByteArray();
        var si = new CodedInputStream(siteData);
        var cc = "";
        var domains = new List<string>();

        while (!si.IsAtEnd)
        {
            var t = si.ReadTag();
            if (t == 0) break;
            var fn = WireFormat.GetTagFieldNumber(t);
            var wt = WireFormat.GetTagWireType(t);

            if (fn == 1 && wt == WireFormat.WireType.LengthDelimited)
            {
                cc = si.ReadString();
            }
            else if (fn == 2 && wt == WireFormat.WireType.LengthDelimited)
            {
                var innerData = si.ReadBytes().ToByteArray();
                var domainStr = TryParseDomainEntry(innerData);
                if (domainStr != null) domains.Add(domainStr);
            }
            else
            {
                si.SkipLastField();
            }
        }

        if (!string.IsNullOrEmpty(cc))
        {
            geositeDomains[cc] = domains;
            totalSites++;
        }
    }
    else input.SkipLastField();
}

Console.WriteLine($"\nGeosite: {totalSites} entries");

// Show some keys
var sampleKeys = geositeDomains.Keys.Where(k => k.Length <= 10).Take(20).ToList();
Console.WriteLine($"Keys (<=10 chars): {string.Join(", ", sampleKeys)}");

// Check CN
if (geositeDomains.TryGetValue("CN", out var cnRules))
{
    Console.WriteLine($"CN: {cnRules.Count} rules");
    if (cnRules.Count > 0) Console.WriteLine($"  First 5: {string.Join(", ", cnRules.Take(5))}");
    // Check if baidu.com is in CN
    var baiduInCn = cnRules.Any(r => r.Contains("baidu"));
    Console.WriteLine($"  baidu.com in CN: {baiduInCn}");
}
else
{
    Console.WriteLine("CN not found directly, searching...");
    foreach (var key in geositeDomains.Keys)
    {
        if (key.Contains("cn", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"  Found: '{key}' with {geositeDomains[key].Count} rules");
    }
}

// Check GOOGLE
if (geositeDomains.TryGetValue("GOOGLE", out var googleRules))
{
    Console.WriteLine($"\nGOOGLE: {googleRules.Count} rules");
    if (googleRules.Count > 0) Console.WriteLine($"  First 5: {string.Join(", ", googleRules.Take(5))}");
}
else
{
    Console.WriteLine("\nGOOGLE not found, searching for google-related keys...");
    foreach (var key in geositeDomains.Keys)
    {
        if (key.Contains("google", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"  Found: '{key}' with {geositeDomains[key].Count} rules");
    }
}

// Test domain matching
Console.WriteLine("\n=== Domain Match Test ===");
foreach (var domain in new[] { "baidu.com", "www.baidu.com", "taobao.com", "google.com", "github.com" })
{
    var matched = false;
    var matchedKey = "";
    foreach (var (key, rules) in geositeDomains)
    {
        foreach (var rule in rules)
        {
            var parts = rule.Split(':', 2);
            var type = parts.Length > 0 ? parts[0] : "plain";
            var value = parts.Length > 1 ? parts[1] : rule;
            var domainLower = domain.ToLowerInvariant();
            var v = value.ToLowerInvariant();
            bool hit = type switch
            {
                "plain" or "domain" => domainLower == v || domainLower.EndsWith("." + v),
                "full" => domainLower == v,
                _ => false
            };
            if (hit) { matched = true; matchedKey = key; break; }
        }
        if (matched) break;
    }
    Console.WriteLine($"  {domain}: {(matched ? $"✓ {matchedKey}" : "✗ none")}");
}

string? TryParseDomainEntry(byte[] data)
{
    try
    {
        var di = new CodedInputStream(data);
        var tp = 0;
        var value = "";
        while (!di.IsAtEnd)
        {
            var dt = di.ReadTag();
            if (dt == 0) break;
            var df = WireFormat.GetTagFieldNumber(dt);
            var dw = WireFormat.GetTagWireType(dt);

            // v2ray Entry: field 1 = type (varint), field 2 = value (string)
            if (df == 1 && dw == WireFormat.WireType.Varint) tp = (int)di.ReadUInt32();
            else if (df == 2 && dw == WireFormat.WireType.LengthDelimited) value = di.ReadString();
            else di.SkipLastField();
        }
        if (string.IsNullOrEmpty(value)) return null;
        var prefix = tp switch { 0 => "plain", 1 => "regex", 2 => "domain", 3 => "full", _ => "plain" };
        return $"{prefix}:{value}";
    }
    catch { return null; }
}

// === GeoIP Test ===
Console.WriteLine("\n\n========== GeoIP Test ==========");
if (!File.Exists(geoipPath))
{
    // Search for it
    var candidates = Directory.GetFiles(@"D:\UGit\v2rayN\v2rayN\v2rayN\bin\Debug\net10.0-windows10.0.19041.0", "geoip.dat", SearchOption.AllDirectories);
    if (candidates.Length > 0)
    {
        geoipPath = candidates[0];
        Console.WriteLine($"Found geoip.dat at: {geoipPath}");
    }
    else
    {
        Console.WriteLine("geoip.dat not found anywhere. Aborting geoip test.");
        return;
    }
}

Console.WriteLine($"geoip.dat: {new FileInfo(geoipPath).Length / 1024}KB ({geoipPath})");

var geoipCidrs = new Dictionary<string, List<(byte[] Ip, int Prefix)>>(StringComparer.OrdinalIgnoreCase);
var geoipData = File.ReadAllBytes(geoipPath);
var giInput = new CodedInputStream(geoipData);
var totalIpEntries = 0;

while (!giInput.IsAtEnd)
{
    var tag = giInput.ReadTag();
    if (tag == 0) break;
    if (WireFormat.GetTagFieldNumber(tag) == 1 && WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
    {
        var ipData = giInput.ReadBytes().ToByteArray();
        var si = new CodedInputStream(ipData);
        var cc = "";
        var cidrs = new List<(byte[] Ip, int Prefix)>();

        while (!si.IsAtEnd)
        {
            var t = si.ReadTag();
            if (t == 0) break;
            var fn = WireFormat.GetTagFieldNumber(t);
            var wt = WireFormat.GetTagWireType(t);

            if (fn == 1 && wt == WireFormat.WireType.LengthDelimited)
            {
                cc = si.ReadString();
            }
            else if (fn == 2 && wt == WireFormat.WireType.LengthDelimited)
            {
                var cidrData = si.ReadBytes().ToByteArray();
                var ci = new CodedInputStream(cidrData);
                byte[]? ip = null;
                var prefix = 0;

                while (!ci.IsAtEnd)
                {
                    var ct = ci.ReadTag();
                    if (ct == 0) break;
                    var cfn = WireFormat.GetTagFieldNumber(ct);
                    var cwt = WireFormat.GetTagWireType(ct);

                    if (cfn == 1 && cwt == WireFormat.WireType.LengthDelimited)
                        ip = ci.ReadBytes().ToByteArray();
                    else if (cfn == 2 && cwt == WireFormat.WireType.Varint)
                    {
                        var rawPrefix = ci.ReadUInt32();
                        prefix = rawPrefix > 32 ? -1 : (int)rawPrefix;
                    }
                    else
                        ci.SkipLastField();
                }

                if (ip != null && ip.Length == 4 && prefix >= 0 && prefix <= 32)
                    cidrs.Add((ip, prefix));
            }
            else
            {
                si.SkipLastField();
            }
        }

        if (!string.IsNullOrEmpty(cc) && cidrs.Count > 0)
        {
            geoipCidrs[cc] = cidrs;
            totalIpEntries++;
        }
    }
    else
    {
        giInput.SkipLastField();
    }
}

Console.WriteLine($"\nGeoIP: {totalIpEntries} entries");

if (geoipCidrs.TryGetValue("cn", out var cnIpCidrs))
{
    Console.WriteLine($"CN: {cnIpCidrs.Count} CIDRs");
    Console.WriteLine($"  First 5: {string.Join(", ", cnIpCidrs.Take(5).Select(c => $"{c.Ip[0]}.{c.Ip[1]}.{c.Ip[2]}.{c.Ip[3]}/{c.Prefix}"))}");
}

// Test IP matching
Console.WriteLine("\n=== IP Match Test (223.5.5.5 should be CN) ===");
var testIps = new[] { "223.5.5.5", "8.8.8.8", "1.1.1.1", "114.114.114.114", "10.0.0.1" };
foreach (var ipStr in testIps)
{
    if (!IPAddress.TryParse(ipStr, out var addr)) continue;
    var targetBytes = addr.GetAddressBytes();
    var matched = false;
    var matchedKey = "";

    foreach (var (cc, cidrs) in geoipCidrs)
    {
        foreach (var (ip, prefix) in cidrs)
        {
            if (IsInCidr(targetBytes, ip, prefix))
            {
                matched = true;
                matchedKey = cc;
                break;
            }
        }
        if (matched) break;
    }
    Console.WriteLine($"  {ipStr}: {(matched ? $"✓ {matchedKey}" : "✗ none")}");
}

static bool IsInCidr(byte[] target, byte[] network, int prefixLen)
{
    if (prefixLen < 0 || prefixLen > 32) return false;
    if (target.Length != 4 || network.Length != 4) return false;

    var fullBytes = prefixLen / 8;
    var remainBits = prefixLen % 8;

    for (var i = 0; i < fullBytes; i++)
    {
        if (target[i] != network[i]) return false;
    }

    if (remainBits > 0 && fullBytes < 4)
    {
        unchecked
        {
            var mask = (byte)(0xFF << (8 - remainBits));
            if ((target[fullBytes] & mask) != (network[fullBytes] & mask))
                return false;
        }
    }

    return true;
}
