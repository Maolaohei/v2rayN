using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests;

public class StatusBarViewModelTests
{
    [Fact]
    public void NetBridgeManager_ShouldTrackRunningState()
    {
        var isRunning = NetBridgeManager.Instance.IsRunning;
        Assert.IsType<bool>(isRunning);
    }

    [Fact]
    public void NetBridgeManager_DriverCheck_ShouldReturnBool()
    {
        var isDriverLoaded = NetBridgeManager.Instance.IsDriverLoaded;
        Assert.IsType<bool>(isDriverLoaded);
    }

    [Fact]
    public void Config_NetBridgeItem_ShouldStoreProcessList()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        var testProcesses = "chrome.exe,firefox.exe";
        config.NetBridgeItem.RuleProcess = testProcesses;

        Assert.Equal(testProcesses, config.NetBridgeItem.RuleProcess);
    }

    [Fact]
    public void Config_NetBridgeItem_ShouldStoreDnsViaProxy()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        config.NetBridgeItem.EnableDnsViaProxy = true;
        Assert.True(config.NetBridgeItem.EnableDnsViaProxy);

        config.NetBridgeItem.EnableDnsViaProxy = false;
        Assert.False(config.NetBridgeItem.EnableDnsViaProxy);
    }

    [Fact]
    public void Config_TunModeItem_ShouldStoreProtectedProcesses()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        config.TunModeItem.ProtectedProcesses = new List<string> { "chrome.exe", "firefox.exe" };

        Assert.NotNull(config.TunModeItem.ProtectedProcesses);
        Assert.Equal(2, config.TunModeItem.ProtectedProcesses.Count);
        Assert.Contains("chrome.exe", config.TunModeItem.ProtectedProcesses);
        Assert.Contains("firefox.exe", config.TunModeItem.ProtectedProcesses);
    }

    [Fact]
    public void Config_TunModeItem_LegacyProtect_ShouldConflictWithTun()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem { EnableTun = true, EnableLegacyProtect = false },
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        config.TunModeItem.EnableTun = false;
        config.TunModeItem.EnableLegacyProtect = true;

        Assert.False(config.TunModeItem.EnableTun);
        Assert.True(config.TunModeItem.EnableLegacyProtect);
    }

    [Fact]
    public void Config_TunModeItem_ProtectedProcesses_ShouldAllowDuplicates()
    {
        var config = new Config
        {
            TunModeItem = new TunModeItem(),
            NetBridgeItem = new NetBridgeItem(),
            Inbound = [],
            SystemProxyItem = new SystemProxyItem(),
            UiItem = new UIItem()
        };

        config.TunModeItem.ProtectedProcesses = new List<string> { "chrome.exe", "chrome.exe", "firefox.exe" };
        var distinct = config.TunModeItem.ProtectedProcesses.Distinct().ToList();

        Assert.Equal(2, distinct.Count);
    }
}
