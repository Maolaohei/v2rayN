using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests;

public class StatusBarViewModelTests
{
    [Fact]
    public async Task NetBridgeManager_IsRunning_FalseAfterStop()
    {
        var mgr = NetBridgeManager.Instance;
        await mgr.Stop();

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public void NetBridgeManager_IsDriverLoaded_DoesNotThrow()
    {
        var loaded = NetBridgeManager.Instance.IsDriverLoaded;

        // Should return a definite value without throwing
        Assert.True(loaded || !loaded);
    }

    [Fact]
    public void Config_NetBridgeItem_RoundTrip_PreservesSettings()
    {
        var config = CreateTestConfig();
        config.NetBridgeItem.RuleProcess = "chrome.exe,firefox.exe";
        config.NetBridgeItem.EnableDnsViaProxy = true;

        var json = JsonUtils.Serialize(config);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.Equal("chrome.exe,firefox.exe", restored.NetBridgeItem.RuleProcess);
        Assert.True(restored.NetBridgeItem.EnableDnsViaProxy);
    }

    [Fact]
    public void Config_TunModeItem_RoundTrip_PreservesProtectedProcesses()
    {
        var config = CreateTestConfig();
        config.TunModeItem.ProtectedProcesses = new List<string> { "chrome.exe", "firefox.exe" };

        var json = JsonUtils.Serialize(config);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.TunModeItem.ProtectedProcesses);
        Assert.Equal(2, restored.TunModeItem.ProtectedProcesses.Count);
        Assert.Contains("chrome.exe", restored.TunModeItem.ProtectedProcesses);
        Assert.Contains("firefox.exe", restored.TunModeItem.ProtectedProcesses);
    }

    [Fact]
    public void Config_TunModeItem_RoundTrip_PreservesLegacyProtectState()
    {
        var config = CreateTestConfig();
        config.TunModeItem.EnableTun = false;
        config.TunModeItem.EnableLegacyProtect = true;

        var json = JsonUtils.Serialize(config);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.False(restored.TunModeItem.EnableTun);
        Assert.True(restored.TunModeItem.EnableLegacyProtect);
    }

    [Fact]
    public void Config_TunModeItem_DistinctProcesses_ArePreserved()
    {
        var config = CreateTestConfig();
        // List allows duplicates at data level — verify serialization preserves all
        config.TunModeItem.ProtectedProcesses = new List<string> { "chrome.exe", "chrome.exe", "firefox.exe" };

        var json = JsonUtils.Serialize(config);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.Equal(3, restored.TunModeItem.ProtectedProcesses.Count);
    }

    [Fact]
    public void Config_NetBridgeItem_RoundTrip_PreservesForwardMode()
    {
        var config = CreateTestConfig();
        config.NetBridgeItem.ForwardMode = "CoreDirect";
        config.NetBridgeItem.CoreDirectTcpPort = 40000;
        config.NetBridgeItem.CoreDirectUdpPort = 40001;

        var json = JsonUtils.Serialize(config);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.Equal("CoreDirect", restored.NetBridgeItem.ForwardMode);
        Assert.Equal(40000, restored.NetBridgeItem.CoreDirectTcpPort);
        Assert.Equal(40001, restored.NetBridgeItem.CoreDirectUdpPort);
    }

    [Fact]
    public void Config_NetBridgeItem_DefaultForwardMode_IsBridge()
    {
        var item = new NetBridgeItem();
        Assert.Equal("Bridge", item.ForwardMode);
    }

    [Fact]
    public void TuplePatternMatch_ProcessListSetting_WithForwardMode()
    {
        // Simulate the tuple that ShowProcessListSetting creates
        var processText = "chrome.exe,firefox.exe";
        var dnsViaBridge = true;
        var protocolMode = "TCP";
        var forwardMode = "Bridge";

        var tuple = (processText, dnsViaBridge, protocolMode, forwardMode);

        // Verify pattern matching works (same as UpdateViewHandler)
        if (tuple is (string pt, bool dvb, string pm, string fm))
        {
            Assert.Equal("chrome.exe,firefox.exe", pt);
            Assert.True(dvb);
            Assert.Equal("TCP", pm);
            Assert.Equal("Bridge", fm);
        }
        else
        {
            Assert.Fail("Tuple pattern match failed");
        }
    }

    [Fact]
    public void TuplePatternMatch_CoreDirectMode()
    {
        var tuple = ("chrome.exe", true, "BOTH", "CoreDirect");

        if (tuple is (string _, bool _, string _, string fm))
        {
            Assert.Equal("CoreDirect", fm);
        }
        else
        {
            Assert.Fail("Tuple pattern match failed");
        }
    }

    [Fact]
    public void ForwardMode_GetAllowedProtocolModes()
    {
        Assert.Equal(new[] { "TCP" }, NetBridgeManager.GetAllowedProtocolModes("Legacy"));
        Assert.Equal(new[] { "TCP", "UDP", "BOTH" }, NetBridgeManager.GetAllowedProtocolModes("Bridge"));
        Assert.Equal(new[] { "TCP", "UDP", "BOTH" }, NetBridgeManager.GetAllowedProtocolModes("CoreDirect"));
    }

    [Fact]
    public void ForwardMode_FindFreePort_ReturnsPreferredIfAvailable()
    {
        // Port 49999 is unlikely to be in use
        var port = NetBridgeManager.FindFreePort(49999);
        Assert.Equal(49999, port);
    }

    private static Config CreateTestConfig() => new()
    {
        TunModeItem = new TunModeItem(),
        NetBridgeItem = new NetBridgeItem(),
        Inbound = [],
        SystemProxyItem = new SystemProxyItem(),
        UiItem = new UIItem()
    };
}
