using ServiceLib.HealthCheck;

namespace ServiceLib.ViewModels;

public class StatusBarViewModel : MyReactiveObject
{
    private static readonly Lazy<StatusBarViewModel> _instance = new(() => new(null));
    public static StatusBarViewModel Instance => _instance.Value;

    #region ObservableCollection

    public IObservableCollection<RoutingItem> RoutingItems { get; } = new ObservableCollectionExtended<RoutingItem>();

    public IObservableCollection<ComboItem> Servers { get; } = new ObservableCollectionExtended<ComboItem>();

    [Reactive]
    public RoutingItem SelectedRouting { get; set; }

    [Reactive]
    public ComboItem SelectedServer { get; set; }

    [Reactive]
    public bool BlServers { get; set; }

    #endregion ObservableCollection

    public ReactiveCommand<Unit, Unit> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> AddServerViaScanCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyProxyCmdToClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> NotifyLeftClickCmd { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCmd { get; }
    public ReactiveCommand<Unit, Unit> HideWindowCmd { get; }
    public ReactiveCommand<Unit, Unit> TunHealthCheckCmd { get; }
    public ReactiveCommand<Unit, Unit> ProcessListSettingCmd { get; }

    #region System Proxy

    [Reactive]
    public bool BlSystemProxyClear { get; set; }

    [Reactive]
    public bool BlSystemProxySet { get; set; }

    [Reactive]
    public bool BlSystemProxyNothing { get; set; }

    [Reactive]
    public bool BlSystemProxyPac { get; set; }

    public ReactiveCommand<Unit, Unit> SystemProxyClearCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxySetCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxyNothingCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxyPacCmd { get; }

    [Reactive]
    public bool BlRouting { get; set; }

    [Reactive]
    public int SystemProxySelected { get; set; }

    [Reactive]
    public bool BlSystemProxyPacVisible { get; set; }

    /// <summary>
    /// Controls whether the system-proxy segmented control is interactive.
    /// Disabled for secondary instances to prevent proxy override.
    /// </summary>
    [Reactive]
    public bool BlSystemProxyEnabled { get; set; } = true;

    #endregion System Proxy

    #region UI

    [Reactive]
    public string InboundDisplay { get; set; }

    [Reactive]
    public string InboundLanDisplay { get; set; }

    [Reactive]
    public string RunningServerDisplay { get; set; }

    [Reactive]
    public string RunningServerToolTipText { get; set; }

    [Reactive]
    public string RunningInfoDisplay { get; set; }

    [Reactive]
    public string SpeedProxyDisplay { get; set; }

    [Reactive]
    public string SpeedDirectDisplay { get; set; }

    [Reactive]
    public bool EnableTun { get; set; }

    [Reactive]
    public bool EnableLegacyProtect { get; set; }

    [Reactive]
    public bool BlIsNonWindows { get; set; }

    #endregion UI

    public StatusBarViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        SelectedRouting = new();
        SelectedServer = new();
        RunningServerToolTipText = "-";
        BlSystemProxyPacVisible = Utils.IsWindows();
        BlIsNonWindows = Utils.IsNonWindows();

        // Secondary instances cannot change system proxy via UI.
        if (AppManager.Instance.IsSecondaryInstance)
        {
            BlSystemProxyEnabled = false;
        }

        if (_config.TunModeItem.EnableTun && AllowEnableTun())
        {
            EnableTun = true;
        }
        else
        {
            _config.TunModeItem.EnableTun = EnableTun = false;
        }

        EnableLegacyProtect = _config.TunModeItem.EnableLegacyProtect;

        #region WhenAnyValue && ReactiveCommand

        this.WhenAnyValue(
                x => x.SelectedRouting,
                y => y != null && !y.Remarks.IsNullOrEmpty())
            .Subscribe(async c => await RoutingSelectedChangedAsync(c));

        this.WhenAnyValue(
                x => x.SelectedServer,
                y => y != null && !y.Text.IsNullOrEmpty())
            .Subscribe(ServerSelectedChanged);

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        this.WhenAnyValue(
                x => x.SystemProxySelected,
                y => y >= 0)
            .Subscribe(async c => await DoSystemProxySelected(c));

        this.WhenAnyValue(
                x => x.EnableTun,
                y => y == true)
            .Subscribe(async c => await DoEnableTun(c));

        this.WhenAnyValue(
                x => x.EnableLegacyProtect,
                y => y == true)
            .Subscribe(async c => await DoEnableLegacyProtect(c));

        CopyProxyCmdToClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyProxyCmdToClipboard();
        });

        TunHealthCheckCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RunTunHealthCheck();
        });

        ProcessListSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ShowProcessListSetting();
        });

        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            AppEvents.ShowHideWindowRequested.Publish(null);
            await Task.CompletedTask;
        });
        ShowWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            AppEvents.ShowHideWindowRequested.Publish(true);
            await Task.CompletedTask;
        });
        HideWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            AppEvents.ShowHideWindowRequested.Publish(false);
            await Task.CompletedTask;
        });

        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await AddServerViaClipboard();
            });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScan();
        });
        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(true);
        });

        //System proxy
        // Secondary instance: system-proxy commands must be disabled.
        var canChangeProxy = Observable.Return(!AppManager.Instance.IsSecondaryInstance);
        SystemProxyClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedClear);
        }, canChangeProxy);
        SystemProxySetCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedChange);
        }, canChangeProxy);
        SystemProxyNothingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Unchanged);
        }, canChangeProxy);
        SystemProxyPacCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Pac);
        }, canChangeProxy);

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        if (updateView != null)
        {
            InitUpdateView(updateView);
        }

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async result => await UpdateStatistics(result));

        AppEvents.RoutingsMenuRefreshRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await RefreshRoutingsMenu());

        AppEvents.TestServerRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await TestServerAvailability());

        AppEvents.InboundDisplayRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async _ => await InboundDisplayStatus());

        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async result => await SetListenerType(result));

        AppEvents.NetBridgeRestartRequested
            .AsObservable()
            .Subscribe(async _ =>
            {
                var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
                var coreReady = ServiceLib.Services.NetBridgeRestartPolicy.IsCoreReady(socksPort);
                if (!coreReady) return; // avoid false restart while core is switching
                if (!ServiceLib.Services.NetBridgeRestartPolicy.ShouldRestart(true, false, coreReady)) return;
                ServiceLib.Services.NetBridgeRestartPolicy.MarkRestarted();
                await Task.Run(async () => await RestartNetBridgeAsync());
            });

        #endregion AppEvents

        if (EnableLegacyProtect)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000);
                    await StartNetBridgeAsync();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"NetBridge startup failed: {ex.Message}");
                }
            });
        }

        _ = Init();
    }

    private async Task Init()
    {
        await ConfigHandler.InitBuiltinRouting(_config);
        await RefreshRoutingsMenu();
        await InboundDisplayStatus();
        await ChangeSystemProxyAsync(_config.SystemProxyItem.SysProxyType, true);

        BlRouting = true;
    }

    public void InitUpdateView(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _updateView = updateView;
        if (_updateView != null)
        {
            AppEvents.ProfilesRefreshRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async _ => await RefreshServersBiz()); //.DisposeWith(_disposables);
        }
    }

    private async Task CopyProxyCmdToClipboard()
    {
        var cmd = Utils.IsWindows() ? "set" : "export";
        var address = $"{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}";

        var sb = new StringBuilder();
        sb.AppendLine($"{cmd} http_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} https_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} all_proxy={Global.Socks5Protocol}{address}");
        sb.AppendLine("");
        sb.AppendLine($"{cmd} HTTP_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} HTTPS_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} ALL_PROXY={Global.Socks5Protocol}{address}");

        await _updateView?.Invoke(EViewAction.SetClipboardData, sb.ToString());
    }

    private async Task AddServerViaClipboard()
    {
        AppEvents.AddServerViaClipboardRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task AddServerViaScan()
    {
        AppEvents.AddServerViaScanRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task UpdateSubscriptionProcess(bool blProxy)
    {
        AppEvents.SubscriptionsUpdateRequested.Publish(blProxy);
        await Task.Delay(1000);
    }

    private async Task RefreshServersBiz()
    {
        await RefreshServersMenu();

        //display running server
        var running = await ConfigHandler.GetDefaultServer(_config);
        if (running != null)
        {
            RunningServerDisplay =
                RunningServerToolTipText = running.GetSummary();
        }
        else
        {
            RunningServerDisplay =
                RunningServerToolTipText = ResUI.CheckServerSettings;
        }
    }

    private async Task RefreshServersMenu()
    {
        var lstModel = await AppManager.Instance.ProfileModels(_config.SubIndexId, "");

        Servers.Clear();
        if (lstModel.Count > _config.GuiItem.TrayMenuServersLimit)
        {
            BlServers = false;
            return;
        }

        var models = new List<ComboItem>();
        BlServers = true;
        foreach (var it in lstModel)
        {
            var name = it.GetSummary();

            var item = new ComboItem() { ID = it.IndexId, Text = name };
            models.Add(item);
            if (_config.IndexId == it.IndexId)
            {
                SelectedServer = item;
            }
        }
        Servers.AddRange(models);
    }

    private void ServerSelectedChanged(bool c)
    {
        if (!c)
        {
            return;
        }
        if (SelectedServer == null)
        {
            return;
        }
        if (SelectedServer.ID.IsNullOrEmpty())
        {
            return;
        }
        AppEvents.SetDefaultServerRequested.Publish(SelectedServer.ID);
    }

    public async Task TestServerAvailability()
    {
        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            return;
        }

        await TestServerAvailabilitySub(ResUI.Speedtesting);

        var msg = await Task.Run(ConnectionHandler.RunAvailabilityCheck);

        NoticeManager.Instance.SendMessageEx(msg);
        await TestServerAvailabilitySub(msg);
    }

    private async Task TestServerAvailabilitySub(string msg)
    {
        RxSchedulers.MainThreadScheduler.Schedule(msg, (scheduler, msg) =>
        {
            _ = TestServerAvailabilityResult(msg);
            return Disposable.Empty;
        });
        await Task.CompletedTask;
    }

    public async Task TestServerAvailabilityResult(string msg)
    {
        RunningInfoDisplay = msg;
        await Task.CompletedTask;
    }

    public async Task RunTunHealthCheck()
    {
        if (!AppManager.Instance.IsRunningCore(ECoreType.Xray) && !AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TunHealthCheckCoreNotRunning);
            return;
        }

        var locale = _config.UiItem?.CurrentLanguage ?? "en";
        var isZh = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        if (_config.TunModeItem?.EnableTun != true)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TunHealthCheckNonTunMode);
        }

        await TestServerAvailabilitySub(ResUI.TunHealthCheckRunning);

        var service = new TunHealthCheckService(_config);
        var report = await service.RunFullCheckAsync(async msg =>
        {
            await TestServerAvailabilitySub(msg);
        });

        var reportText = TunHealthCheckService.FormatReport(report, isZh ? "zh" : "en");
        NoticeManager.Instance.SendMessageEx(reportText);
        await TestServerAvailabilitySub(report.Summary);

        _updateView?.Invoke(EViewAction.TunHealthCheckResult, report);

        try
        {
            var jsonPath = Path.Combine(Utils.GetLogPath(), $"tun-health-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var json = TunHealthCheckService.ExportJson(report);
            await File.WriteAllTextAsync(jsonPath, json);
            NoticeManager.Instance.SendMessageEx(string.Format(ResUI.TunHealthCheckReportExported, jsonPath));
        }
        catch { }
    }

    public async Task ShowProcessListSetting()
    {
        var currentProcesses = _config.TunModeItem.ProtectedProcesses ?? new List<string>();
        var processText = string.Join(",", currentProcesses);
        var dnsViaBridge = _config.NetBridgeItem?.EnableDnsViaProxy ?? false;
        var protocolMode = _config.NetBridgeItem?.ProtocolMode ?? "TCP";
        var forwardMode = _config.NetBridgeItem?.ForwardMode ?? "Bridge";
        _updateView?.Invoke(EViewAction.ProcessListSetting, (processText, dnsViaBridge, protocolMode, forwardMode));
        await Task.CompletedTask;
    }

    #region System proxy and Routings

    private async Task SetListenerType(ESysProxyType type)
    {
        // Secondary instance must not modify system proxy state or config.
        if (AppManager.Instance.IsSecondaryInstance)
        {
            return;
        }
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        _config.SystemProxyItem.SysProxyType = type;
        await ChangeSystemProxyAsync(type, true);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        await ConfigHandler.SaveConfig(_config);
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange)
    {
        // Secondary instance must not change system proxy — it belongs to the primary instance.
        if (AppManager.Instance.IsSecondaryInstance)
        {
            return;
        }

        // Apply WinINET/proxy settings first so subsequent reconnects pick up the new mode.
        await SysProxyHandler.UpdateSysProxy(_config, false);

        // UI flags must update immediately; do not wait on NetBridge TCP reset work.
        BlSystemProxyClear = type == ESysProxyType.ForcedClear;
        BlSystemProxySet = type == ESysProxyType.ForcedChange;
        BlSystemProxyNothing = type == ESysProxyType.Unchanged;
        BlSystemProxyPac = type == ESysProxyType.Pac;

        if (blChange)
        {
            _updateView?.Invoke(EViewAction.DispatcherRefreshIcon, null);
        }

        // System proxy and process hijack are independent. When proxy is toggled while
        // NetBridge is active, Chrome/etc may keep half-open sockets that were established
        // via WinINET proxy and no longer work after clear (or vice versa). Force reconnect.
        // IMPORTANT: ResetHijackedConnections scans TCP tables + SetTcpEntry and can stall
        // the UI thread for seconds if done inline on the ReactiveCommand/MainThread path.
        if (EnableLegacyProtect && NetBridgeManager.Instance.IsRunning)
        {
            var forwardMode = _config.NetBridgeItem?.ForwardMode;
            _ = Task.Run(async () =>
            {
                try
                {
                    var reset = await NetBridgeManager.Instance.RefreshAfterSystemProxyChangeAsync(forwardMode);
                    if (reset > 0)
                    {
                        NoticeManager.Instance.SendMessageEx(
                            $"NetBridge: reset {reset} hijacked TCP connections after proxy change");
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"NetBridge proxy-change refresh failed: {ex.Message}");
                }
            });
        }

        await Task.CompletedTask;
    }

    private async Task RefreshRoutingsMenu()
    {
        var routings = await AppManager.Instance.RoutingItems();

        RoutingItems.Clear();
        RoutingItems.AddRange(routings);

        SelectedRouting = routings.FirstOrDefault(t => t.IsActive == true);
    }

    private async Task RoutingSelectedChangedAsync(bool c)
    {
        if (!c)
        {
            return;
        }

        if (SelectedRouting == null)
        {
            return;
        }

        var item = await AppManager.Instance.GetRoutingItem(SelectedRouting?.Id);
        if (item is null)
        {
            return;
        }

        if (await ConfigHandler.SetDefaultRouting(_config, item) == 0)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            AppEvents.ReloadRequested.Publish();
            _updateView?.Invoke(EViewAction.DispatcherRefreshIcon, null);
        }
    }

    private async Task DoSystemProxySelected(bool c)
    {
        if (!c)
        {
            return;
        }
        if (AppManager.Instance.IsSecondaryInstance)
        {
            return;
        }
        if (_config.SystemProxyItem.SysProxyType == (ESysProxyType)SystemProxySelected)
        {
            return;
        }
        await SetListenerType((ESysProxyType)SystemProxySelected);
    }

    private async Task DoEnableTun(bool c)
    {
        if (_config.TunModeItem.EnableTun == EnableTun)
        {
            return;
        }

        _config.TunModeItem.EnableTun = EnableTun;

        if (EnableTun && AllowEnableTun() == false)
        {
            // When running as a non-administrator, reboot to administrator mode
            if (Utils.IsWindows())
            {
                _config.TunModeItem.EnableTun = false;
                await AppManager.Instance.RebootAsAdmin();
                return;
            }
            else
            {
                bool? passwordResult = await _updateView?.Invoke(EViewAction.PasswordInput, null);
                if (passwordResult == false)
                {
                    _config.TunModeItem.EnableTun = false;
                    return;
                }
            }
        }

        if (EnableTun)
        {
            EnableLegacyProtect = false;
            _config.TunModeItem.EnableLegacyProtect = false;

            if (NetBridgeManager.Instance.IsRunning)
            {
                await StopNetBridgeAsync();
            }
        }

        await ConfigHandler.SaveConfig(_config);
        AppEvents.ReloadRequested.Publish();
    }

    private async Task DoEnableLegacyProtect(bool c)
    {
        if (_config.TunModeItem.EnableLegacyProtect == EnableLegacyProtect)
        {
            return;
        }

        _config.TunModeItem.EnableLegacyProtect = EnableLegacyProtect;

        if (EnableLegacyProtect && AllowEnableTun() == false)
        {
            if (Utils.IsWindows())
            {
                _config.TunModeItem.EnableLegacyProtect = false;
                await AppManager.Instance.RebootAsAdmin();
                return;
            }
            else
            {
                bool? passwordResult = await _updateView?.Invoke(EViewAction.PasswordInput, null);
                if (passwordResult == false)
                {
                    _config.TunModeItem.EnableLegacyProtect = false;
                    return;
                }
            }
        }

        if (EnableLegacyProtect)
        {
            var cachedForwardMode = _config.NetBridgeItem?.ForwardMode ?? "Bridge";
            var wasTunEnabled = EnableTun;
            EnableTun = false;
            _config.TunModeItem.EnableTun = false;

            await ConfigHandler.SaveConfig(_config);

            if (wasTunEnabled)
            {
                AppEvents.ReloadRequested.Publish();
                var tunStopped = await WaitForTunStop();

                if (!tunStopped)
                {
                    NoticeManager.Instance.SendMessageEx(ResUI.OperationFailed);
                    return;
                }
            }
            await StartNetBridgeAsync(cachedForwardMode);
        }
        else
        {
            await ConfigHandler.SaveConfig(_config);
            await StopNetBridgeAsync();
            AppEvents.ReloadRequested.Publish();
        }
    }

    private async Task<bool> WaitForTunStop()
    {
        // Poll quickly; ReloadRequested is already published before this wait.
        // Poll every 100ms, max ~5 seconds (first check is immediate).
        for (var i = 0; i < 50; i++)
        {
            if (!IsTunAdapterActive())
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static bool IsTunAdapterActive()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => ni.Name.Contains("wintun", StringComparison.OrdinalIgnoreCase)
                        || ni.Name.Contains(Global.V2rayTunName, StringComparison.OrdinalIgnoreCase)
                        || ni.Name.Contains(Global.SingboxTunName, StringComparison.OrdinalIgnoreCase)
                        || ni.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private async Task StartNetBridgeAsync(string? cachedForwardMode = null)
    {
        var ruleProcess = _config.NetBridgeItem?.RuleProcess ?? "";
        var forwardMode = cachedForwardMode ?? _config.NetBridgeItem?.ForwardMode ?? "Bridge";
        var dnsViaProxy = _config.NetBridgeItem?.EnableDnsViaProxy ?? true;

        await NetBridgeManager.Instance.Init(async (isError, msg) =>
        {
            NoticeManager.Instance.SendMessageEx(msg);
        });

        // ProxyBridge always accepts WinDivert-redirected CoreDirect TCP on fixed 35000
        // (NB_CORE_TCP_PORT). Core's netbridge inbound MUST be a different port; that port
        // is what SetRelayPort() points nb_tcp at.
        const int proxyBridgeAcceptPort = 35000;

        if (forwardMode == "CoreDirect")
        {
            _config.NetBridgeItem ??= new();
            var preferredTcpPort = _config.NetBridgeItem.CoreDirectTcpPort;
            if (preferredTcpPort <= 0 || preferredTcpPort == proxyBridgeAcceptPort)
            {
                preferredTcpPort = 35050;
            }

            var nbTcpPort = NetBridgeManager.FindFreePort(preferredTcpPort);
            if (nbTcpPort < 0 || nbTcpPort == proxyBridgeAcceptPort)
            {
                nbTcpPort = NetBridgeManager.FindFreePort(proxyBridgeAcceptPort + 1);
            }
            if (nbTcpPort < 0 || nbTcpPort == proxyBridgeAcceptPort)
            {
                NoticeManager.Instance.SendMessageEx("NetBridge CoreDirect: no free Core port");
                return;
            }

            var portChanged = _config.NetBridgeItem.CoreDirectTcpPort != nbTcpPort;
            _config.NetBridgeItem.CoreDirectTcpPort = nbTcpPort;
            // Native UDP redirect hardcodes 35001 (NB_CORE_UDP_PORT). Keep config aligned.
            if (_config.NetBridgeItem.CoreDirectUdpPort <= 0 ||
                _config.NetBridgeItem.CoreDirectUdpPort == proxyBridgeAcceptPort ||
                _config.NetBridgeItem.CoreDirectUdpPort == nbTcpPort)
            {
                _config.NetBridgeItem.CoreDirectUdpPort = 35001;
            }

            if (portChanged)
            {
                await ConfigHandler.SaveConfig(_config);
            }

            // Core must expose netbridge inbound BEFORE ProxyBridge connects to it.
            AppEvents.ReloadRequested.Publish();
            var coreReady = false;
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                // Port not available => something is listening (core inbound).
                if (!NetBridgeHealthMonitor.IsLocalPortAvailable(nbTcpPort))
                {
                    coreReady = true;
                    break;
                }
            }
            if (!coreReady)
            {
                NoticeManager.Instance.SendMessageEx($"NetBridge CoreDirect: Core not listening on {nbTcpPort}, starting anyway");
            }

            NetBridgeManager.SetUseNetBridgeProtocol(true);
            var succeed = await NetBridgeManager.Instance.Start();
            if (!succeed)
            {
                return;
            }

            // Critical: relay port = Core inbound, NOT ProxyBridge accept port 35000.
            NetBridgeManager.SetUseNetBridgeProtocol(true);
            NetBridgeManager.SetRelayPort((ushort)nbTcpPort);
            NoticeManager.Instance.SendMessageEx($"NetBridge CoreDirect: ProxyBridgeCore(35000) -> Core:{nbTcpPort}");
        }
        else
        {
            // Legacy / Bridge(fallback): SOCKS5 local relay -> Core mixed inbound.
            NetBridgeManager.SetUseNetBridgeProtocol(false);
            var succeed = await NetBridgeManager.Instance.Start();
            if (!succeed)
            {
                return;
            }

            NetBridgeManager.SetUseNetBridgeProtocol(false);
            await NetBridgeManager.Instance.UpdateProxyConfig(
                Global.Loopback,
                AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
            NoticeManager.Instance.SendMessageEx("NetBridge Legacy: ProxyBridgeCore -> Core (SOCKS5)");
        }

        await NetBridgeManager.Instance.UpdateRoutes(ruleProcess);

        _config.NetBridgeItem ??= new();
        _config.NetBridgeItem.EnableDnsViaProxy = dnsViaProxy;
        await NetBridgeManager.Instance.SetDnsViaProxy(dnsViaProxy);
    }

    private async Task StopNetBridgeAsync()
    {
        await NetBridgeManager.Instance.Stop();
    }

    private async Task RestartNetBridgeAsync()
    {
        await StopNetBridgeAsync();
        await Task.Delay(500);
        await StartNetBridgeAsync();
    }

    private bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }

    #endregion System proxy and Routings

    #region UI

    private async Task InboundDisplayStatus()
    {
        StringBuilder sb = new();
        sb.Append($"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        if (_config.Inbound.First().SecondLocalPortEnabled)
        {
            sb.Append($",{AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}");
        }
        sb.Append(']');
        InboundDisplay = $"{ResUI.LabLocal}:{sb}";

        if (_config.Inbound.First().AllowLANConn)
        {
            var lan = _config.Inbound.First().NewPort4LAN
                ? $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}]"
                : $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}]";
            InboundLanDisplay = $"{ResUI.LabLAN}:{lan}";
        }
        else
        {
            InboundLanDisplay = $"{ResUI.LabLAN}:{Global.None}";
        }
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        if (!_config.GuiItem.DisplayRealTimeSpeed)
        {
            return;
        }

        try
        {
            if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, EInboundProtocol.mixed, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Empty;
            }
            else
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, Global.ProxyTag, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Format(ResUI.SpeedDisplayText, Global.DirectTag, Utils.HumanFy(update.DirectUp), Utils.HumanFy(update.DirectDown));
            }
        }
        catch
        {
        }
        await Task.CompletedTask;
    }

    #endregion UI
}
