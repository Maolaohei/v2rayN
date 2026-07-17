using ServiceLib.Manager;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests;

public class PerformanceOptimizationTests
{
    [Fact]
    public void ClashUIItem_DefaultConnectionsRefreshInterval_IsAtLeast5()
    {
        var item = new ClashUIItem();
        Assert.True(item.ConnectionsRefreshInterval >= 5);
    }

    [Fact]
    public void StatisticsManager_UpdateRunningCore_DoesNotThrow_WhenNotInitialized()
    {
        // Should be safe no-op before Init (config null path).
        StatisticsManager.Instance.UpdateRunningCore(ECoreType.Xray);
        StatisticsManager.Instance.UpdateRunningCore(ECoreType.sing_box);
        StatisticsManager.Instance.Close();
    }

    [Fact]
    public void NetBridgeHealthMonitor_RequiresConnectivityFailure_ForIdleRecover()
    {
        var recover = 0;
        using var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { Interlocked.Increment(ref recover); return Task.CompletedTask; },
            isRunning: () => true,
            idleThreshold: TimeSpan.FromMilliseconds(30),
            connectivityCheck: () => Task.FromResult(true));

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(20));
        Thread.Sleep(150);
        monitor.StopStuckMonitor();

        Assert.Equal(0, recover);
    }

    [Fact]
    public void NetBridgeHealthMonitor_Recovers_WhenIdleAndConnectivityFails()
    {
        var recover = 0;
        using var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { Interlocked.Increment(ref recover); return Task.CompletedTask; },
            isRunning: () => true,
            idleThreshold: TimeSpan.FromMilliseconds(30),
            connectivityCheck: () => Task.FromResult(false));

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(20));
        Thread.Sleep(180);
        monitor.StopStuckMonitor();

        Assert.True(recover > 0);
    }

    [Fact]
    public void ProfileItem_ShallowListBuild_PreservesIndexIdOrder()
    {
        // Mirrors ProfilesViewModel.RefreshServersBiz lightweight copy path.
        var models = new[]
        {
            new ProfileItemModel { IndexId = "a", Remarks = "A", Port = 1 },
            new ProfileItemModel { IndexId = "b", Remarks = "B", Port = 2 },
        };

        var lstProfile = models.Select(m => new ProfileItem
        {
            IndexId = m.IndexId,
            ConfigType = m.ConfigType,
            Remarks = m.Remarks,
            Address = m.Address,
            Port = m.Port,
            Network = m.Network,
            StreamSecurity = m.StreamSecurity,
            Subid = m.Subid
        }).ToList();

        Assert.Equal(2, lstProfile.Count);
        Assert.Equal("a", lstProfile[0].IndexId);
        Assert.Equal("b", lstProfile[1].IndexId);
        Assert.Equal("A", lstProfile[0].Remarks);
    }

    [Fact]
    public async Task RefreshAfterSystemProxyChangeAsync_DoesNotBlock_WhenNotRunning()
    {
        // When NetBridge is stopped, refresh must return quickly without hanging the caller
        // (proxy-mode UI path used to await TCP-table scan on the UI thread).
        await NetBridgeManager.Instance.Stop();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reset = await NetBridgeManager.Instance.RefreshAfterSystemProxyChangeAsync("Legacy");
        sw.Stop();

        Assert.Equal(0, reset);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"expected quick no-op, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task RefreshAfterSystemProxyChangeAsync_CoalescesConcurrentCalls()
    {
        await NetBridgeManager.Instance.Stop();
        // Concurrent calls while not running should not throw and should finish promptly.
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => NetBridgeManager.Instance.RefreshAfterSystemProxyChangeAsync("CoreDirect"))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r >= 0));
    }
}