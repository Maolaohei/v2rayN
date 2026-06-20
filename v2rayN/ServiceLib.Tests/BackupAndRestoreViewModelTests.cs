using ServiceLib.ViewModels;
using Xunit;

namespace ServiceLib.Tests;

public class BackupAndRestoreViewModelTests
{
    [Fact]
    public void Config_Backup_ShouldIncludeNetBridgeItem()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem
            {
                RuleProcess = "chrome.exe,firefox.exe,msedge.exe",
                EnableDnsViaProxy = true
            },
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        Assert.Equal("chrome.exe,firefox.exe,msedge.exe", config.NetBridgeItem.RuleProcess);
        Assert.True(config.NetBridgeItem.EnableDnsViaProxy);
    }

    [Fact]
    public void Config_Backup_ShouldIncludeTunModeLegacyProtect()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem
            {
                EnableTun = false,
                EnableLegacyProtect = true,
                ProtectedProcesses = new List<string> { "chrome.exe", "firefox.exe" }
            },
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        Assert.True(config.TunModeItem.EnableLegacyProtect);
        Assert.False(config.TunModeItem.EnableTun);
        Assert.NotNull(config.TunModeItem.ProtectedProcesses);
        Assert.Equal(2, config.TunModeItem.ProtectedProcesses.Count);
    }

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
    public void Config_LegacyProtectAndTun_ShouldNotCoexist()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem
            {
                EnableTun = true,
                EnableLegacyProtect = false
            },
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        config.TunModeItem.EnableLegacyProtect = true;
        config.TunModeItem.EnableTun = false;

        Assert.True(config.TunModeItem.EnableLegacyProtect);
        Assert.False(config.TunModeItem.EnableTun);
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
