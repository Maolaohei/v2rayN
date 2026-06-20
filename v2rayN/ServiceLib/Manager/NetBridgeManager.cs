using NetBridgeLib.Services;

namespace ServiceLib.Manager;

public sealed class NetBridgeManager
{
    private static readonly Lazy<NetBridgeManager> _instance = new(() => new());
    public static NetBridgeManager Instance => _instance.Value;
    private readonly Config _config = AppManager.Instance.Config;
    private NetBridgeService? _netBridgeService;
    private bool _isProxyRunning;
    private bool _isInitialized;
    private bool _driverLoaded;
    private List<NetBridgeRuleConfig> _ruleConfigs = [];
    private Func<bool, string, Task>? _updateFunc;
    private uint _proxyConfigId;
    private System.Threading.Timer? _watchdogTimer;

    public bool IsDriverLoaded => _driverLoaded;
    public bool IsRunning => _isProxyRunning;

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

            _netBridgeService.ConnectionReceived += (processName, pid, destIp, destPort, proxyInfo) =>
            {
                var message = $"NetBridge Connection: {processName} (PID: {pid}) -> {destIp}:{destPort} -> {proxyInfo}";
                _ = SafeInvoke(false, message);
            };

            _driverLoaded = CheckDriverAvailability();
            if (!_driverLoaded)
            {
                var driverPath = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge", "WinDivert.dll");
                await SafeInvoke(true, $"驱动文件未找到: {driverPath}");
                return;
            }

            _ruleConfigs = BuildRuleConfigs(_config.NetBridgeItem?.RuleProcess);
            _isInitialized = true;
            await SafeInvoke(false, "NetBridge 初始化成功");
        }
        catch (Exception ex)
        {
            var error = $"Failed to initialize NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
        }
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
            StartWatchdog();
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

            if (_netBridgeService == null)
            {
                return false;
            }

            var stopped = _netBridgeService.Stop();
            if (!stopped)
            {
                return false;
            }

            _isProxyRunning = false;
        }
        catch (Exception ex)
        {
            var error = $"Failed to stop NetBridgeService: {ex.Message}";
            await SafeInvoke(true, error);
            return false;
        }

        return true;
    }

    #region Watchdog

    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdogTimer = new System.Threading.Timer(async _ =>
        {
            if (!_isProxyRunning) return;

            try
            {
                if (_netBridgeService == null)
                {
                    await SafeInvoke(true, "NetBridge service lost, attempting restart...");
                    _ = RestartAsync();
                    return;
                }

                if (!_netBridgeService.IsRunning && _isProxyRunning)
                {
                    await SafeInvoke(true, "NetBridge crashed, attempting restart...");
                    _isProxyRunning = false;
                    _ = RestartAsync();
                }
            }
            catch
            {
                _isProxyRunning = false;
                _ = RestartAsync();
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    private void StopWatchdog()
    {
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;
    }

    private async Task RestartAsync()
    {
        try
        {
            _isProxyRunning = false;
            _isInitialized = false;

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
                }
            }
        }
        catch (Exception ex)
        {
            await SafeInvoke(true, $"NetBridge restart failed: {ex.Message}");
        }
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
                    return false;
                }
            }
            else
            {
                _proxyConfigId = _netBridgeService.AddProxyConfig(proxyType, proxyHost, (ushort)proxyPort, username, password);
                if (_proxyConfigId == 0)
                {
                    return false;
                }
            }

            return await Task.FromResult(true);
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

    private async Task<bool> ApplyRoutesInternal()
    {
        if (_netBridgeService == null)
        {
            return false;
        }

        List<NetBridgeRuleConfig> rules;

        rules = _ruleConfigs.Select(JsonUtils.DeepCopy).ToList();

        foreach (var rule in rules.Where(x => x.RuleId > 0))
        {
            try
            {
                _ = _netBridgeService.DeleteRule(rule.RuleId);
            }
            catch
            {
                // ignored
            }
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var newRuleId = _netBridgeService.AddRule(rule.ProcessName, rule.TargetHosts, rule.TargetPorts, rule.Protocol, rule.Action, rule.ProxyConfigId);
            if (newRuleId == 0)
            {
                return false;
            }

            rules[i].RuleId = newRuleId;
        }

        _ruleConfigs = rules;

        return await Task.FromResult(true);
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
