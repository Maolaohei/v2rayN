using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ServiceLib.Manager;

/// <summary>
/// Lightweight health monitor for NetBridge (WinDivert).
/// Detects stuck states and verifies network connectivity.
/// </summary>
public sealed class NetBridgeHealthMonitor : IDisposable
{
    private readonly Func<Task> _forceRecover;
    private readonly Func<bool> _isRunning;
    private readonly Func<string, Task>? _log;
    private readonly TimeSpan _idleThreshold;
    private System.Threading.Timer? _stuckTimer;
    private int _checkRunning;
    private long _lastTrafficBytes;
    private long _lastTrafficTimeUtc;

    public NetBridgeHealthMonitor(Func<Task> forceRecover, Func<bool> isRunning, Func<string, Task>? log = null, TimeSpan? idleThreshold = null)
    {
        _forceRecover = forceRecover;
        _isRunning = isRunning;
        _log = log;
        _idleThreshold = idleThreshold ?? TimeSpan.FromMinutes(2);
        _lastTrafficTimeUtc = DateTime.UtcNow.Ticks;
    }

    public void StartStuckMonitor(TimeSpan interval)
    {
        StopStuckMonitor();
        _stuckTimer = new Timer(async _ => await CheckStuckState(), null, interval, interval);
    }

    public void StopStuckMonitor()
    {
        _stuckTimer?.Dispose();
        _stuckTimer = null;
    }

    public void RecordTraffic(long totalBytes)
    {
        Interlocked.Exchange(ref _lastTrafficBytes, totalBytes);
        Interlocked.Exchange(ref _lastTrafficTimeUtc, DateTime.UtcNow.Ticks);
    }

    private async Task CheckStuckState()
    {
        if (!_isRunning()) return;
        if (Interlocked.CompareExchange(ref _checkRunning, 1, 0) != 0) return;

        try
        {
            var lastTicks = Interlocked.Read(ref _lastTrafficTimeUtc);
            var idle = new TimeSpan(DateTime.UtcNow.Ticks - lastTicks);
            if (idle < _idleThreshold) return;

            if (_log != null) await _log.Invoke($"NetBridge stuck: no traffic for {(int)idle.TotalSeconds}s, triggering recovery");
            await _forceRecover();
        }
        finally
        {
            Interlocked.Exchange(ref _checkRunning, 0);
        }
    }

    private static readonly string[] ConnectivityHosts = ["8.8.8.8", "1.1.1.1", "223.5.5.5"];

    public static async Task<bool> VerifyConnectivityAsync()
    {
        foreach (var host in ConnectivityHosts)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 3000);
                if (reply.Status == IPStatus.Success)
                {
                    return true;
                }
            }
            catch
            {
                // Try next host
            }
        }
        return false;
    }

    public static bool IsLocalPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        StopStuckMonitor();
    }
}
