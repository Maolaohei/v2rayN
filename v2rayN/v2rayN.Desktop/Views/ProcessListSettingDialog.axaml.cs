using System.Diagnostics;
using Avalonia.Collections;
using Avalonia.Platform.Storage;
using ServiceLib.Manager;
using ServiceLib.Resx;

namespace v2rayN.Desktop.Views;

public partial class ProcessListSettingDialog : Window
{
    private readonly List<ProcessItem> _processItems = [];
    private readonly List<string> _activeProcesses = [];
    private readonly DataGridCollectionView _processView;
    private readonly DataGridCollectionView _activeView;
    private bool _dnsViaBridge;
    private int _sessionAddedCount;

    private static readonly List<string> PresetBrowsers =
        ["chrome.exe", "firefox.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe"];

    private static readonly List<string> PresetDevTools =
        ["code.exe", "cursor.exe", "git.exe", "node.exe", "python.exe", "dotnet.exe",
         "claude.exe", "curl.exe", "wget.exe", "powershell.exe", "pwsh.exe"];

    public string ResultProcessList { get; private set; } = "";
    public bool ResultDnsViaBridge { get; private set; }

    public ProcessListSettingDialog()
        : this(string.Empty, false)
    {
    }

    public ProcessListSettingDialog(string processList, bool dnsViaBridge)
    {
        InitializeComponent();

        _dnsViaBridge = dnsViaBridge;
        chkDnsViaBridge.IsChecked = dnsViaBridge;

        var existingProcesses = processList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _activeProcesses.AddRange(existingProcesses.Distinct(StringComparer.OrdinalIgnoreCase));

        _processView = new DataGridCollectionView(_processItems);
        _processView.Filter = FilterRunningProcess;
        lstProcesses.ItemsSource = _processView;

        _activeView = new DataGridCollectionView(_activeProcesses);
        _activeView.Filter = FilterActiveProcess;
        lstActiveProcesses.ItemsSource = _activeView;

        LoadProcesses();
        RefreshActiveList();
        CheckDriverStatus();

        btnRefresh.Click += (_, _) => LoadProcesses();
        chkSelectAll.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "IsChecked")
            {
                foreach (var item in _processItems)
                {
                    item.IsSelected = chkSelectAll.IsChecked == true;
                }
                _processView.Refresh();
            }
        };
        btnAddSelected.Click += BtnAddSelected_Click;
        btnAddManual.Click += BtnAddManual_Click;
        btnRemoveSelected.Click += BtnRemoveSelected_Click;
        btnPresetBrowser.Click += (_, _) => AddPresetProcesses(PresetBrowsers);
        btnPresetDev.Click += (_, _) => AddPresetProcesses(PresetDevTools);
        btnPresetAll.Click += (_, _) => AddPresetProcesses(PresetBrowsers.Concat(PresetDevTools).ToList());
        btnImportFolder.Click += BtnImportFolder_Click;
        btnOk.Click += BtnOk_Click;
        btnCancel.Click += BtnCancel_Click;

        CanMinimize = false;
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
                    var appName = GetApplicationName(procName);
                    return new ProcessItem
                    {
                        Name = procName,
                        DisplayName = appName != null ? $"{procName}（{appName}）" : procName,
                        IsSelected = false,
                        IsActive = activeSet.Contains(procName)
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

    private static string? GetApplicationName(string processName)
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
                        var fileVersion = mainModule.FileVersionInfo;
                        if (!string.IsNullOrEmpty(fileVersion.ProductName))
                        {
                            return fileVersion.ProductName;
                        }
                        if (!string.IsNullOrEmpty(fileVersion.FileDescription))
                        {
                            return fileVersion.FileDescription;
                        }
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

        return null;
    }

    #region Running Process Search

    private void TxtSearch_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        txtSearchWatermark.IsVisible = string.IsNullOrEmpty(txtSearch.Text);
        _processView.Refresh();
    }

    private void TxtSearch_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Return)
        {
            e.Handled = true;
        }
    }

    #endregion

    #region Active Process Search

    private void TxtActiveSearch_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        txtActiveSearchWatermark.IsVisible = string.IsNullOrEmpty(txtActiveSearch.Text);
        _activeView.Refresh();
    }

    private void TxtActiveSearch_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Return)
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

    private void BtnAddManual_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private void BtnAddSelected_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private async void BtnImportFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(folderPath)) return;

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

    private void BtnRemoveSelected_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
        var statusText = this.Find<TextBlock>("txtStatus");
        if (statusText != null)
        {
            statusText.Text = $"目标生效: {_activeProcesses.Count} 个进程" +
                              (_sessionAddedCount > 0 ? $" (本次添加: {_sessionAddedCount})" : "");
        }
    }

    private void BtnOk_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ResultProcessList = string.Join(",", _activeProcesses);
        ResultDnsViaBridge = chkDnsViaBridge.IsChecked == true;
        Close(ResultProcessList);
    }

    private void BtnCancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    #endregion

    private class ProcessItem
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsSelected { get; set; }
        public bool IsActive { get; set; }
    }
}
