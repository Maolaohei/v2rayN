using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using ServiceLib.HealthCheck.Models;
using ServiceLib.Resx;
using ServiceLib.Handler;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class StatusBarView
{
    private static Config _config;
    private bool _syncingProxySelection;

    public StatusBarView()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;
        ViewModel = StatusBarViewModel.Instance;
        ViewModel?.InitUpdateView(UpdateViewHandler);

        ApplySegmentLabels();
        // PAC is Windows-only; keep segment count in sync with hidden ComboBox options.
        if (Utils.IsNonWindows())
        {
            if (lstSystemProxyPac != null)
            {
                lstSystemProxy.Items.Remove(lstSystemProxyPac);
            }

            if (cmbSystemProxy.Items.Count > 0)
            {
                try
                {
                    cmbSystemProxy.Items.RemoveAt(cmbSystemProxy.Items.Count - 1);
                }
                catch
                {
                    // ComboBox items may be fixed in some hosts; tray still covers PAC.
                }
            }

            if (menuSystemProxyPac != null)
            {
                menuSystemProxyPac.Visibility = Visibility.Collapsed;
            }
        }

        menuExit.Click += menuExit_Click;
        btnTunHealthCheck.Click += btnTunHealthCheck_Click;
        menuTunHealthCheck.Click += btnTunHealthCheck_Click;
        btnProcessListSetting.Click += btnProcessListSetting_Click;
        txtRunningServerDisplay.PreviewMouseDown += txtRunningInfoDisplay_MouseDoubleClick;
        txtConnectionState.PreviewMouseDown += txtRunningInfoDisplay_MouseDoubleClick;
        lstSystemProxy.SelectionChanged += LstSystemProxy_SelectionChanged;

        this.WhenActivated(disposables =>
        {
            //system proxy
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyClear, v => v.menuSystemProxyClear2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, vmToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxySet, v => v.menuSystemProxySet2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, vmToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyNothing, v => v.menuSystemProxyNothing2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, vmToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyPac, v => v.menuSystemProxyPac2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, vmToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyEnabled, v => v.lstSystemProxy.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyEnabled, v => v.cmbSystemProxy.IsEnabled).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxyClearCmd, v => v.menuSystemProxyClear).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxySetCmd, v => v.menuSystemProxySet).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxyNothingCmd, v => v.menuSystemProxyNothing).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxyPacCmd, v => v.menuSystemProxyPac).DisposeWith(disposables);

            //routings and servers
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.cmbRoutings.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.menuRoutings.Visibility).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.sepRoutings.Visibility).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.Servers, v => v.cmbServers.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedServer, v => v.cmbServers.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlServers, v => v.cmbServers.Visibility).DisposeWith(disposables);

            //tray menu
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy2).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.CopyProxyCmdToClipboardCmd, v => v.menuCopyProxyCmdToClipboard).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.RunningServerToolTipText, v => v.tbNotify.ToolTipText).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NotifyLeftClickCmd, v => v.tbNotify.LeftClickCommand).DisposeWith(disposables);

            //status bar
            this.OneWayBind(ViewModel, vm => vm.InboundDisplay, v => v.txtInboundDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundLanDisplay, v => v.txtInboundLanDisplay.Text).DisposeWith(disposables);
            // Subtitle is composed in RefreshConnectionHero (node · mode · routing); keep raw fields for that.
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedProxyDisplay, v => v.txtSpeedProxyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedDirectDisplay, v => v.txtSpeedDirectDisplay.Text).DisposeWith(disposables);
            this.WhenAnyValue(x => x.ViewModel!.SpeedProxyDisplay, x => x.ViewModel!.SpeedDirectDisplay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    var locale = AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en";
                    var isZh = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    NormalizeRateDisplay(txtSpeedProxyDisplay, isZh);
                    NormalizeRateDisplay(txtSpeedDirectDisplay, isZh);
                })
                .DisposeWith(disposables);

            // Primary surface no longer shows TUN/Legacy toggles; keep bindings via collapsed controls + tray.
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableLegacyProtect, v => v.togEnableLegacyProtect.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.menuEnableTun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableLegacyProtect, v => v.menuEnableLegacyProtect.IsChecked).DisposeWith(disposables);

            // Hidden ComboBox keeps existing binding; segmented ListBox is the visible control.
            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.cmbRoutings2.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.cmbRoutings2.Visibility).DisposeWith(disposables);

            this.WhenAnyValue(x => x.ViewModel!.SystemProxySelected)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(SyncSystemProxySegment)
                .DisposeWith(disposables);

            this.WhenAnyValue(
                    x => x.ViewModel!.SystemProxySelected,
                    x => x.ViewModel!.EnableTun,
                    x => x.ViewModel!.EnableLegacyProtect,
                    x => x.ViewModel!.RunningServerDisplay,
                    x => x.ViewModel!.RunningInfoDisplay,
                    x => x.ViewModel!.SelectedRouting)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RefreshConnectionHero())
                .DisposeWith(disposables);
        });

        RefreshConnectionHero();
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherRefreshIcon:
                Application.Current?.Dispatcher.Invoke(async () =>
                {
                    await RefreshIcon();
                    RefreshConnectionHero();
                }, DispatcherPriority.Normal);
                break;

            case EViewAction.SetClipboardData:
                if (obj is null)
                {
                    return false;
                }

                WindowsUtils.SetClipboardData((string)obj);
                break;

            case EViewAction.TunHealthCheckResult:
                if (obj is HealthCheckReport report)
                {
                    var window = new TunHealthCheckResultWindow(report);
                    window.ShowDialog();
                }
                break;

            case EViewAction.ProcessListSetting:
                if (obj is (string processText, bool dnsViaBridge, string protocolMode, string forwardMode))
                {
                    ProcessListSettingWindow? window = null;
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        window = new ProcessListSettingWindow(processText, dnsViaBridge, protocolMode, forwardMode);
                    });
                    if (window != null && window.ShowDialog() == true)
                    {
                        var processes = window.ResultProcessList
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList();
                        var oldForwardMode = AppManager.Instance.Config.NetBridgeItem?.ForwardMode ?? "Bridge";
                        var newForwardMode = window.ResultForwardMode;
                        var newProtocolMode = window.ResultProtocolMode;

                        AppManager.Instance.Config.TunModeItem.ProtectedProcesses = processes;
                        AppManager.Instance.Config.NetBridgeItem ??= new();
                        AppManager.Instance.Config.NetBridgeItem.EnableDnsViaProxy = window.ResultDnsViaBridge;
                        AppManager.Instance.Config.NetBridgeItem.RuleProcess = window.ResultProcessList;
                        AppManager.Instance.Config.NetBridgeItem.ProtocolMode = window.ResultProtocolMode;
                        AppManager.Instance.Config.NetBridgeItem.ForwardMode = window.ResultForwardMode;
                        await ConfigHandler.SaveConfig(AppManager.Instance.Config);

                        RefreshProcessHijackLabel();

                        if (NetBridgeManager.Instance.IsRunning)
                        {
                            var modeChanged = oldForwardMode != newForwardMode;
                            if (modeChanged)
                            {
                                NoticeManager.Instance.SendMessageEx($"转发模式已切换: {oldForwardMode} → {newForwardMode}，正在重启 NetBridge...");
                                AppEvents.ReloadRequested.Publish();
                                AppEvents.NetBridgeRestartRequested.Publish();
                            }
                            else
                            {
                                await NetBridgeManager.Instance.UpdateProxyConfig(Global.Loopback, AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
                                await NetBridgeManager.Instance.UpdateRoutes(window.ResultProcessList);
                                await NetBridgeManager.Instance.SetDnsViaProxy(window.ResultDnsViaBridge);
                                NoticeManager.Instance.SendMessageEx($"进程列表和协议模式已更新 (进程: {processes.Count}, 协议: {newProtocolMode})");
                            }
                        }
                        else
                        {
                            NoticeManager.Instance.SendMessageEx($"设置已保存 (转发模式: {newForwardMode}, 协议: {newProtocolMode})，开启进程劫持后生效");
                        }
                    }
                }
                break;
        }
        return await Task.FromResult(true);
    }

    private async void menuExit_Click(object sender, RoutedEventArgs e)
    {
        tbNotify.Dispose();
        await AppManager.Instance.AppExitAsync(true);
    }

    private async Task RefreshIcon()
    {
        tbNotify.Icon = await WindowsManager.Instance.GetNotifyIcon(_config);
        if (Application.Current?.MainWindow != null)
        {
            Application.Current.MainWindow.Icon = WindowsManager.Instance.GetAppIcon(_config);
        }
    }

    private void ApplySegmentLabels()
    {
        if (lstSystemProxy == null || lstSystemProxy.Items.Count < 3)
        {
            return;
        }

        var locale = AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en";
        var isZh = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        // Short segment labels for density (design mock). Full strings remain on tray menu.
        string[] labels = isZh
            ? ["清除", "系统代理", "不改动", "PAC"]
            : ["Clear", "System", "Unchanged", "PAC"];

        for (var i = 0; i < Math.Min(labels.Length, lstSystemProxy.Items.Count); i++)
        {
            if (lstSystemProxy.Items[i] is System.Windows.Controls.ListBoxItem item)
            {
                item.Content = labels[i];
            }
        }
    }

    private void SyncSystemProxySegment(int selected)
    {
        if (lstSystemProxy == null)
        {
            return;
        }

        var count = lstSystemProxy.Items.Count;
        if (count <= 0)
        {
            return;
        }

        var index = Math.Clamp(selected, 0, count - 1);
        if (lstSystemProxy.SelectedIndex == index)
        {
            return;
        }

        _syncingProxySelection = true;
        try
        {
            lstSystemProxy.SelectedIndex = index;
        }
        finally
        {
            _syncingProxySelection = false;
        }
    }

    private void LstSystemProxy_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingProxySelection || ViewModel == null)
        {
            return;
        }

        var index = lstSystemProxy.SelectedIndex;
        // Segmented control must always have a selection (clicking selected chip can clear ListBox).
        if (index < 0)
        {
            SyncSystemProxySegment(ViewModel.SystemProxySelected);
            return;
        }

        if (ViewModel.SystemProxySelected != index)
        {
            ViewModel.SystemProxySelected = index;
        }
    }

    private void RefreshConnectionHero()
    {
        if (brdStatusOrb == null || txtConnectionState == null)
        {
            return;
        }

        var proxy = ViewModel?.SystemProxySelected ?? (int)_config.SystemProxyItem.SysProxyType;
        var tunOn = ViewModel?.EnableTun == true;
        var legacyOn = ViewModel?.EnableLegacyProtect == true;
        var locale = AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en";
        var isZh = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        var nodeDisplay = ViewModel?.RunningServerDisplay?.Trim() ?? string.Empty;
        var hasNode = nodeDisplay.IsNotEmpty()
            && !nodeDisplay.Equals("-", StringComparison.Ordinal)
            && !nodeDisplay.Contains("无效", StringComparison.OrdinalIgnoreCase)
            && !nodeDisplay.Contains("invalid", StringComparison.OrdinalIgnoreCase);

        var protectedMode = proxy is (int)ESysProxyType.ForcedChange or (int)ESysProxyType.Pac || tunOn || legacyOn;
        var cleared = proxy == (int)ESysProxyType.ForcedClear && !tunOn && !legacyOn;
        var unchanged = proxy == (int)ESysProxyType.Unchanged && !tunOn && !legacyOn;

        // Design contract: title = outcome language; subtitle = composition (node · mode · routing).
        // No active node ⇒ never claim "protected", even if system-proxy segment is selected.
        string title;
        Color orbColor;
        PackIconKind iconKind;
        if (!hasNode)
        {
            title = isZh ? "未运行" : "Not running";
            orbColor = Color.FromRgb(0x8B, 0x90, 0xA0); // DesignOrbOff
            iconKind = PackIconKind.Minus;
        }
        else if (cleared)
        {
            // Design: node ready but system proxy off → warning, not dead gray.
            title = isZh ? "代理已关" : "Proxy off";
            orbColor = Color.FromRgb(0xC4, 0x7B, 0x12); // DesignWarn
            iconKind = PackIconKind.Exclamation;
        }
        else if (unchanged)
        {
            title = isZh ? "不改动系统代理" : "System proxy unchanged";
            orbColor = Color.FromRgb(0xC4, 0x7B, 0x12);
            iconKind = PackIconKind.Exclamation;
        }
        else if (protectedMode)
        {
            title = isZh ? "已保护" : "Protected";
            orbColor = Color.FromRgb(0x0F, 0x9F, 0x6E); // DesignSignal
            iconKind = PackIconKind.Check;
        }
        else
        {
            title = isZh ? "运行中" : "Running";
            orbColor = Color.FromRgb(0x2F, 0x6F, 0xED); // DesignSelect
            iconKind = PackIconKind.LightningBolt;
        }

        txtConnectionState.Text = title;
        brdStatusOrb.Background = new SolidColorBrush(orbColor);
        if (icoStatusOrb != null)
        {
            icoStatusOrb.Kind = iconKind;
        }

        if (txtRunningServerDisplay != null)
        {
            string modePart;
            if (tunOn)
            {
                modePart = "TUN";
            }
            else if (legacyOn)
            {
                modePart = isZh ? "进程劫持" : "Process hijack";
            }
            else
            {
                modePart = proxy switch
                {
                    (int)ESysProxyType.ForcedChange => isZh ? "系统代理" : "System proxy",
                    (int)ESysProxyType.Pac => "PAC",
                    (int)ESysProxyType.Unchanged => isZh ? "不改动" : "Unchanged",
                    _ => isZh ? "未设置系统代理" : "Proxy cleared"
                };
            }

            var routing = ViewModel?.SelectedRouting?.Remarks;
            var parts = new List<string>();
            if (hasNode)
            {
                parts.Add(nodeDisplay.Length > 28 ? nodeDisplay[..28] + "…" : nodeDisplay);
            }
            else if (!protectedMode)
            {
                parts.Add(isZh ? "选择节点后启动核心" : "Select a node to start");
            }

            if (hasNode || protectedMode)
            {
                parts.Add(modePart);
            }

            if (routing.IsNotEmpty() && (hasNode || protectedMode))
            {
                parts.Add(routing!);
            }

            txtRunningServerDisplay.Text = string.Join(" · ", parts.Where(p => p.IsNotEmpty()));
        }

        // Routing ghost button always shows 路由 · name (closed state).
        if (btnRoutingDisplay != null)
        {
            var routingName = ViewModel?.SelectedRouting?.Remarks;
            btnRoutingDisplay.Content = routingName.IsNotEmpty()
                ? $"{(isZh ? "路由" : "Routing")} · {routingName}"
                : (isZh ? "路由 · —" : "Routing · —");
            btnRoutingDisplay.ToolTip = btnRoutingDisplay.Content;
        }

        // Idle / empty rates should read as "—" not blank (design: 未运行 shows ↓ — ↑ —).
        NormalizeRateDisplay(txtSpeedProxyDisplay, isZh);
        NormalizeRateDisplay(txtSpeedDirectDisplay, isZh);

        RefreshProcessHijackLabel(isZh);
    }

    private void BtnRoutingDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (cmbRoutings2 == null)
        {
            return;
        }

        cmbRoutings2.Focus();
        cmbRoutings2.IsDropDownOpen = true;
    }

    private static void NormalizeRateDisplay(System.Windows.Controls.TextBlock? block, bool isZh)
    {
        if (block == null)
        {
            return;
        }

        // Design: rates always visible as "↓ … / ↑ …"; empty or bare zero → em dash.
        var t = block.Text?.Trim() ?? string.Empty;
        var isEmpty = t.Length == 0
            || t == "0"
            || t == "0 B"
            || t == "0B"
            || t == "0 KB/s"
            || t == "0KB/s"
            || t == "—"
            || t == "-";

        // Preserve ViewModel strings that already carry direction glyphs.
        if (!isEmpty)
        {
            return;
        }

        // Assign stable placeholders so the capsule right column never collapses.
        if (block.Name == "txtSpeedProxyDisplay")
        {
            block.Text = "↓ —";
        }
        else if (block.Name == "txtSpeedDirectDisplay")
        {
            block.Text = "↑ —";
        }
        else
        {
            block.Text = "—";
        }
    }

    private void RefreshProcessHijackLabel(bool? isZh = null)
    {
        if (btnProcessListSetting == null)
        {
            return;
        }

        isZh ??= (AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en")
            .StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        var count = 0;
        if (_config.TunModeItem?.ProtectedProcesses is { Count: > 0 } list)
        {
            count = list.Count;
        }
        else if (_config.NetBridgeItem?.RuleProcess.IsNotEmpty() == true)
        {
            count = _config.NetBridgeItem.RuleProcess
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;
        }

        var label = isZh == true ? "进程劫持" : "Process hijack";
        btnProcessListSetting.Content = count > 0 ? $"{label} · {count}" : label;
    }

    private void btnTunHealthCheck_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RunTunHealthCheck();
    }

    private void btnProcessListSetting_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ShowProcessListSetting();
    }

    private void txtRunningInfoDisplay_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }
}
