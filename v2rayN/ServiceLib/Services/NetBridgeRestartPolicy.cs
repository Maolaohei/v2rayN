namespace ServiceLib.Services;

/// <summary>
/// Shared restart gating for NetBridge to reduce false restarts during core switch / transient net blips.
/// NetBridgeManager should consult this when Manager sources are writable again.
/// </summary>
public static class NetBridgeRestartPolicy
{
    private static long _lastRestartUtcTicks;
    private static int _consecutiveFailures;
    private const int MinRestartIntervalSeconds = 20;
    private const int FailuresBeforeRestart = 2;

    public static bool ShouldRestart(bool healthOk, bool connectivityOk, bool coreReady)
    {
        if (!coreReady)
        {
            // Core not listening yet — do not treat as NetBridge death.
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return false;
        }

        if (healthOk && connectivityOk)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return false;
        }

        var fails = Interlocked.Increment(ref _consecutiveFailures);
        if (fails < FailuresBeforeRestart)
            return false;

        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastRestartUtcTicks);
        if (last > 0 && new TimeSpan(now - last).TotalSeconds < MinRestartIntervalSeconds)
            return false;

        Interlocked.Exchange(ref _lastRestartUtcTicks, now);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        return true;
    }

    public static void MarkRestarted()
    {
        Interlocked.Exchange(ref _lastRestartUtcTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    public static bool IsCoreReady(int socksPort)
    {
        try
        {
            // Port occupied => core (or something) is listening on expected socks port.
            return !Manager.NetBridgeHealthMonitor.IsLocalPortAvailable(socksPort);
        }
        catch
        {
            return false;
        }
    }
}
