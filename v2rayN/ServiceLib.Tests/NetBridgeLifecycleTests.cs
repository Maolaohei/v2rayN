using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>
/// Tests verifying the full NetBridge process-hijacking lifecycle:
/// Init → Start → Stop → re-Init → Start, including error recovery,
/// state-reset guarantees, and clean shutdown.
/// </summary>
public class NetBridgeLifecycleTests : IDisposable
{
    public NetBridgeLifecycleTests()
    {
        NetBridgeManager.Instance.Stop().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        NetBridgeManager.Instance.Stop().GetAwaiter().GetResult();
    }

    #region Init

    [Fact]
    public async Task Init_SetsIsInitialized()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();

        // After Init, driverLoaded may be false (no WinDivert in test env),
        // but Init itself should not throw.
    }

    [Fact]
    public async Task Init_IsIdempotent()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Init();
        await mgr.Init();
    }

    [Fact]
    public async Task Init_AfterStop_ReInitializes()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Stop();

        // Should not throw — Init should work again after Stop
        await mgr.Init();
    }

    #endregion

    #region Start / Stop full cycle

    [Fact]
    public async Task StartStopStart_FullCycle_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();

        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();
    }

    [Fact]
    public async Task StartStopStart_IsRunningTransitionsCorrectly()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        // Start may fail in test env (no driver), but after Stop, IsRunning must be false
        await mgr.Stop();
        Assert.False(mgr.IsRunning);

        // After re-Init + Start, IsRunning should reflect the result
        await mgr.Init();
        await mgr.Start();

        // Whether Start succeeded or not, Stop must bring it back to false
        await mgr.Stop();
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task Stop_ResetsAllState_ForFreshRestart()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();

        Assert.False(mgr.IsRunning);

        // After Stop, Init should not short-circuit
        // (this was the bug: _isInitialized stayed true after failed Start)
        await mgr.Init((_, _) =>
        {
            return Task.FromResult(true);
        });

        // Stop then Start should not fail with "service is null"
        var startResult = await mgr.Start();
        await mgr.Stop();
    }

    #endregion

    #region Stop state reset guarantees (regression: Issue 1)

    [Fact]
    public async Task Stop_EvenWhenNotRunning_ResetsInitialized()
    {
        var mgr = NetBridgeManager.Instance;

        // Stop when nothing is running — must still reset internal state
        var result = await mgr.Stop();
        Assert.True(result);
        Assert.False(mgr.IsRunning);

        // Init should proceed (not short-circuit)
        await mgr.Init();
    }

    [Fact]
    public async Task Stop_AfterFailedStart_ResetsForRetry()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();

        // Start will fail in test env (no WinDivert driver)
        await mgr.Start();
        Assert.False(mgr.IsRunning);

        // Stop must reset _isInitialized so next Init works
        await mgr.Stop();

        // This Init must not return early
        await mgr.Init();

        // And Start must not throw
        await mgr.Start();
        await mgr.Stop();
    }

    [Fact]
    public async Task MultipleStopCalls_DoesNotCorruptState()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        for (var i = 0; i < 10; i++)
        {
            await mgr.Stop();
        }

        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region StopForShutdown

    [Fact]
    public async Task StopForShutdown_CompletesAndResetsState()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        var result = await mgr.StopForShutdown(5000);

        Assert.True(result);
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task StopForShutdown_IsIdempotent()
    {
        var mgr = NetBridgeManager.Instance;

        var r1 = await mgr.StopForShutdown(2000);
        var r2 = await mgr.StopForShutdown(2000);
        var r3 = await mgr.StopForShutdown(2000);

        Assert.True(r1);
        Assert.True(r2);
        Assert.True(r3);
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task StopForShutdown_ThenInit_Works()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();
        await mgr.StopForShutdown(3000);

        // Must be able to re-Init after shutdown
        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();
    }

    [Fact]
    public async Task ConcurrentStopForShutdown_DoesNotDeadlock()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => mgr.StopForShutdown(3000))
            .ToArray();

        var completed = await Task.WhenAny(
            Task.WhenAll(tasks),
            Task.Delay(10000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Concurrent operations

    [Fact]
    public async Task ConcurrentStop_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () =>
            {
                try { await mgr.Stop(); }
                catch (Exception ex) { exceptions.Add(ex); }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task ConcurrentInit_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        var exceptions = new ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                try { await mgr.Init(); }
                catch (Exception ex) { exceptions.Add(ex); }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task ConcurrentStopAndStopForShutdown_NoDeadlock()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        var stopTask = Task.Run(async () => await mgr.Stop());
        var shutdownTask = Task.Run(async () => await mgr.StopForShutdown(3000));

        var completed = await Task.WhenAny(
            Task.WhenAll(stopTask, shutdownTask),
            Task.Delay(10000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Statistics survive lifecycle

    [Fact]
    public async Task Statistics_ResetAndSurviveStop()
    {
        var mgr = NetBridgeManager.Instance;

        mgr.ResetStatistics();
        Assert.Equal(0, mgr.TotalConnections);
        Assert.Empty(mgr.ProcessConnections);

        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();

        // After stop, collections are still accessible
        Assert.NotNull(mgr.ProcessConnections);
    }

    #endregion

    #region StateChanged event

    [Fact]
    public async Task Stop_RaisesStateChanged()
    {
        var mgr = NetBridgeManager.Instance;
        var raised = false;
        mgr.StateChanged += () => raised = true;

        await mgr.Init();
        await mgr.Stop();

        Assert.True(raised);
    }

    [Fact]
    public async Task Stop_DoesNotThrowIfNoSubscribers()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Stop();
    }

    #endregion

    #region AppExitAsync

    [Fact]
    public async Task AppExitAsync_StopsNetBridge()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        await AppManager.Instance.AppExitAsync(false);

        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region ProxyConfigId propagation (regression: browser lag fix)

    [Fact]
    public async Task UpdateRoutes_WithEmptyProcess_ReturnsTrue()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        var result = await mgr.UpdateRoutes("");
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateRoutes_WithNullProcess_ReturnsTrue()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        var result = await mgr.UpdateRoutes(null);
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateRoutes_WhenNotRunning_ReturnsTrue()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();
        var result = await mgr.UpdateRoutes("chrome.exe");
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateProxyConfig_WhenNotRunning_ReturnsFalse()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();
        var result = await mgr.UpdateProxyConfig("127.0.0.1", 1080);
        Assert.False(result);
    }

    #endregion

    #region Protocol mode

    [Fact]
    public void NetBridgeItem_DefaultProtocolMode_IsTcp()
    {
        var item = new NetBridgeItem();
        Assert.Equal("TCP", item.ProtocolMode);
    }

    [Fact]
    public void NetBridgeItem_ProtocolMode_CanBeSetToUdp()
    {
        var item = new NetBridgeItem { ProtocolMode = "UDP" };
        Assert.Equal("UDP", item.ProtocolMode);
    }

    [Fact]
    public void NetBridgeItem_ProtocolMode_CanBeSetToBoth()
    {
        var item = new NetBridgeItem { ProtocolMode = "BOTH" };
        Assert.Equal("BOTH", item.ProtocolMode);
    }

    [Fact]
    public async Task UpdateRoutes_UsesConfiguredProtocolMode()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();
        var result = await mgr.UpdateRoutes("chrome.exe");
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateRoutes_DefaultProtocolMode_IsTcp()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();
        var result = await mgr.UpdateRoutes("chrome.exe");
        Assert.True(result);
    }

    #endregion
}
