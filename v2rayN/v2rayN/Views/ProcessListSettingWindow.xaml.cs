using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ServiceLib.Manager;
using ServiceLib.Resx;

namespace v2rayN.Views;

public partial class ProcessListSettingWindow
{
    private readonly List<ProcessItem> _processItems = [];
    private readonly List<string> _activeProcesses = [];
    private readonly ListCollectionView _processView;
    private readonly ListCollectionView _activeView;
    private bool _dnsViaBridge;
    private string _protocolMode = "TCP";
    private int _sessionAddedCount;
    private readonly Action _onNetBridgeStateChanged;

    private static readonly List<string> PresetBrowsers =
        ["chrome.exe", "firefox.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe"];

    private static readonly List<string> PresetDevTools =
        ["code.exe", "cursor.exe", "git.exe", "node.exe", "python.exe", "dotnet.exe",
         "claude.exe", "curl.exe", "wget.exe", "powershell.exe", "pwsh.exe"];

    public string ResultProcessList { get; private set; } = "";
    public bool ResultDnsViaBridge { get; private set; }
    public string ResultProtocolMode => _protocolMode;

    public ProcessListSettingWindow(string processList, bool dnsViaBridge, string protocolMode = "TCP")
    {
        InitializeComponent();

        _dnsViaBridge = dnsViaBridge;
        _protocolMode = protocolMode;
        chkDnsViaBridge.IsChecked = dnsViaBridge;

        // Initialize protocol radio buttons
        switch (protocolMode.ToUpperInvariant())
        {
            case "UDP":
                rbProtocolUdp.IsChecked = true;
                break;
            case "BOTH":
            case "TCP+UDP":
                rbProtocolBoth.IsChecked = true;
                break;
            default:
                rbProtocolTcp.IsChecked = true;
                break;
        }

        var existingProcesses = processList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _activeProcesses.AddRange(existingProcesses.Distinct(StringComparer.OrdinalIgnoreCase));

        _processView = new ListCollectionView(_processItems);
        _processView.Filter = FilterRunningProcess;
        lstProcesses.ItemsSource = _processView;

        _activeView = new ListCollectionView(_activeProcesses);
        _activeView.Filter = FilterActiveProcess;
        lstActiveProcesses.ItemsSource = _activeView;

        LoadProcesses();
        RefreshActiveList();
        CheckDriverStatus();

        _onNetBridgeStateChanged = () => Dispatcher.BeginInvoke(CheckDriverStatus);
        NetBridgeManager.Instance.StateChanged += _onNetBridgeStateChanged;
        Closed += (_, _) => NetBridgeManager.Instance.StateChanged -= _onNetBridgeStateChanged;

        btnRefresh.Click += (_, _) => LoadProcesses();
        chkSelectAll.Checked += (_, _) => SelectAllRunning(true);
        chkSelectAll.Unchecked += (_, _) => SelectAllRunning(false);
        btnAddSelected.Click += BtnAddSelected_Click;
        btnAddManual.Click += BtnAddManual_Click;
        btnRemoveSelected.Click += BtnRemoveSelected_Click;
        btnPresetBrowser.Click += (_, _) => AddPresetProcesses(PresetBrowsers);
        btnPresetDev.Click += (_, _) => AddPresetProcesses(PresetDevTools);
        btnPresetAll.Click += (_, _) => AddPresetProcesses(PresetBrowsers.Concat(PresetDevTools).ToList());
        btnImportFolder.Click += BtnImportFolder_Click;
        rbProtocolTcp.Checked += (_, _) => _protocolMode = "TCP";
        rbProtocolUdp.Checked += (_, _) => _protocolMode = "UDP";
        rbProtocolBoth.Checked += (_, _) => _protocolMode = "BOTH";
    }

    private bool FilterRunningProcess(object obj)
    {
        if (obj is not ProcessItem item) return false;
        var searchText = txtSearch.Text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(searchText)) return true;
        return item.Name.ToLowerInvariant().Contains(searchText) ||
               item.DisplayName.ToLowerInvariant().Contains(searchText);
    }

    private bool FilterActiveProcess(object obj)
    {
        if (obj is not string procName) return false;
        var searchText = txtActiveSearch.Text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(searchText)) return true;
        return procName.ToLowerInvariant().Contains(searchText);
    }

    private void CheckDriverStatus()
    {
        try
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge", "WinDivert.dll");
            var sysPath = Path.Combine(AppContext.BaseDirectory, "bin", "NetBridge", "WinDivert64.sys");

            txtDriverFile.Text = File.Exists(dllPath) && File.Exists(sysPath)
                ? "• 驱动文件: ✓ 已就绪"
                : "• 驱动文件: ✗ 缺失";

            txtDriverService.Text = NetBridgeManager.Instance.IsRunning
                ? "• 驱动服务: ✓ 已加载"
                : "• 驱动服务: ○ 未加载";

            txtNetBridgeStatus.Text = NetBridgeManager.Instance.IsRunning
                ? "• NetBridge: ✓ 运行中"
                : "• NetBridge: ○ 未启动";
        }
        catch
        {
            txtDriverFile.Text = "• 驱动文件: ? 检查失败";
            txtDriverService.Text = "• 驱动服务: ? 检查失败";
            txtNetBridgeStatus.Text = "• NetBridge: ? 检查失败";
        }
    }

    private void LoadProcesses()
    {
        _processItems.Clear();

        try
        {
            var activeSet = _activeProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var processes = Process.GetProcesses()
                .Where(p =>
                {
                    try { return !string.IsNullOrEmpty(p.ProcessName); }
                    catch { return false; }
                })
                .GroupBy(p => p.ProcessName.ToLowerInvariant())
                .Select(g =>
                {
                    var procName = g.Key + ".exe";
                    var (appName, filePath) = GetProcessInfo(procName);
                    return new ProcessItem
                    {
                        Name = procName,
                        DisplayName = appName != null ? $"{procName}（{appName}）" : procName,
                        IsSelected = false,
                        IsActive = activeSet.Contains(procName),
                        Icon = filePath != null ? GetFileIcon(filePath) : null
                    };
                })
                .OrderBy(p => p.Name)
                .Take(200)
                .ToList();

            _processItems.AddRange(processes);
        }
        catch { }

        _processView.Refresh();
        UpdateStatus();
    }

    private static (string? appName, string? filePath) GetProcessInfo(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
            foreach (var proc in processes)
            {
                try
                {
                    var mainModule = proc.MainModule;
                    if (mainModule != null)
                    {
                        var filePath = mainModule.FileName;
                        var fileVersion = mainModule.FileVersionInfo;
                        var appName = !string.IsNullOrEmpty(fileVersion.ProductName)
                            ? fileVersion.ProductName
                            : fileVersion.FileDescription;
                        return (appName, filePath);
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }

        return (null, null);
    }

    #region Running Process Search

    private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        txtSearchWatermark.Visibility = string.IsNullOrEmpty(txtSearch.Text)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        _processView.Refresh();
    }

    private void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
        }
    }

    #endregion

    #region Active Process Search

    private void TxtActiveSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        txtActiveSearchWatermark.Visibility = string.IsNullOrEmpty(txtActiveSearch.Text)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        _activeView.Refresh();
    }

    private void TxtActiveSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
        }
    }

    #endregion

    #region Actions

    private void RefreshActiveList()
    {
        _activeView.Refresh();
        UpdateStatus();
    }

    private void SelectAllRunning(bool select)
    {
        foreach (var item in _processItems)
        {
            item.IsSelected = select;
        }
        _processView.Refresh();
    }

    private void BtnAddManual_Click(object sender, RoutedEventArgs e)
    {
        var searchText = txtActiveSearch.Text?.Trim();
        if (string.IsNullOrEmpty(searchText)) return;

        var processNames = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var addedCount = 0;
        foreach (var proc in processNames)
        {
            var name = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proc : proc + ".exe";
            if (!_activeProcesses.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProcesses.Add(name);
                addedCount++;
            }
        }

        _sessionAddedCount += addedCount;
        _activeView.Refresh();
        _processView.Refresh();
        UpdateStatus();

        txtActiveSearch.Text = string.Empty;

        if (addedCount > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.ProcessListPresetAdded, addedCount));
        }
    }

    private void BtnAddSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _processItems
            .Where(p => p.IsSelected && !p.IsActive)
            .Select(p => p.Name)
            .ToList();

        var addedCount = 0;
        foreach (var proc in selected)
        {
            if (!_activeProcesses.Any(a => string.Equals(a, proc, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProcesses.Add(proc);
                addedCount++;
            }
        }

        foreach (var item in _processItems.Where(p => p.IsSelected))
        {
            item.IsSelected = false;
            item.IsActive = true;
        }

        _sessionAddedCount += addedCount;
        _processView.Refresh();
        _activeView.Refresh();
        UpdateStatus();
    }

    private void AddPresetProcesses(List<string> presetList)
    {
        var addedCount = 0;
        foreach (var proc in presetList)
        {
            if (!_activeProcesses.Any(a => string.Equals(a, proc, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProcesses.Add(proc);
                addedCount++;
            }
        }

        _sessionAddedCount += addedCount;
        _activeView.Refresh();
        _processView.Refresh();
        UpdateStatus();

        if (addedCount > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.ProcessListPresetAdded, addedCount));
        }
    }

    private void BtnImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择包含 EXE 文件的文件夹",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var folderPath = dialog.SelectedPath;
        var exeFiles = ScanFolderForExe(folderPath);

        var totalFound = exeFiles.Count;
        var addedCount = 0;
        foreach (var proc in exeFiles)
        {
            if (!_activeProcesses.Any(a => string.Equals(a, proc, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProcesses.Add(proc);
                addedCount++;
            }
        }

        _sessionAddedCount += addedCount;
        _activeView.Refresh();
        _processView.Refresh();
        UpdateStatus();

        NoticeManager.Instance.Enqueue(string.Format(ResUI.ProcessListImportResult, totalFound, addedCount));
    }

    private static List<string> ScanFolderForExe(string folderPath)
    {
        try
        {
            return Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            NoticeManager.Instance.Enqueue("权限不足，无法访问部分文件夹");
            return [];
        }
        catch (Exception ex)
        {
            NoticeManager.Instance.Enqueue($"扫描文件夹失败: {ex.Message}");
            return [];
        }
    }

    private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = lstActiveProcesses.SelectedItems.Cast<string>().ToList();
        foreach (var proc in selectedItems)
        {
            _activeProcesses.RemoveAll(a => string.Equals(a, proc, StringComparison.OrdinalIgnoreCase));
        }

        _activeView.Refresh();
        _processView.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        txtStatus.Text = $"目标生效: {_activeProcesses.Count} 个进程" +
                         (_sessionAddedCount > 0 ? $" (本次添加: {_sessionAddedCount})" : "");
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ResultProcessList = string.Join(",", _activeProcesses);
        ResultDnsViaBridge = chkDnsViaBridge.IsChecked == true;
        DialogResult = true;
    }

    #endregion

    #region Icon Extraction

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x00000100;
    private const uint SHGFI_SMALLICON = 0x00000001;

    private static readonly Dictionary<string, BitmapSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private static BitmapSource? GetFileIcon(string filePath)
    {
        if (_iconCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        BitmapSource? result = null;
        var shInfo = new SHFILEINFO();
        var hIcon = SHGetFileInfo(filePath, 0, out shInfo,
            (uint)Marshal.SizeOf(shInfo), SHGFI_ICON | SHGFI_SMALLICON);

        if (hIcon != IntPtr.Zero)
        {
            try
            {
                result = Imaging.CreateBitmapSourceFromHIcon(
                    shInfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
            }
            finally
            {
                DestroyIcon(shInfo.hIcon);
            }
        }

        if (_iconCache.Count < 300)
        {
            _iconCache[filePath] = result;
        }

        return result;
    }

    #endregion

    private class ProcessItem
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsSelected { get; set; }
        public bool IsActive { get; set; }
        public BitmapSource? Icon { get; set; }
    }
}
