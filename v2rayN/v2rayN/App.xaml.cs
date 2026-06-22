namespace v2rayN;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static EventWaitHandle ProgramStarted;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Open only one process
    /// </summary>
    /// <param name="e"></param>
    protected override void OnStartup(StartupEventArgs e)
    {
        var exePathKey = Utils.GetMd5(Utils.GetExePath());

        var rebootas = e.Args.Any(t => t == Global.RebootAs);
        ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, exePathKey, out var bCreatedNew);
        if (!rebootas && !bCreatedNew)
        {
            ProgramStarted.Set();
            Environment.Exit(0);
            return;
        }

        if (!AppManager.Instance.InitApp())
        {
            UI.Show($"Loading GUI configuration file is abnormal,please restart the application{Environment.NewLine}加载GUI配置文件异常,请重启应用");
            Environment.Exit(0);
            return;
        }

        AppManager.Instance.InitComponents();

        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();

        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.SaveLog("App_DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logging.SaveLog("OnExit");
        base.OnExit(e);

        // Safety-net: ensure WinDivert handle is released before exiting.
        // AppExitAsync should have already done this, but if the shutdown race
        // skipped it we must try here — otherwise the kernel driver retains
        // stale hooks and the network stays dead after process exit.
        // v2.1.0: Driver itself stays resident (no sc stop), only the handle is released.
        try
        {
            var stopTask = NetBridgeManager.Instance.StopForShutdown(2000);
            if (!stopTask.Wait(2500))
            {
                Logging.SaveLog("NetBridge stop timed out in OnExit — forcing handle release");
                // Force native handle release even if Stop() hung
                try { NetBridgeManager.Instance.ForceReleaseHandle(); }
                catch (Exception ex2) { Logging.SaveLog($"ForceReleaseHandle failed: {ex2.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"NetBridge stop failed in OnExit: {ex.Message}");
        }

        // Give 500ms for native cleanup to complete before exit
        Thread.Sleep(500);
    }
}
