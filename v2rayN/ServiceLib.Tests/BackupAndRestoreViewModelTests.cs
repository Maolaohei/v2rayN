using Xunit;

namespace ServiceLib.Tests;

public class BackupAndRestoreViewModelTests
{
    [Fact]
    public void Config_Restore_ShouldPreserveProcessHijackSettings()
    {
        var original = new Config
        {
            TunModeItem = new TunModeItem
            {
                EnableTun = false,
                EnableLegacyProtect = true,
                ProtectedProcesses = new List<string> { "chrome.exe", "firefox.exe", "code.exe" }
            },
            NetBridgeItem = new NetBridgeItem
            {
                RuleProcess = "chrome.exe,firefox.exe,code.exe",
                EnableDnsViaProxy = false
            },
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        var json = JsonUtils.Serialize(original);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.True(restored.TunModeItem.EnableLegacyProtect);
        Assert.False(restored.TunModeItem.EnableTun);
        Assert.Equal(3, restored.TunModeItem.ProtectedProcesses.Count);
        Assert.Contains("chrome.exe", restored.TunModeItem.ProtectedProcesses);
        Assert.Contains("firefox.exe", restored.TunModeItem.ProtectedProcesses);
        Assert.Contains("code.exe", restored.TunModeItem.ProtectedProcesses);
        Assert.Equal("chrome.exe,firefox.exe,code.exe", restored.NetBridgeItem.RuleProcess);
        Assert.False(restored.NetBridgeItem.EnableDnsViaProxy);
    }

    [Fact]
    public void Config_Restore_EmptyProcessHijackSettings_ShouldUseDefaults()
    {
        var original = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        var json = JsonUtils.Serialize(original);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.False(restored.TunModeItem.EnableLegacyProtect);
        Assert.Null(restored.TunModeItem.ProtectedProcesses);
        Assert.Null(restored.NetBridgeItem.RuleProcess);
        Assert.False(restored.NetBridgeItem.EnableDnsViaProxy);
    }

    [Fact]
    public void Config_NetBridgeItem_DnsViaProxy_RoundTrip()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem
            {
                RuleProcess = "notepad.exe",
                EnableDnsViaProxy = true
            },
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        var json = JsonUtils.Serialize(config);
        var restored = JsonUtils.Deserialize<Config>(json);

        Assert.NotNull(restored);
        Assert.True(restored.NetBridgeItem.EnableDnsViaProxy);
        Assert.Equal("notepad.exe", restored.NetBridgeItem.RuleProcess);
    }
}
