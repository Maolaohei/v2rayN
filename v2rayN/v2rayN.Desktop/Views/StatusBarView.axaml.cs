using DialogHostAvalonia;
using ServiceLib.Handler;
using ServiceLib.Resx;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private static Config _config;

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        ViewModel = StatusBarViewModel.Instance;
        ViewModel?.InitUpdateView(UpdateViewHandler);

        txtRunningServerDisplay.Tapped += TxtRunningServerDisplay_Tapped;
        txtRunningInfoDisplay.Tapped += TxtRunningServerDisplay_Tapped;
        btnTunHealthCheck.Tapped += BtnTunHealthCheck_Tapped;
        btnProcessListSetting.Tapped += BtnProcessListSetting_Tapped;

        this.WhenActivated(disposables =>
        {
            //status bar
            this.OneWayBind(ViewModel, vm => vm.InboundDisplay, v => v.txtInboundDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundLanDisplay, v => v.txtInboundLanDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningServerDisplay, v => v.txtRunningServerDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedProxyDisplay, v => v.txtSpeedProxyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedDirectDisplay, v => v.txtSpeedDirectDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableLegacyProtect, v => v.togEnableLegacyProtect.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);
        });

        //spEnableTun.IsVisible = (Utils.IsWindows() || AppHandler.Instance.IsAdministrator);

        if (Utils.IsNonWindows() && cmbSystemProxy.Items.IsReadOnly == false)
        {
            cmbSystemProxy.Items.RemoveAt(cmbSystemProxy.Items.Count - 1);
        }
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherRefreshIcon:
                Dispatcher.UIThread.Post(RefreshIcon,
                DispatcherPriority.Default);
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
                if (obj is string reportText)
                {
                    var box = new MessageBoxDialog(ResUI.TunHealthCheckTitle, reportText);
                    await box.ShowDialog(VisualRoot as Window);
                }
                break;

            case EViewAction.ProcessListSetting:
                if (obj is (string processText, bool dnsViaBridge))
                {
                    var box = new ProcessListSettingDialog(processText, dnsViaBridge);
                    var result = await box.ShowDialog<string?>(VisualRoot as Window);
                    if (result != null)
                    {
                        var processes = result.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        AppManager.Instance.Config.TunModeItem.ProtectedProcesses = processes;
                        AppManager.Instance.Config.NetBridgeItem ??= new();
                        AppManager.Instance.Config.NetBridgeItem.EnableDnsViaProxy = box.ResultDnsViaBridge;
                        AppManager.Instance.Config.NetBridgeItem.RuleProcess = result;
                        await ConfigHandler.SaveConfig(AppManager.Instance.Config);

                        if (NetBridgeManager.Instance.IsRunning)
                        {
                            await NetBridgeManager.Instance.UpdateProxyConfig(Global.Loopback, AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
                            await NetBridgeManager.Instance.UpdateRoutes(result);
                            await NetBridgeManager.Instance.SetDnsViaProxy(box.ResultDnsViaBridge);
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
