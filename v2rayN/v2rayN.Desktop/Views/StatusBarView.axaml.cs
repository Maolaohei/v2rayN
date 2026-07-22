using DialogHostAvalonia;
using ServiceLib.Handler;
using ServiceLib.HealthCheck;
using ServiceLib.HealthCheck.Models;
using ServiceLib.Resx;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private static Config _config;
    private bool _syncingProxySelection;

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        ViewModel = StatusBarViewModel.Instance;
        ViewModel?.InitUpdateView(UpdateViewHandler);

        txtRunningServerDisplay.Tapped += TxtRunningServerDisplay_Tapped;
        txtConnectionState.Tapped += TxtRunningServerDisplay_Tapped;
        btnTunHealthCheck.Tapped += BtnTunHealthCheck_Tapped;
        btnProcessListSetting.Tapped += BtnProcessListSetting_Tapped;
        lstSystemProxy.SelectionChanged += LstSystemProxy_SelectionChanged;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.InboundDisplay, v => v.txtInboundDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundLanDisplay, v => v.txtInboundLanDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningServerDisplay, v => v.txtRunningServerDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedProxyDisplay, v => v.txtSpeedProxyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedDirectDisplay, v => v.txtSpeedDirectDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableLegacyProtect, v => v.togEnableLegacyProtect.IsChecked).DisposeWith(disposables);

            // Keep hidden ComboBox binding for compatibility; segmented ListBox is the visible control.
            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyEnabled, v => v.lstSystemProxy.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyEnabled, v => v.cmbSystemProxy.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);

            this.WhenAnyValue(x => x.ViewModel!.SystemProxySelected)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(SyncSystemProxySegment)
                .DisposeWith(disposables);

            this.WhenAnyValue(
                    x => x.ViewModel!.SystemProxySelected,
                    x => x.ViewModel!.EnableTun,
                    x => x.ViewModel!.EnableLegacyProtect,
                    x => x.ViewModel!.RunningServerDisplay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RefreshConnectionHero())
                .DisposeWith(disposables);
        });

        if (Utils.IsNonWindows())
        {
            if (cmbSystemProxy.Items.IsReadOnly == false && cmbSystemProxy.Items.Count > 0)
            {
                cmbSystemProxy.Items.RemoveAt(cmbSystemProxy.Items.Count - 1);
            }

            if (lstSystemProxyPac != null)
            {
                lstSystemProxy.Items.Remove(lstSystemProxyPac);
            }
        }

        // Because this view has not yet been initialized when DispatcherRefreshIcon is first called.
        RefreshIcon();
        RefreshConnectionHero();
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherRefreshIcon:
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshIcon();
                    RefreshConnectionHero();
                }, DispatcherPriority.Default);
                break;

            case EViewAction.SetClipboardData:
                if (obj is null)
                {
                    return false;
                }

                await AvaUtils.SetClipboardData(this, (string)obj);
                break;

            case EViewAction.PasswordInput:
                return await PasswordInputAsync();

            case EViewAction.TunHealthCheckResult:
                if (obj is HealthCheckReport report)
                {
                    var locale = AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en";
                    var isZh = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    var reportText = TunHealthCheckService.FormatReport(report, locale);
                    var fixes = report.AvailableFixes ?? [];
                    if (fixes.Count > 0)
                    {
                        var fixLines = string.Join("\n", fixes.Select(f => "- " + f.Title(isZh)));
                        reportText += "\n\n" + ResUI.TunHealthCheckAvailableFixes + ":\n" + fixLines;
                    }
                    var box = new MessageBoxDialog(ResUI.TunHealthCheckTitle, reportText);
                    await box.ShowDialog(VisualRoot as Window);
                }
                else if (obj is string reportText)
                {
                    var box = new MessageBoxDialog(ResUI.TunHealthCheckTitle, reportText);
                    await box.ShowDialog(VisualRoot as Window);
                }
                break;

            case EViewAction.ProcessListSetting:
                if (obj is (string processText, bool dnsViaBridge, string protocolMode, string forwardMode))
                {
                    var box = new ProcessListSettingDialog(processText, dnsViaBridge, protocolMode, forwardMode);
                    var result = await box.ShowDialog<string?>(VisualRoot as Window);
                    if (result != null)
                    {
                        var processes = result.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        AppManager.Instance.Config.TunModeItem.ProtectedProcesses = processes;
                        AppManager.Instance.Config.NetBridgeItem ??= new();
                        AppManager.Instance.Config.NetBridgeItem.EnableDnsViaProxy = box.ResultDnsViaBridge;
                        AppManager.Instance.Config.NetBridgeItem.RuleProcess = result;
                        AppManager.Instance.Config.NetBridgeItem.ProtocolMode = box.ResultProtocolMode;
                        AppManager.Instance.Config.NetBridgeItem.ForwardMode = box.ResultForwardMode;
                        await ConfigHandler.SaveConfig(AppManager.Instance.Config);

                        if (NetBridgeManager.Instance.IsRunning)
                        {
                            var modeChanged = NetBridgeManager.Instance.ForwardMode != box.ResultForwardMode;
                            if (modeChanged)
                            {
                                await NetBridgeManager.Instance.Stop();
                                await NetBridgeManager.Instance.Start();
                            }
                            else
                            {
                                await NetBridgeManager.Instance.UpdateProxyConfig(Global.Loopback, AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
                                await NetBridgeManager.Instance.UpdateRoutes(result);
                                await NetBridgeManager.Instance.SetDnsViaProxy(box.ResultDnsViaBridge);
                            }
                        }
                    }
                }
                break;
        }
        return await Task.FromResult(true);
    }

    private void RefreshIcon()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(_config.SystemProxyItem.SysProxyType);
            var iconslist = TrayIcon.GetIcons(Application.Current);
            iconslist[0].Icon = desktop.MainWindow.Icon;
            TrayIcon.SetIcons(Application.Current, iconslist);
        }
    }

    private void SyncSystemProxySegment(int selected)
    {
        if (lstSystemProxy == null)
        {
            return;
        }

        var count = lstSystemProxy.ItemCount;
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

    private void LstSystemProxy_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingProxySelection || ViewModel == null)
        {
            return;
        }

        var index = lstSystemProxy.SelectedIndex;
        if (index < 0)
        {
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

        // Connected: system proxy set / PAC, or TUN / process hijack active.
        var protectedMode = proxy is (int)ESysProxyType.ForcedChange or (int)ESysProxyType.Pac || tunOn || legacyOn;
        var cleared = proxy == (int)ESysProxyType.ForcedClear && !tunOn && !legacyOn;
        var unchanged = proxy == (int)ESysProxyType.Unchanged && !tunOn && !legacyOn;

        string title;
        IBrush orbBrush;
        if (cleared)
        {
            title = isZh ? "未设置系统代理" : "System proxy off";
            orbBrush = TryBrush("SemiColorTertiary", "#8E8E93");
        }
        else if (unchanged)
        {
            title = isZh ? "不改动系统代理" : "System proxy unchanged";
            orbBrush = TryBrush("SemiColorWarning", "#FF9F0A");
        }
        else if (protectedMode)
        {
            title = isZh ? "已连接" : "Connected";
            orbBrush = TryBrush("SemiColorSuccess", "#34C759");
        }
        else
        {
            title = isZh ? "运行中" : "Running";
            orbBrush = TryBrush("SemiColorPrimary", "#0071E3");
        }

        txtConnectionState.Text = title;
        brdStatusOrb.Background = orbBrush;
    }

    private static IBrush TryBrush(string resourceKey, string fallbackHex)
    {
        if (Application.Current?.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    private async Task<bool> PasswordInputAsync()
    {
        var dialog = new SudoPasswordInputView();
        var obj = await DialogHost.Show(dialog);

        var password = obj?.ToString();
        if (password.IsNullOrEmpty())
        {
            togEnableTun.IsChecked = false;
            return false;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return true;
    }

    private void TxtRunningServerDisplay_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }

    private void BtnTunHealthCheck_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        ViewModel?.RunTunHealthCheck();
    }

    private void BtnProcessListSetting_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        ViewModel?.ShowProcessListSetting();
    }
}
