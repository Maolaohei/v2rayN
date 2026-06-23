using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using NetBridgeLib.Services;

namespace ServiceLib.Manager;

public sealed class NetBridgeManager : IDisposable
{
    private static readonly Lazy<NetBridgeManager> _instance = new(() => new());
    public static NetBridgeManager Instance => _instance.Value;
    private readonly Config _config = AppManager.Instance.Config;
    private NetBridgeService? _netBridgeService;
    private volatile bool _isProxyRunning;
    private volatile bool _isInitialized;
    private volatile bool _driverLoaded;
    private List<NetBridgeRuleConfig> _ruleConfigs = [];
    private Func<bool, string, Task>? _updateFunc;
    private uint _proxyConfigId;
    private System.Threading.Timer? _watchdogTimer;
    private int _restartCount;
    private const int MaxRestartAttempts = 3;
    private const int RestartCooldownSeconds = 60;
    private System.Threading.Timer? _networkChangeDebounceTimer;
    private NetBridgeHealthMonitor? _healthMonitor;
    private int _restartRunning;
    private System.Diagnostics.Process? _nbBridgeProcess;

    // Connection statistics
    private long _totalConnections;
    private readonly ConcurrentDictionary<string, long> _processConnections = new();
    private const int MaxProcessEntries = 500;

    public event Action? StateChanged;

    public bool IsDriverLoaded => _driverLoaded;
    public bool IsRunning => _isProxyRunning;
    public long TotalConnections => Interlocked.Read(ref _totalConnections);
    public ConcurrentDictionary<string, long> ProcessConnections => _processConnections;

    /// <summary>
    /// Gets the current forward mode from config.
    /// </summary>
    public string ForwardMode => _config?.NetBridgeItem?.ForwardMode ?? "Bridge";

    /// <summary>
    /// Returns the allowed protocol modes for the current forward mode.
    /// Legacy only supports TCP; Bridge and CoreDirect support TCP/UDP/BOTH.
    /// </summary>
    public static string[] GetAllowedProtocolModes(string forwardMode)
    {
        return forwardMode == "Legacy"
            ? ["TCP"]
            : ["TCP", "UDP", "BOTH"];
    }

    /// <summary>
    /// Finds a free port starting from the given port.
    /// Tries the requested port first, then scans up to 100 nearby ports.
    /// Returns the free port, or -1 if none found.
    /// </summary>
    public static int FindFreePort(int preferredPort)
    {
        for (var port = preferredPort; port < preferredPort + 100; port++)
        {
            if (NetBridgeHealthMonitor.IsLocalPortAvailable(port))
            {
                return port;
            }
        }
        return -1;
    }

    public async Task Init(Func<bool, string, Task>? updateFunc = null)
    {
        if (_isInitialized)
        {
            return;
        }

        _updateFunc = updateFunc;

        try
        {
            if (_netBridgeService != null)
            {
                try { _netBridgeService.Dispose(); }
                catch { }
                _netBridgeService = null;
            }

            _netBridgeService = new NetBridgeService();
            _netBridgeService.LogReceived += msg =>
            {
                var message = $"NetBridge Log: {msg}";
                _ = SafeInvoke(false, message);
            };

            _netBridgeService.ConnectionReceived += OnConnectionReceived;

            _driverLoaded = await CheckDriverAvailabilityAsync();
            if (!_driverLoaded)
            {
                var driverPath = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge", "WinDivert.dll");
                await SafeInvoke(true, $"驱动文件未找到: {driverPath}");
                return;
            }

            _ruleConfigs = BuildRuleConfigs(_config?.NetBridgeItem?.RuleProcess, _config?.NetBridgeItem?.ProtocolMode ?? "TCP");

            _healthMonitor?.Dispose();
            _healthMonitor = new NetBridgeHealthMonitor(
                forceRecover: async () => { _isProxyRunning = false; await RestartAsync(); },
                isRunning: () => _isProxyRunning,
                log: msg => SafeInvoke(false, msg));

            _isInitialized = true;
            await SafeInvoke(false, "NetBridge 初始化成功");
        }
        catch (Exception ex)
        {
            var error = $"Failed to initialize NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
        }
    }

    private int _lastLogTime;
    private void OnConnectionReceived(string processName, uint pid, string destIp, ushort destPort, string proxyInfo)
    {
        try
        {
            Interlocked.Increment(ref _totalConnections);
            _processConnections.AddOrUpdate(processName, 1, (_, count) => count + 1);
            TcpConnectionResetter.TrackConnection(pid);

            if (_processConnections.Count > MaxProcessEntries)
            {
                var oldest = _processConnections.OrderBy(kvp => kvp.Value).Take(_processConnections.Count / 2).Select(kvp => kvp.Key).ToList();
                foreach (var key in oldest)
                {
                    _processConnections.TryRemove(key, out _);
                }
            }

            _healthMonitor?.RecordTraffic(Interlocked.Read(ref _totalConnections));

            // Log at most once per second to avoid flooding
            var now = Environment.TickCount;
            if (now - _lastLogTime > 1000)
            {
                Interlocked.Exchange(ref _lastLogTime, now);
                var message = $"NetBridge Connection: {processName} (PID: {pid}) -> {destIp}:{destPort} -> {proxyInfo}";
                _ = SafeInvoke(false, message);
            }
        }
        catch { }
    }

    private async Task SafeInvoke(bool isError, string message)
    {
        if (_updateFunc == null) return;

        try
        {
            await _updateFunc.Invoke(isError, message);
        }
        catch
        {
            // Swallow callback exceptions
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch
        {
            // Swallow subscriber exceptions to prevent state machine interruption
        }
    }

    private static async Task<bool> CheckDriverAvailabilityAsync()
    {
        try
        {
            var baseDir = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge");
            if (!File.Exists(Path.Combine(baseDir, "WinDivert.dll"))
                || !File.Exists(Path.Combine(baseDir, "WinDivert64.sys")))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> Start()
    {
        if (_isProxyRunning)
        {
            return true;
        }

        if (!_driverLoaded)
        {
            await SafeInvoke(true, ResUI.MsgNetBridgeDriverNotFound);
            return false;
        }

        try
        {
            if (_netBridgeService == null)
            {
                await Init(_updateFunc);
            }

            if (_netBridgeService == null)
            {
                return false;
            }

            // Stop existing service with timeout before starting new one
            try
            {
                var stopTask = Task.Run(() =>
                {
                    try { _netBridgeService.Stop(); }
                    catch { }
                });
                if (!stopTask.Wait(1500))
                {
                    await SafeInvoke(false, "Previous NetBridge Stop() timed out, forcing");
                    _netBridgeService.ForceStop();
                }
            }
            catch { }

            // v2.1.0 Phase 2: Retry with short backoff (200ms, 400ms)
            const int maxRetries = 2;
            var delayMs = 200;
            var started = false;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                // Run ProxyBridge_Start on background thread to avoid blocking UI
                var startTask = Task.Run(() => _netBridgeService.Start());
                started = await startTask;
                if (started) break;
                if (attempt < maxRetries)
                {
                    await SafeInvoke(false, $"NetBridge Start() attempt {attempt + 1}/{maxRetries} failed, retrying in {delayMs}ms...");
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
            }

            if (!started)
            {
                await SafeInvoke(true, $"NetBridge native Start() failed after {maxRetries + 1} attempts");
                return false;
            }

            // Log native version for diagnostics
            var version = NetBridgeService.GetNativeVersion();
            await SafeInvoke(false, $"NetBridge started (native v{version})");

            _isProxyRunning = true;
            _restartCount = 0;
            StartWatchdog();
            StartNetworkMonitor();
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            var error = $"Failed to start NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
            return false;
        }

        return true;
    }

    public async Task<bool> Stop()
    {
        try
        {
            StopWatchdog();
            StopNetworkMonitor();
            StopNetBridgeBridge();

            var resetCount = TcpConnectionResetter.ResetTrackedConnections();
            if (resetCount > 0)
            {
                await SafeInvoke(false, $"NetBridge: Reset {resetCount} TCP connections to force app reconnection");
            }

            if (_netBridgeService != null)
            {
                _netBridgeService.Dispose();
            }

            _netBridgeService = null;
            _isProxyRunning = false;
            _isInitialized = false;
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            var error = $"Failed to stop NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
            _netBridgeService = null;
            _isProxyRunning = false;
            _isInitialized = false;
            RaiseStateChanged();
            return false;
        }

        return true;
    }

    public async Task<bool> StopForShutdown(int timeoutMs = 3000)
    {
        try
        {
            StopWatchdog();
            StopNetworkMonitor();
            StopNetBridgeBridge();
            _healthMonitor?.Dispose();
            _healthMonitor = null;

            var resetCount = TcpConnectionResetter.ResetTrackedConnections();
            if (resetCount > 0)
            {
                await SafeInvoke(false, $"NetBridge: Reset {resetCount} TCP connections on shutdown");
            }

            if (_netBridgeService != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(timeoutMs);
                    await Task.Run(() =>
                    {
                        try { _netBridgeService.Stop(); }
                        catch { }
                    }, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    try { _netBridgeService.Dispose(); }
                    catch { }
                }
            }
            _netBridgeService = null;
            _isProxyRunning = false;
            _isInitialized = false;
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            var error = $"Failed to stop NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
            _netBridgeService = null;
            _isProxyRunning = false;
            _isInitialized = false;
            RaiseStateChanged();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Force-releases the native WinDivert handle without waiting for clean shutdown.
    /// Used as a last resort when StopForShutdown times out.
    /// </summary>
    public void ForceReleaseHandle()
    {
        try
        {
            if (_netBridgeService != null)
            {
                _netBridgeService.ForceStop();
                _netBridgeService.Dispose();
            }
        }
        catch { }
        _netBridgeService = null;
        _isProxyRunning = false;
        _isInitialized = false;
    }

    #region Watchdog

    private int _watchdogRunning;
    private void StartWatchdog()
    {
        StopWatchdog();
        _healthMonitor?.StartStuckMonitor(TimeSpan.FromMinutes(1));
        _watchdogTimer = new System.Threading.Timer(_ => _ = WatchdogTickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    private async Task WatchdogTickAsync()
    {
        if (!_isProxyRunning) return;
        if (Interlocked.CompareExchange(ref _watchdogRunning, 1, 0) != 0) return;

        try
        {
            if (!await CheckHealthAsync())
            {
                await SafeInvoke(true, $"NetBridge health check failed (attempt {_restartCount + 1}/{MaxRestartAttempts}), attempting restart...");
                _isProxyRunning = false;
                RaiseStateChanged();
                await RestartAsync();
            }
            else if (!await NetBridgeHealthMonitor.VerifyConnectivityAsync())
            {
                await SafeInvoke(true, $"NetBridge connectivity lost (attempt {_restartCount + 1}/{MaxRestartAttempts}), forcing restart...");
                _isProxyRunning = false;
                RaiseStateChanged();
                await RestartAsync();
            }
        }
        catch (Exception ex)
        {
            await SafeInvoke(true, $"NetBridge watchdog error: {ex.Message}");
            _isProxyRunning = false;
            RaiseStateChanged();
            await RestartAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _watchdogRunning, 0);
        }
    }

    private async Task<bool> CheckHealthAsync()
    {
        try
        {
            if (_netBridgeService == null) return false;

            // Simple health check: try to get rule position for rule ID 0
            // This is a lightweight call that verifies the native service is responsive
            var result = _netBridgeService.GetRulePosition(0);
            return true; // If no exception, service is healthy
        }
        catch
        {
            return false;
        }
    }

    private void StopWatchdog()
    {
        _healthMonitor?.StopStuckMonitor();
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;
    }

    private async Task RestartAsync()
    {
        if (Interlocked.CompareExchange(ref _restartRunning, 1, 0) != 0) return;

        try
        {
            _isProxyRunning = false;
            _isInitialized = false;
            _restartCount++;
            RaiseStateChanged();

            if (_restartCount > MaxRestartAttempts)
            {
                await SafeInvoke(true, $"NetBridge restart failed after {MaxRestartAttempts} attempts, giving up");
                await Task.Delay(RestartCooldownSeconds * 1000);
                _restartCount = 0;
                return;
            }

            await Init(_updateFunc);
            if (_isInitialized)
            {
                await Start();
                if (_isProxyRunning)
                {
                    await UpdateProxyConfig(Global.Loopback, AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
                    await UpdateRoutes(_config?.NetBridgeItem?.RuleProcess);
                    await SetDnsViaProxy(_config?.NetBridgeItem?.EnableDnsViaProxy ?? false);
                    await SafeInvoke(false, "NetBridge restarted successfully");
                    _restartCount = 0; // Reset on success
                }
            }
        }
        catch (Exception ex)
        {
            await SafeInvoke(true, $"NetBridge restart failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _restartRunning, 0);
        }
    }

    #endregion

    #region Network Change Detection

    private void StartNetworkMonitor()
    {
        StopNetworkMonitor();
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    private void StopNetworkMonitor()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _networkChangeDebounceTimer?.Dispose();
        _networkChangeDebounceTimer = null;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (!_isProxyRunning) return;

        // Debounce: WiFi switch may fire multiple events in rapid succession
        _networkChangeDebounceTimer?.Dispose();
        _networkChangeDebounceTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                await SafeInvoke(false, "Network change detected, restarting NetBridge...");
                await RestartAsync();
            }
            catch (Exception ex)
            {
                await SafeInvoke(true, $"Network change restart failed: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    #endregion

    public async Task<bool> UpdateRoutes(string? ruleProcess)
    {
        var protocolMode = _config?.NetBridgeItem?.ProtocolMode ?? "TCP";
        var newRuleConfigs = BuildRuleConfigs(ruleProcess, protocolMode);

        _ruleConfigs = newRuleConfigs;

        if (!_isProxyRunning)
        {
            return true;
        }

        return await ApplyRoutesInternal();
    }

    public async Task<bool> UpdateProxyConfig(string proxyHost, int proxyPort)
    {
        try
        {
            if (_netBridgeService == null)
            {
                return false;
            }

            var proxyType = "SOCKS5";
            var username = "";
            var password = "";

            if (_proxyConfigId > 0)
            {
                var edited = _netBridgeService.EditProxyConfig(_proxyConfigId, proxyType, proxyHost, (ushort)proxyPort, username, password);
                if (!edited)
                {
                    // Config was deleted, recreate
                    _proxyConfigId = _netBridgeService.AddProxyConfig(proxyType, proxyHost, (ushort)proxyPort, username, password);
                }
            }
            else
            {
                _proxyConfigId = _netBridgeService.AddProxyConfig(proxyType, proxyHost, (ushort)proxyPort, username, password);
            }

            return _proxyConfigId > 0;
        }
        catch (Exception ex)
        {
            var error = $"Failed to update proxy config: {ex.Message}";
            await SafeInvoke(true, error);
            return false;
        }
    }

    public Task<bool> SetDnsViaProxy(bool enable)
    {
        if (_netBridgeService == null)
        {
            return Task.FromResult(false);
        }

        _netBridgeService.SetDnsViaProxy(enable);
        return Task.FromResult(true);
    }

    public Task<bool> SetLocalhostViaProxy(bool enable)
    {
        if (_netBridgeService == null)
        {
            return Task.FromResult(false);
        }

        _netBridgeService.SetLocalhostViaProxy(enable);
        return Task.FromResult(true);
    }

    public static void SetTrafficLoggingEnabled(bool enable)
    {
        NetBridgeService.SetTrafficLoggingEnabled(enable);
    }

    public static void SetRelayPort(ushort port)
    {
        NetBridgeService.SetRelayPort(port);
    }

    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _totalConnections, 0);
        _processConnections.Clear();
    }

    private async Task<bool> ApplyRoutesInternal()
    {
        if (_netBridgeService == null)
        {
            return false;
        }

        var newRules = _ruleConfigs.Select(JsonUtils.DeepCopy).ToList();

        foreach (var rule in newRules.Where(x => x.RuleId > 0))
        {
            try { _netBridgeService.DeleteRule(rule.RuleId); }
            catch { }
        }

        foreach (var rule in newRules)
        {
            rule.ProxyConfigId = _proxyConfigId;
            rule.RuleId = _netBridgeService.AddRule(rule.ProcessName, rule.TargetHosts, rule.TargetPorts, rule.Protocol, rule.Action, _proxyConfigId);
        }

        if (newRules.Any(rule => rule.RuleId == 0))
        {
            return false;
        }

        _ruleConfigs = newRules;
        return await Task.FromResult(true);
    }

    private static List<NetBridgeRuleConfig> BuildRuleConfigs(string? ruleProcess, string protocolMode = "TCP")
    {
        if (ruleProcess.IsNullOrEmpty())
        {
            return new();
        }

        var processNames = Utils.String2List(Utils.Convert2Comma(ruleProcess));
        return processNames.Select(processName => new NetBridgeRuleConfig
        {
            ProcessName = processName,
            TargetHosts = "*",
            TargetPorts = "*",
            Protocol = protocolMode,
            Action = "PROXY",
            ProxyConfigId = 0
        }).ToList();
    }

    #region NetBridgeBridge Process (deprecated — Bridge mode hidden)

    [Obsolete("Bridge mode is deprecated. Use CoreDirect or Legacy instead.")]
    public async Task StartNetBridgeBridgeAsync(int socksPort)
    {
        // Bridge mode deprecated — no longer starting NetBridgeBridge.exe
        await SafeInvoke(false, "Bridge mode is deprecated, use CoreDirect or Legacy");
    }

    public void StopNetBridgeBridge()
    {
        if (_nbBridgeProcess == null) return;

        try
        {
            if (!_nbBridgeProcess.HasExited)
            {
                _nbBridgeProcess.Kill();
                _nbBridgeProcess.WaitForExit(2000);
            }
        }
        catch { }

        try { _nbBridgeProcess.Dispose(); } catch { }
        _nbBridgeProcess = null;

        // Reset relay port back to CoreDirect default
        NetBridgeService.SetRelayPort(35000);
    }

    public bool IsNetBridgeBridgeRunning => _nbBridgeProcess is { HasExited: false };

    #endregion

    public void Dispose()
    {
        StopWatchdog();
        StopNetworkMonitor();
        _healthMonitor?.Dispose();
        _healthMonitor = null;
        StopNetBridgeBridge();

        try { _netBridgeService?.Dispose(); }
        catch { }

        _netBridgeService = null;
        _isProxyRunning = false;
        _isInitialized = false;
    }
}
