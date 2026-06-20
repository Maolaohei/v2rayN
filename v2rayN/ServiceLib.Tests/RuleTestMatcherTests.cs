using ServiceLib.Models.Entities;
using ServiceLib.Common;
using Xunit;

namespace ServiceLib.Tests;

public class RuleTestMatcherTests
{
    private static string NormalizeRuleSetJson(string json) =>
        json.Replace("\"Domains\"", "\"Domain\"")
            .Replace("\"Protocols\"", "\"Protocol\"")
            .Replace("\"inboundTags\"", "\"InboundTag\"");

    [Fact]
    public void Deserialize_PluralDomains_MapsToDomain()
    {
        var json = "[{\"Domains\":[\"geosite:google\"],\"Ip\":[\"geoip:cn\"],\"OutboundTag\":\"proxy\",\"Enabled\":true}]";
        var rules = JsonUtils.Deserialize<List<RulesItem>>(NormalizeRuleSetJson(json));

        Assert.NotNull(rules);
        Assert.Single(rules);
        Assert.Equal(["geosite:google"], rules![0].Domain);
        Assert.Equal(["geoip:cn"], rules[0].Ip);
        Assert.Equal("proxy", rules[0].OutboundTag);
    }

    [Fact]
    public void Deserialize_SingularDomain_MapsToDomain()
    {
        var json = "[{\"Domain\":[\"geosite:cn\"],\"OutboundTag\":\"direct\"}]";
        var rules = JsonUtils.Deserialize<List<RulesItem>>(NormalizeRuleSetJson(json));

        Assert.NotNull(rules);
        Assert.Single(rules);
        Assert.Equal(["geosite:cn"], rules![0].Domain);
    }

    [Fact]
    public void Deserialize_PluralProtocols_MapsToProtocol()
    {
        var json = "[{\"Protocols\":[\"http\"],\"OutboundTag\":\"proxy\"}]";
        var rules = JsonUtils.Deserialize<List<RulesItem>>(NormalizeRuleSetJson(json));

        Assert.NotNull(rules);
        Assert.Equal(["http"], rules![0].Protocol);
    }

    [Fact]
    public void Deserialize_AllFieldsPresent()
    {
        var json = "[{\"Domains\":[\"geosite:google\"],\"Ip\":[\"geoip:cn\"],\"Protocols\":[\"http\"],\"inboundTags\":[\"socks\"],\"Port\":\"443\",\"Network\":\"tcp\",\"OutboundTag\":\"proxy\",\"Enabled\":true,\"Remarks\":\"test\"}]";
        var rules = JsonUtils.Deserialize<List<RulesItem>>(NormalizeRuleSetJson(json));

        Assert.NotNull(rules);
        var r = rules![0];
        Assert.Equal(["geosite:google"], r.Domain);
        Assert.Equal(["geoip:cn"], r.Ip);
        Assert.Equal(["http"], r.Protocol);
        Assert.Equal(["socks"], r.InboundTag);
        Assert.Equal("443", r.Port);
        Assert.Equal("tcp", r.Network);
        Assert.Equal("proxy", r.OutboundTag);
        Assert.True(r.Enabled);
        Assert.Equal("test", r.Remarks);
    }

    [Fact]
    public void Deserialize_V4Whitelist_RulesParsedCorrectly()
    {
        var json = """
        [
          {"remarks":"阻断udp443","outboundTag":"block","port":"443","network":"udp"},
          {"remarks":"代理Google","outboundTag":"proxy","Domains":["geosite:google"]},
          {"remarks":"绕过局域网IP","outboundTag":"direct","Ip":["geoip:private"]},
          {"remarks":"绕过局域网域名","outboundTag":"direct","Domains":["geosite:private"]},
          {"remarks":"绕过中国IP","outboundTag":"direct","Ip":["geoip:cn"]},
          {"remarks":"绕过中国域名","outboundTag":"direct","Domains":["geosite:cn"]}
        ]
        """;

        var rules = JsonUtils.Deserialize<List<RulesItem>>(NormalizeRuleSetJson(json));

        Assert.NotNull(rules);
        Assert.Equal(6, rules!.Count);

        // P1: 阻断udp443 - port only
        Assert.Null(rules[0].Domain);
        Assert.Equal("443", rules[0].Port);

        // P2: 代理Google - geosite:google
        Assert.Equal(["geosite:google"], rules[1].Domain);
        Assert.Equal("proxy", rules[1].OutboundTag);

        // P6: 绕过中国域名 - geosite:cn
        Assert.Equal(["geosite:cn"], rules[5].Domain);
        Assert.Equal("direct", rules[5].OutboundTag);
    }
}
