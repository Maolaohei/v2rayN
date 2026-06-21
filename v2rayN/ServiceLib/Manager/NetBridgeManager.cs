using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using NetBridgeLib.Services;

namespace ServiceLib.Manager;

public sealed class NetBridgeManager
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

    // Connection statistics
    private long _totalConnections;
    private readonly ConcurrentDictionary<string, long> _processConnections = new();

    public bool IsDriverLoaded => _driverLoaded;
    public bool IsRunning => _isProxyRunning;
    public long TotalConnections => Interlocked.Read(ref _totalConnections);
    public ConcurrentDictionary<string, long> ProcessConnections => _processConnections;

    public async Task Init(Func<bool, string, Task>? updateFunc = null)
    {
        if (_isInitialized)
        {
            return;
        }

        _updateFunc = updateFunc;

        try
        {
            _netBridgeService = new NetBridgeService();
            _netBridgeService.LogReceived += msg =>
            {
                var message = $"NetBridge Log: {msg}";
                _ = SafeInvoke(false, message);
            };

            _netBridgeService.ConnectionReceived += OnConnectionReceived;

            _driverLoaded = CheckDriverAvailability();
            if (!_driverLoaded)
            {
                var driverPath = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge", "WinDivert.dll");
                await SafeInvoke(true, $"驱动文件未找到: {driverPath}");
                return;
            }

            _ruleConfigs = BuildRuleConfigs(_config.NetBridgeItem?.RuleProcess);

            _healthMonitor?.Dispose();
            _healthMonitor = new NetBridgeHealthMonitor(
                forceRecover: async () => { _isProxyRunning = false; await RestartAsync(); },
                isRunning: () => _isProxyRunning,
                log: msg => SafeInvoke(false, msg));

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

    private static bool CheckDriverAvailability()
    {
        try
        {
            var driverPath = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge", "WinDivert.dll");
            return File.Exists(driverPath);
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
                return false;
            }

            var started = _netBridgeService.Start();
            if (!started)
            {
                await SafeInvoke(true, "NetBridge native Start() returned false");
                return false;
            }

            _isProxyRunning = true;
            _restartCount = 0;
            StartWatchdog();
            StartNetworkMonitor();
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
        if (!_isProxyRunning)
        {
            return true;
        }

        try
        {
            StopWatchdog();
            StopNetworkMonitor();

            if (_netBridgeService == null)
            {
                _isProxyRunning = false;
                _isInitialized = false;
                return false;
            }

            var stopped = _netBridgeService.Stop();
            if (!stopped)
            {
                // Even if native stop fails, reset state so next Init() re-initializes
                _isProxyRunning = false;
                _isInitialized = false;
                return false;
            }

            _isProxyRunning = false;
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            var error = $"Failed to stop NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
            _isProxyRunning = false;
            _isInitialized = false;
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
            _healthMonitor?.Dispose();

            if (_netBridgeService != null)
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                await Task.Run(() =>
                {
                    try { _netBridgeService.Stop(); }
                    catch { }
                }, cts.Token).ConfigureAwait(false);
            }
            _isProxyRunning = false;
            _isInitialized = false;
        }
        catch (OperationCanceledException)
        {
            _isProxyRunning = false;
            _isInitialized = false;
            return false;
        }
        catch (Exception ex)
        {
            var error = $"Failed to stop NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
            _isProxyRunning = false;
            _isInitialized = false;
            return false;
        }
        return true;
    }

    #region Watchdog

    private int _watchdogRunning;
    private void StartWatchdog()
    {
        StopWatchdog();
        _healthMonitor?.StartStuckMonitor(TimeSpan.FromMinutes(1));
        _watchdogTimer = new System.Threading.Timer(async _ =>
        {
            if (!_isProxyRunning) return;
            if (Interlocked.CompareExchange(ref _watchdogRunning, 1, 0) != 0) return;

            try
            {
                // Check if service is actually healthy by testing connectivity
                if (!await CheckHealthAsync())
                {
                    await SafeInvoke(true, "NetBridge health check failed, attempting restart...");
                    _isProxyRunning = false;
                    await RestartAsync();
                }
                else if (!await NetBridgeHealthMonitor.VerifyConnectivityAsync())
                {
                    await SafeInvoke(true, "NetBridge connectivity lost, forcing restart...");
                    _isProxyRunning = false;
                    await RestartAsync();
                }
            }
            catch
            {
                _isProxyRunning = false;
                await RestartAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _watchdogRunning, 0);
            }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
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
        try
        {
            _isProxyRunning = false;
            _isInitialized = false;
            _restartCount++;

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
                    await UpdateRoutes(_config.NetBridgeItem?.RuleProcess);
                    await SetDnsViaProxy(_config.NetBridgeItem?.EnableDnsViaProxy ?? false);
                    await SafeInvoke(false, "NetBridge restarted successfully");
                    _restartCount = 0; // Reset on success
                }
            }
        }
        catch (Exception ex)
        {
            await SafeInvoke(true, $"NetBridge restart failed: {ex.Message}");
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
        var newRuleConfigs = BuildRuleConfigs(ruleProcess);

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

    public async Task<bool> SetDnsViaProxy(bool enable)
    {
        if (_netBridgeService == null)
        {
            return false;
        }

        _netBridgeService.SetDnsViaProxy(enable);

        return await Task.FromResult(true);
    }

    public async Task<bool> SetLocalhostViaProxy(bool enable)
    {
        if (_netBridgeService == null)
        {
            return false;
        }

        _netBridgeService.SetLocalhostViaProxy(enable);

        return await Task.FromResult(true);
    }

    public static void SetTrafficLoggingEnabled(bool enable)
    {
        NetBridgeService.SetTrafficLoggingEnabled(enable);
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

        // Batch delete old rules
        var deleteTasks = newRules.Where(x => x.RuleId > 0)
            .Select(rule =>
            {
                try { _netBridgeService.DeleteRule(rule.RuleId); }
                catch { }
                return Task.CompletedTask;
            });
        await Task.WhenAll(deleteTasks);

        // Batch add new rules
        var addTasks = newRules.Select(rule =>
        {
            var newRuleId = _netBridgeService.AddRule(rule.ProcessName, rule.TargetHosts, rule.TargetPorts, rule.Protocol, rule.Action, rule.ProxyConfigId);
            rule.RuleId = newRuleId;
            return Task.FromResult(newRuleId);
        });
        var results = await Task.WhenAll(addTasks);

        if (results.Any(id => id == 0))
        {
            return false;
        }

        _ruleConfigs = newRules;
        return true;
    }

    private static List<NetBridgeRuleConfig> BuildRuleConfigs(string? ruleProcess)
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
            Protocol = "BOTH",
            Action = "PROXY",
            ProxyConfigId = 0
        }).ToList();
    }
}
