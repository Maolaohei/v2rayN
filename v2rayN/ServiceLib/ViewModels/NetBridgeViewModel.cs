namespace ServiceLib.ViewModels;

public class NetBridgeViewModel : MyReactiveObject
{
    [Reactive]
    public bool EnableNetBridge { get; set; }

    [Reactive]
    public bool EnabletDnsViaProxy { get; set; }

    [Reactive]
    public string RuleProcess { get; set; }

    public ReactiveCommand<Unit, Unit> SaveRulesCmd { get; }

    public NetBridgeViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;

        SaveRulesCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveRulesAsync();
        });

        this.WhenAnyValue(x => x.EnableNetBridge)
            .Skip(1)
            .Subscribe(async enabled =>
            {
                await ToggleNetBridgeAsync(enabled);
            });

        this.WhenAnyValue(x => x.EnabletDnsViaProxy)
            .Skip(1)
            .Subscribe(async enabled =>
            {
                await ToggleDnsViaProxyAsync(enabled);
            });

        _ = Init();
    }

    private async Task Init()
    {
        _config.NetBridgeItem ??= new()
        {
            RuleProcess = string.Empty
        };

        EnabletDnsViaProxy = _config.NetBridgeItem.EnableDnsViaProxy;
        EnableNetBridge = NetBridgeManager.Instance.IsRunning || _config.TunModeItem.EnableLegacyProtect;
        RuleProcess = _config.NetBridgeItem.RuleProcess ?? "";

        await Task.CompletedTask;
    }

    private async Task ToggleNetBridgeAsync(bool enabled)
    {
        if (enabled && _config.TunModeItem.EnableTun)
        {
            NoticeManager.Instance.Enqueue(ResUI.MsgNetBridgeConflictWithTun);
            EnableNetBridge = false;
            return;
        }

        // Keep status-bar "process hijack" flag in sync; Core inbound generation depends on it.
        _config.TunModeItem.EnableLegacyProtect = enabled;

        await NetBridgeManager.Instance.Init(UpdateViewHandler);

        if (enabled)
        {
            var forwardMode = _config.NetBridgeItem?.ForwardMode ?? "Legacy";
            // Bridge is deprecated -> treat as Legacy SOCKS.
            if (forwardMode is not "CoreDirect" and not "Legacy")
            {
                forwardMode = "Legacy";
            }

            const int proxyBridgeAcceptPort = 35000;
            if (forwardMode == "CoreDirect")
            {
                _config.NetBridgeItem ??= new();
                var preferred = _config.NetBridgeItem.CoreDirectTcpPort;
                if (preferred <= 0 || preferred == proxyBridgeAcceptPort)
                {
                    preferred = 35050;
                }

                var nbTcpPort = NetBridgeManager.FindFreePort(preferred);
                if (nbTcpPort < 0 || nbTcpPort == proxyBridgeAcceptPort)
                {
                    nbTcpPort = NetBridgeManager.FindFreePort(proxyBridgeAcceptPort + 1);
                }
                if (nbTcpPort < 0 || nbTcpPort == proxyBridgeAcceptPort)
                {
                    NoticeManager.Instance.Enqueue("NetBridge CoreDirect: no free Core port");
                    EnableNetBridge = false;
                    _config.TunModeItem.EnableLegacyProtect = false;
                    return;
                }

                _config.NetBridgeItem.CoreDirectTcpPort = nbTcpPort;
                // Native UDP redirect is fixed at 35001 for now.
                if (_config.NetBridgeItem.CoreDirectUdpPort <= 0 ||
                    _config.NetBridgeItem.CoreDirectUdpPort == proxyBridgeAcceptPort)
                {
                    _config.NetBridgeItem.CoreDirectUdpPort = 35001;
                }

                await ConfigHandler.SaveConfig(_config);
                AppEvents.ReloadRequested.Publish();

                for (var i = 0; i < 50; i++)
                {
                    await Task.Delay(100);
                    if (!NetBridgeHealthMonitor.IsLocalPortAvailable(nbTcpPort))
                    {
                        break;
                    }
                }

                NetBridgeManager.SetUseNetBridgeProtocol(true);
                var succeed = await NetBridgeManager.Instance.Start();
                if (succeed)
                {
                    NetBridgeManager.SetUseNetBridgeProtocol(true);
                    NetBridgeManager.SetRelayPort((ushort)nbTcpPort);
                    await NetBridgeManager.Instance.UpdateRoutes(RuleProcess);
                    await NetBridgeManager.Instance.SetDnsViaProxy(EnabletDnsViaProxy);
                }
                else
                {
                    EnableNetBridge = false;
                    _config.TunModeItem.EnableLegacyProtect = false;
                }

                NoticeManager.Instance.Enqueue(succeed ? ResUI.OperationSuccess : ResUI.OperationFailed);
            }
            else
            {
                await ConfigHandler.SaveConfig(_config);
                NetBridgeManager.SetUseNetBridgeProtocol(false);
                var succeed = await NetBridgeManager.Instance.Start();
                if (succeed)
                {
                    await NetBridgeManager.Instance.ConfigureForwardModeAsync("Legacy");
                    await NetBridgeManager.Instance.UpdateRoutes(RuleProcess);
                    await NetBridgeManager.Instance.SetDnsViaProxy(EnabletDnsViaProxy);
                }
                else
                {
                    EnableNetBridge = false;
                    _config.TunModeItem.EnableLegacyProtect = false;
                }

                NoticeManager.Instance.Enqueue(succeed ? ResUI.OperationSuccess : ResUI.OperationFailed);
            }
        }
        else
        {
            await ConfigHandler.SaveConfig(_config);
            var succeed = await NetBridgeManager.Instance.Stop();
            AppEvents.ReloadRequested.Publish();
            NoticeManager.Instance.Enqueue(succeed ? ResUI.OperationSuccess : ResUI.OperationFailed);
        }
    }

    private async Task ToggleDnsViaProxyAsync(bool enabled)
    {
        _config.NetBridgeItem.EnableDnsViaProxy = enabled;
        await ConfigHandler.SaveConfig(_config);
        await NetBridgeManager.Instance.SetDnsViaProxy(enabled);
    }

    private async Task SaveRulesAsync()
    {
        _config.NetBridgeItem ??= new();

        var normalizedRuleProcess = RuleProcess;
        _config.NetBridgeItem.RuleProcess = normalizedRuleProcess;
        RuleProcess = normalizedRuleProcess;

        if (await ConfigHandler.SaveConfig(_config) != 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return;
        }

        if (EnableNetBridge || NetBridgeManager.Instance.IsRunning)
        {
            await NetBridgeManager.Instance.Init(UpdateViewHandler);
            var routesUpdated = await NetBridgeManager.Instance.UpdateRoutes(normalizedRuleProcess);
            NoticeManager.Instance.Enqueue(routesUpdated ? ResUI.OperationSuccess : ResUI.OperationFailed);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        }
    }

    private async Task<bool> UpdateViewHandler(bool isError, string msg)
    {
        NoticeManager.Instance.SendMessageEx(msg);
        return await Task.FromResult(true);
    }
}
