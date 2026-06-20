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
    public void Stop_DoesNotThrow_WhenCalledFromBackgroundThread()
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

    // ==================== HealthMonitor Tests ====================

    [Fact]
    public void HealthMonitor_Constructor_DoesNotThrow()
    {
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => Task.CompletedTask,
            isRunning: () => false);

        monitor.Dispose();
    }

    [Fact]
    public void HealthMonitor_StartStop_DoesNotThrow()
    {
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => Task.CompletedTask,
            isRunning: () => false);

        monitor.StartStuckMonitor(TimeSpan.FromSeconds(1));
        monitor.StopStuckMonitor();

        monitor.Dispose();
    }

    [Fact]
    public void HealthMonitor_Dispose_CanBeCalledMultipleTimes()
    {
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => Task.CompletedTask,
            isRunning: () => false);

        monitor.Dispose();
        monitor.Dispose(); // Must not throw
    }

    [Fact]
    public void HealthMonitor_RecordTraffic_DoesNotThrow()
    {
        var monitor = new NetBridgeHealthMonitor(
            forceRecover: () => Task.CompletedTask,
            isRunning: () => false);

        monitor.RecordTraffic(100);
        monitor.RecordTraffic(200);

        monitor.Dispose();
    }

    [Fact]
    public async Task VerifyConnectivityAsync_DoesNotThrow()
    {
        var result = await NetBridgeHealthMonitor.VerifyConnectivityAsync();
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsLocalPortAvailable_DoesNotThrow()
    {
        var result = NetBridgeHealthMonitor.IsLocalPortAvailable(1);
        Assert.IsType<bool>(result);
    }
}
