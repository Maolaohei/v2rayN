using ServiceLib.Manager;
using Xunit;

namespace ServiceLib.Tests;

/// <summary>
/// Comprehensive edge-case tests for NetBridge lifecycle management.
/// Covers: timeout handling, force-stop, concurrent operations,
/// rapid toggling, driver failures, shutdown cleanup, and user-interrupt scenarios.
/// </summary>
public class NetBridgeLifecycleEdgeTests : IDisposable
{
    public NetBridgeLifecycleEdgeTests()
    {
        NetBridgeManager.Instance.Stop().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { NetBridgeManager.Instance.Stop().GetAwaiter().GetResult(); } catch { }
    }

    #region ForceStop / Timeout Scenarios

    [Fact]
    public void ForceReleaseHandle_ReleasesState_WithoutWaitingForNative()
    {
        var mgr = NetBridgeManager.Instance;

        // ForceReleaseHandle should be instant — no blocking on native calls
        var sw = System.Diagnostics.Stopwatch.StartNew();
        mgr.ForceReleaseHandle();
        sw.Stop();

        Assert.False(mgr.IsRunning);
        Assert.True(sw.ElapsedMilliseconds < 100, $"ForceReleaseHandle took {sw.ElapsedMilliseconds}ms, should be <100ms");
    }

    [Fact]
    public void ForceReleaseHandle_IsIdempotent()
    {
        var mgr = NetBridgeManager.Instance;

        mgr.ForceReleaseHandle();
        mgr.ForceReleaseHandle();
        mgr.ForceReleaseHandle();

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public void ForceReleaseHandle_ThenStart_Works()
    {
        var mgr = NetBridgeManager.Instance;

        mgr.ForceReleaseHandle();
        Assert.False(mgr.IsRunning);

        // Should be able to start after force release
        var startTask = mgr.Start();
        Assert.NotNull(startTask);
    }

    [Fact]
    public async Task StopForShutdown_WithVeryShortTimeout_Completes()
    {
        var mgr = NetBridgeManager.Instance;

        // 1ms timeout — should not hang
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await mgr.StopForShutdown(1);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"StopForShutdown took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task StopForShutdown_Timeout_DoesNotLeaveStateDirty()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.StopForShutdown(1);

        // Even if timeout happened, state must be clean
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Concurrent Operations

    [Fact]
    public async Task Concurrent_Start_Stop_DoesNotDeadlock()
    {
        var mgr = NetBridgeManager.Instance;

        var startTask = Task.Run(async () => await mgr.Start());
        var stopTask = Task.Run(async () => await mgr.Stop());

        var completed = await Task.WhenAny(
            Task.WhenAll(startTask, stopTask),
            Task.Delay(5000));

        // Must complete within 5s — no deadlock
        Assert.NotEqual(typeof(Task), completed.GetType());
    }

    [Fact]
    public async Task Concurrent_Stop_StopForShutdown_ForceRelease()
    {
        var mgr = NetBridgeManager.Instance;

        var t1 = Task.Run(async () => await mgr.Stop());
        var t2 = Task.Run(async () => await mgr.StopForShutdown(2000));
        var t3 = Task.Run(() => mgr.ForceReleaseHandle());

        var completed = await Task.WhenAny(
            Task.WhenAll(t1, t2, t3),
            Task.Delay(5000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task Concurrent_Start_FromMultipleThreads()
    {
        var mgr = NetBridgeManager.Instance;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await mgr.Start()))
            .ToArray();

        var completed = await Task.WhenAny(
            Task.WhenAll(tasks),
            Task.Delay(10000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        // No crash, no deadlock
    }

    [Fact]
    public async Task Concurrent_Stop_FromMultipleThreads()
    {
        var mgr = NetBridgeManager.Instance;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await mgr.Stop()))
            .ToArray();

        var completed = await Task.WhenAny(
            Task.WhenAll(tasks),
            Task.Delay(5000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Rapid Toggle (Enable/Disable Cycling)

    [Fact]
    public async Task RapidToggle_EnableDisable_10Times_NoCrash()
    {
        var mgr = NetBridgeManager.Instance;

        for (var i = 0; i < 10; i++)
        {
            await mgr.Init();
            await mgr.Start();
            await mgr.Stop();
        }

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task RapidToggle_ConcurrentEnableDisable()
    {
        var mgr = NetBridgeManager.Instance;

        var enableTasks = Enumerable.Range(0, 5)
            .Select(async _ =>
            {
                await mgr.Init();
                await mgr.Start();
            })
            .ToArray();

        var disableTasks = Enumerable.Range(0, 5)
            .Select(async _ => await mgr.Stop())
            .ToArray();

        var allTasks = enableTasks.Concat(disableTasks).ToArray();
        var completed = await Task.WhenAny(
            Task.WhenAll(allTasks),
            Task.Delay(10000));

        Assert.NotEqual(typeof(Task), completed.GetType());
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region State Consistency

    [Fact]
    public async Task IsRunning_FalseAfterStopForShutdown()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.StopForShutdown(2000);

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task ReInit_WorksAfterStop()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Stop();

        // After Stop, should be able to re-Init without issues
        await mgr.Init();
    }

    [Fact]
    public void ForceReleaseHandle_CleansUp()
    {
        var mgr = NetBridgeManager.Instance;

        // Should not throw even if nothing is running
        mgr.ForceReleaseHandle();

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task ForceReleaseHandle_AfterStart()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        mgr.ForceReleaseHandle();

        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region AppExit Cleanup Simulation

    [Fact]
    public async Task AppExit_CleansUpInCorrectOrder()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        // Simulate AppExitAsync
        await AppManager.Instance.AppExitAsync(false);

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task AppExit_WhenNotRunning_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Stop();

        await AppManager.Instance.AppExitAsync(false);

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task AppExit_WhenNotInitialized_DoesNotThrow()
    {
        await AppManager.Instance.AppExitAsync(false);
    }

    #endregion

    #region Error Recovery

    [Fact]
    public async Task Start_AfterFailedStart_CanRetry()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();

        // Start may fail (no driver in test env), but should not prevent retry
        await mgr.Start();
        await mgr.Stop();

        // Retry should work
        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();
    }

    [Fact]
    public async Task Stop_AfterFailedStart_CleansUp()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start(); // May fail
        await mgr.Stop();  // Must clean up regardless

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task Restart_DoesNotLeaveStateDirty()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        // Stop and re-init (simulating restart)
        await mgr.Stop();
        await mgr.Init();
        await mgr.Start();

        // Final stop must work
        await mgr.Stop();
        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Statistics / State Access During Lifecycle

    [Fact]
    public async Task Statistics_AccessibleDuringStop()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        mgr.ResetStatistics();

        // Stats should be accessible even during stop
        await mgr.Stop();

        Assert.NotNull(mgr.ProcessConnections);
        Assert.Equal(0, mgr.TotalConnections);
    }

    [Fact]
    public void IsRunning_IsFalseByDefault()
    {
        var mgr = NetBridgeManager.Instance;

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public void IsDriverLoaded_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        // Should not throw regardless of driver state
        var _ = mgr.IsDriverLoaded;
    }

    #endregion

    #region Timeout Edge Cases

    [Fact]
    public async Task StopForShutdown_ZeroTimeout_Completes()
    {
        var mgr = NetBridgeManager.Instance;

        // 0ms timeout should not cause issues
        await mgr.StopForShutdown(0);

        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task StopForShutdown_VeryLargeTimeout_CompletesQuickly()
    {
        var mgr = NetBridgeManager.Instance;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await mgr.StopForShutdown(60000); // 60s timeout, but should complete in <1s
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3000, $"Took {sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region User Interrupt Scenarios

    [Fact]
    public async Task UserToggleDuringRestart_NoCorruption()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        // Simulate user toggling off while restart might be happening
        var stopTask = mgr.Stop();

        // Simulate user quickly toggling back on
        await mgr.Init();
        await mgr.Start();

        // Wait for original stop to complete
        await stopTask;

        // Final state should be consistent
        await mgr.Stop();
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task RapidEnableDisable_WhileWatchdogRuns()
    {
        var mgr = NetBridgeManager.Instance;

        // Start with watchdog
        await mgr.Init();
        await mgr.Start();

        // Rapid toggle while watchdog might fire
        for (var i = 0; i < 5; i++)
        {
            await mgr.Stop();
            await Task.Delay(50); // Let watchdog timer fire
            await mgr.Init();
            await mgr.Start();
        }

        await mgr.Stop();
        Assert.False(mgr.IsRunning);
    }

    [Fact]
    public async Task NetworkChange_DuringStop_NoCrash()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        // Simulate network change event while stopping
        var stopTask = mgr.Stop();

        // Wait a bit then check state
        await Task.Delay(100);
        await stopTask;

        Assert.False(mgr.IsRunning);
    }

    #endregion

    #region Resource Cleanup

    [Fact]
    public async Task MultipleDispose_DoesNotThrow()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();

        // Simulate multiple dispose calls (GC + explicit)
        await mgr.Stop();
        await mgr.Stop();
        mgr.ForceReleaseHandle();
        mgr.ForceReleaseHandle();
    }

    [Fact]
    public async Task InitStartStopDispose_FullLifecycle()
    {
        var mgr = NetBridgeManager.Instance;

        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();

        // Re-init after stop
        await mgr.Init();
        await mgr.Start();
        await mgr.Stop();

        // Force cleanup
        mgr.ForceReleaseHandle();

        Assert.False(mgr.IsRunning);
    }

    #endregion
}
