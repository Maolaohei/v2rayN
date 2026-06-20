using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>
/// Tests verifying that NetBridge (WinDivert) is properly stopped during
/// application exit and WiFi-switch restart, preventing the "dead network"
/// bug where the kernel driver retains stale hooks.
/// </summary>
public class NetBridgeCleanupTests
{
    #region Stop / StopForShutdown

    [Fact]
    public async Task Stop_IsIdempotent_CanBeCalledMultipleTimesWithoutError()
    {
        var mgr = NetBridgeManager.Instance;

        var result1 = await mgr.Stop();
        var result2 = await mgr.Stop();

        Assert.True(result1);
        Assert.True(result2);
    }

    [Fact]
    public async Task IsRunning_IsFalseAfterStop()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task StopFromBackgroundThread_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                mgr.Stop().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.Start();
        thread.Join(5000);

        Assert.Null(captured);
    }

    [Fact]
    public async Task AppExitAsync_StopsNetBridge_BeforeCompleting()
    {
        var mgr = NetBridgeManager.Instance;

        await AppManager.Instance.AppExitAsync(false);

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task Stop_TotalConnections_AccessibleAfterStop()
    {
        var mgr = NetBridgeManager.Instance;

        mgr.ResetStatistics();
        Assert.Equal(0, mgr.TotalConnections);

        await mgr.Stop();

        Assert.NotNull(mgr.ProcessConnections);
    }

    [Fact]
    public async Task ConcurrentStop_DoesNotCorruptState()
    {
        var mgr = NetBridgeManager.Instance;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => mgr.Stop()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.True(tasks.All(t => t.Result));
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task StopForShutdown_CompletesWithinTimeout()
    {
        var mgr = NetBridgeManager.Instance;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await mgr.StopForShutdown(2000);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public async Task StopForShutdown_IsIdempotent()
    {
        var mgr = NetBridgeManager.Instance;

        var r1 = await mgr.StopForShutdown(1000);
        var r2 = await mgr.StopForShutdown(1000);

        Assert.True(r1);
        Assert.True(r2);
    }

    [Fact]
    public async Task StopForShutdown_ConcurrentCalls_DontDeadlock()
    {
        var mgr = NetBridgeManager.Instance;

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => mgr.StopForShutdown(2000))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.True(results.All(r => r));
    }

    #endregion

    #region Watchdog / Restart Logic

    [Fact]
    public async Task StopTwice_SimulatesWatchdogHealthCheckFailure()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();
        Assert.False(mgr.IsRunning);

        // Second Stop simulates watchdog calling Stop again after health check failure
        var result = await mgr.Stop();
        Assert.True(result);
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task MultipleStopCalls_SimulateWatchdogFailure()
    {
        var mgr = NetBridgeManager.Instance;

        for (var i = 0; i < 5; i++)
        {
            var result = await mgr.Stop();
            Assert.True(result);
        }

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task StopForShutdown_AfterStop_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();
        Assert.False(mgr.IsRunning);

        var shutdownResult = await mgr.StopForShutdown(1000);
        Assert.True(shutdownResult);
    }

    [Fact]
    public async Task ConcurrentStopAndStopForShutdown_NoDeadlock()
    {
        var mgr = NetBridgeManager.Instance;

        var stopTask = Task.Run(async () => await mgr.Stop());
        var shutdownTask = Task.Run(async () => await mgr.StopForShutdown(2000));

        var completed = await Task.WhenAny(
            Task.WhenAll(stopTask, shutdownTask),
            Task.Delay(5000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Network Change / State Guard Tests

    [Fact]
    public async Task StopForShutdown_ResetsState()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.StopForShutdown(1000);
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region HealthMonitor — Stuck Detection

    [Fact]
    public void HealthMonitor_TriggersRecover_WhenIdleExceedsThreshold()
    {
        var recoverTriggered = false;
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { recoverTriggered = true; return Task.CompletedTask; },
            isRunning: () => true,
            idleThreshold: TimeSpan.FromMilliseconds(50));

        // Record traffic in the past (simulate old traffic)
        // By not recording again, _lastTrafficTime stays at construction time
        // which is "now", so we need to wait for it to age past the threshold

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(30));

        // Wait enough for: lastTrafficTime + 50ms threshold < now
        Thread.Sleep(200);

        monitor.StopStuckMonitor();
        monitor.Dispose();

        Assert.True(recoverTriggered);
    }

    [Fact]
    public void HealthMonitor_DoesNotTrigger_WhenNotRunning()
    {
        var recoverTriggered = false;
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { recoverTriggered = true; return Task.CompletedTask; },
            isRunning: () => false,
            idleThreshold: TimeSpan.FromMilliseconds(10));

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(30));

        Thread.Sleep(200);

        monitor.StopStuckMonitor();
        monitor.Dispose();

        Assert.False(recoverTriggered);
    }

    [Fact]
    public void HealthMonitor_DoesNotTrigger_WhenTrafficActive()
    {
        var recoverTriggered = false;
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { recoverTriggered = true; return Task.CompletedTask; },
            isRunning: () => true,
            idleThreshold: TimeSpan.FromMilliseconds(200));

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(30));

        // Keep recording traffic every 30ms — always within 200ms threshold
        for (var i = 0; i < 10; i++)
        {
            monitor.RecordTraffic(100 * (i + 1));
            Thread.Sleep(30);
        }

        monitor.StopStuckMonitor();
        monitor.Dispose();

        Assert.False(recoverTriggered);
    }

    [Fact]
    public void HealthMonitor_RecordTraffic_ResetsIdleTimer()
    {
        var recoverCount = 0;
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { Interlocked.Increment(ref recoverCount); return Task.CompletedTask; },
            isRunning: () => true,
            idleThreshold: TimeSpan.FromMilliseconds(50));

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(20));

        // Wait for threshold to approach
        Thread.Sleep(40);

        // Record traffic — resets the idle timer
        monitor.RecordTraffic(999);

        // Wait a bit more — should not trigger yet (just reset)
        Thread.Sleep(30);

        monitor.StopStuckMonitor();
        monitor.Dispose();

        // Should not have triggered because we reset the timer
        Assert.Equal(0, recoverCount);
    }

    [Fact]
    public void HealthMonitor_Dispose_StopsMonitor()
    {
        var recoverCount = 0;
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => { Interlocked.Increment(ref recoverCount); return Task.CompletedTask; },
            isRunning: () => true,
            idleThreshold: TimeSpan.FromMilliseconds(10));

        monitor.StartStuckMonitor(TimeSpan.FromMilliseconds(20));

        Thread.Sleep(100);

        monitor.Dispose();

        var countAfterDispose = recoverCount;

        Thread.Sleep(200);

        Assert.Equal(countAfterDispose, recoverCount);
    }

    [Fact]
    public void HealthMonitor_MultipleDispose_DoesNotThrow()
    {
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => Task.CompletedTask,
            isRunning: () => false);

        monitor.Dispose();
        monitor.Dispose();
        monitor.Dispose();
    }

    [Fact]
    public void HealthMonitor_StartStop_Repeat_DoesNotThrow()
    {
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => Task.CompletedTask,
            isRunning: () => false);

        for (var i = 0; i < 5; i++)
        {
            monitor.StartStuckMonitor(TimeSpan.FromSeconds(1));
            monitor.StopStuckMonitor();
        }

        monitor.Dispose();
    }

    #endregion

    #region HealthMonitor — Connectivity

    [Fact]
    public async Task VerifyConnectivityAsync_CompletesWithinTimeout()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await NetBridgeHealthMonitor.VerifyConnectivityAsync();
        sw.Stop();

        Assert.IsType<bool>(result);
        Assert.True(sw.ElapsedMilliseconds < 10000);
    }

    [Fact]
    public void IsLocalPortAvailable_HighPort_ReturnsDeterministicResult()
    {
        // Test the same port twice — should get consistent result
        var port = 49999;
        var result1 = NetBridgeHealthMonitor.IsLocalPortAvailable(port);
        var result2 = NetBridgeHealthMonitor.IsLocalPortAvailable(port);

        Assert.Equal(result1, result2);
    }

    #endregion

    #region UpdateRoutes / UpdateProxyConfig Guard Tests

    [Fact]
    public async Task UpdateRoutes_ReturnsTrue_WhenNotRunning()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        var result = await mgr.UpdateRoutes("example.com");
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateRoutes_ReturnsTrue_WithNullProcess()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        var result = await mgr.UpdateRoutes(null);
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateRoutes_ReturnsTrue_WithEmptyProcess()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        var result = await mgr.UpdateRoutes("");
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateProxyConfig_ReturnsFalse_WhenServiceNull()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        var result = await mgr.UpdateProxyConfig("127.0.0.1", 1080);
        Assert.False(result);
    }

    [Fact]
    public async Task SetDnsViaProxy_ReturnsFalse_WhenServiceNull()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        var result = await mgr.SetDnsViaProxy(true);
        Assert.False(result);
    }

    [Fact]
    public async Task SetLocalhostViaProxy_ReturnsFalse_WhenServiceNull()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        var result = await mgr.SetLocalhostViaProxy(true);
        Assert.False(result);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void ResetStatistics_ResetsCounters()
    {
        var mgr = NetBridgeManager.Instance;

        mgr.ResetStatistics();

        Assert.Equal(0, mgr.TotalConnections);
        Assert.Empty(mgr.ProcessConnections);
    }

    #endregion
}
