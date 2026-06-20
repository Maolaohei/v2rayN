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

    private static Config CreateTestConfig() => new()
    {
        TunModeItem = new TunModeItem(),
        NetBridgeItem = new NetBridgeItem(),
        Inbound = [],
        SystemProxyItem = new SystemProxyItem(),
        UiItem = new UIItem()
    };
}
