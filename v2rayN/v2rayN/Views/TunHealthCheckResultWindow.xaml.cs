using System.Windows;
using System.Windows.Media;
using ServiceLib.HealthCheck;
using ServiceLib.HealthCheck.Models;
using ServiceLib.Resx;

namespace v2rayN.Views;

public partial class TunHealthCheckResultWindow : Window
{
    private readonly HealthCheckReport _report;
    private readonly List<FixItemDisplay> _fixItems = [];
    private bool _isZh;

    public TunHealthCheckResultWindow(HealthCheckReport report)
    {
        InitializeComponent();
        _report = report;
        _isZh = (AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en")
            .StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        btnClose.Click += (_, _) => Close();
        btnCopy.Click += BtnCopy_Click;
        btnExport.Click += BtnExport_Click;
        btnOneClickFix.Click += BtnOneClickFix_Click;
        btnSelectAllFixes.Click += (_, _) =>
        {
            foreach (var item in _fixItems)
            {
                item.IsSelected = true;
            }
            lstFixes.ItemsSource = null; lstFixes.ItemsSource = _fixItems;
        };

        PopulateReport();
    }


    private static Brush BrushFromResource(string key, Brush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // ignore resource lookup failures during early init
        }

        return fallback;
    }
    private void PopulateReport()
    {
        var overallColor = _report.OverallStatus switch
        {
            HealthCheckOverallStatus.AllPass => BrushFromResource("DesignSignalBrush", Brushes.Green),
            HealthCheckOverallStatus.HasWarning => BrushFromResource("DesignWarnBrush", new SolidColorBrush(Color.FromRgb(0xC4, 0x7B, 0x12))),
            HealthCheckOverallStatus.HasFailure => BrushFromResource("DesignDangerBrush", Brushes.Red),
            _ => BrushFromResource("DesignInk3Brush", Brushes.Gray)
        };

        var overallText = _report.OverallStatus switch
        {
            HealthCheckOverallStatus.AllPass => $"  {ResUI.TunHealthCheckAllPassed}",
            HealthCheckOverallStatus.HasWarning => $"  {string.Format(ResUI.TunHealthCheckHasWarning, _report.Results.Count(r => r.Status == HealthCheckStatus.Warning))}",
            HealthCheckOverallStatus.HasFailure => $"  {string.Format(ResUI.TunHealthCheckHasFailure, _report.Results.Count(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error))}",
            _ => ""
        };

        txtOverallStatus.Text = overallText;
        txtOverallStatus.Foreground = overallColor;
        txtDuration.Text = string.Format(ResUI.TunHealthCheckDuration, $"{_report.TotalDuration.TotalMilliseconds:F0}");

        var scores = _report.Results
            .Where(r => r.Details != null && r.Details.TryGetValue("health_score", out _))
            .Select(r => (int)r.Details!["health_score"])
            .ToList();

        if (scores.Count > 0)
        {
            var avg = (int)scores.Average();
            txtScoreLabel.Text = string.Format(ResUI.TunHealthCheckScore, avg, GradeFromScore(avg)).Split(':')[0] + ":";
            txtScore.Text = $"{avg}/100 ({GradeFromScore(avg)})";
            txtScore.Foreground = avg >= 80 ? BrushFromResource("DesignSignalBrush", Brushes.Green) : avg >= 50 ? BrushFromResource("DesignWarnBrush", new SolidColorBrush(Color.FromRgb(0xC4, 0x7B, 0x12))) : BrushFromResource("DesignDangerBrush", Brushes.Red);
        }
        else
        {
            txtScoreLabel.Text = "";
            txtScore.Text = "";
        }

        var layers = _report.Results.Select(r => new LayerResultDisplay
        {
            LayerName = GetLayerName(r.Layer),
            StatusText = r.Status switch
            {
                HealthCheckStatus.Pass => ResUI.TunHealthCheckPass,
                HealthCheckStatus.Warning => ResUI.TunHealthCheckWarning,
                HealthCheckStatus.Fail => ResUI.TunHealthCheckFail,
                HealthCheckStatus.Skipped => ResUI.TunHealthCheckSkipped,
                HealthCheckStatus.Error => ResUI.TunHealthCheckError,
                _ => ""
            },
            StatusColor = r.Status switch
            {
                HealthCheckStatus.Pass => BrushFromResource("DesignSignalBrush", Brushes.Green),
                HealthCheckStatus.Warning => BrushFromResource("DesignWarnBrush", new SolidColorBrush(Color.FromRgb(0xC4, 0x7B, 0x12))),
                HealthCheckStatus.Fail => BrushFromResource("DesignDangerBrush", Brushes.Red),
                HealthCheckStatus.Skipped => BrushFromResource("DesignInk3Brush", Brushes.Gray),
                HealthCheckStatus.Error => BrushFromResource("DesignDangerBrush", Brushes.Red),
                _ => BrushFromResource("DesignInk3Brush", Brushes.Gray)
            },
            Duration = $"{r.Duration.TotalMilliseconds:F0}ms",
            IsExpanded = r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Warning,
            Details = FormatDetails(r)
        }).ToList();

        lstLayers.ItemsSource = layers;

        if (_report.Diagnosis != null && _report.Diagnosis.Count > 0)
        {
            cardDiagnosis.Visibility = Visibility.Visible;
            lstDiagnosis.ItemsSource = _report.Diagnosis;
        }

        PopulateFixes();
    }

    private void PopulateFixes()
    {
        _fixItems.Clear();
        var fixes = _report.AvailableFixes;
        if (fixes == null || fixes.Count == 0)
        {
            cardFixes.Visibility = Visibility.Collapsed;
            btnOneClickFix.IsEnabled = false;
            btnOneClickFix.ToolTip = ResUI.TunHealthCheckNoFixes;
            return;
        }

        foreach (var fix in fixes)
        {
            var tags = new List<string>();
            if (fix.RequiresAdmin) tags.Add(ResUI.TunHealthCheckFixRequiresAdmin);
            if (fix.RequiresReload) tags.Add(ResUI.TunHealthCheckFixRequiresReload);
            var tagText = tags.Count > 0 ? " " + string.Join(" ", tags) : "";
            _fixItems.Add(new FixItemDisplay
            {
                Id = fix.Id,
                IsSelected = fix.IsSafeAuto,
                DisplayText = $"{fix.Title(_isZh)}{tagText} - {fix.Description(_isZh)}"
            });
        }

        cardFixes.Visibility = Visibility.Visible;
        lstFixes.ItemsSource = _fixItems;
        btnOneClickFix.IsEnabled = true;
    }

    private async void BtnOneClickFix_Click(object sender, RoutedEventArgs e)
    {
        var selected = _fixItems.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (selected.Count == 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.TunHealthCheckNoFixes);
            return;
        }

        var confirm = MessageBox.Show(
            ResUI.TunHealthCheckFixConfirm,
            ResUI.TunHealthCheckOneClickFix,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        btnOneClickFix.IsEnabled = false;
        try
        {
            var config = AppManager.Instance.Config;
            if (config == null)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }

            var service = new HealthCheckAutoFixService(config);
            var results = await service.ApplyAsync(selected);

            var ok = results.Count(r => r.Success);
            var fail = results.Count(r => !r.Success && !r.Skipped);
            var msg = fail == 0
                ? string.Format(ResUI.TunHealthCheckFixApplied, ok)
                : string.Format(ResUI.TunHealthCheckFixPartial, ok, fail);

            var detail = string.Join("\n", results.Select(r =>
                $"{(r.Success ? (r.Skipped ? "~" : "+") : "x")} {r.Message(_isZh)}"));
            NoticeManager.Instance.Enqueue(msg);
            MessageBox.Show(
                $"{msg}\n\n{detail}",
                ResUI.TunHealthCheckFixDone,
                MessageBoxButton.OK,
                fail == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            NoticeManager.Instance.Enqueue($"{ResUI.OperationFailed}: {ex.Message}");
        }
        finally
        {
            btnOneClickFix.IsEnabled = true;
        }
    }

    private static List<DetailItem> FormatDetails(HealthCheckResult result)
    {
        var items = new List<DetailItem>
        {
            new() { Key = ResUI.TunHealthCheckSummary, Value = result.Summary }
        };

        if (result.Details != null)
        {
            foreach (var kvp in result.Details)
            {
                if (kvp.Key == "health_score") continue;

                var value = kvp.Value switch
                {
                    bool b => b ? ResUI.TunHealthCheckYes : ResUI.TunHealthCheckNo,
                    double d => d.ToString("F2"),
                    int i => i.ToString(),
                    _ => kvp.Value?.ToString() ?? ""
                };
                items.Add(new DetailItem { Key = FormatDetailKey(kvp.Key), Value = value });
            }
        }

        return items;
    }

    private static string FormatDetailKey(string key) => key switch
    {
        "adapter" => "Adapter",
        "ipv4" => "IPv4",
        "ipv6" => "IPv6",
        "has_default_route" => "Default route",
        "proxy_port" => "Proxy port",
        "mode" => "Mode",
        "exit_ip" => "Exit IP",
        _ => key
    };

    private static string GradeFromScore(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "F"
    };

    private static string GetLayerName(string layer) => layer switch
    {
        "TUN Interface" => ResUI.TunHealthCheckLayerTunInterface,
        "DNS" => ResUI.TunHealthCheckLayerDns,
        "Routing" => ResUI.TunHealthCheckLayerRouting,
        "Outbound" => ResUI.TunHealthCheckLayerOutbound,
        "Website Access" => ResUI.TunHealthCheckLayerWebsite,
        "Quality" => ResUI.TunHealthCheckLayerQuality,
        _ => layer
    };

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var locale = AppManager.Instance.Config?.UiItem?.CurrentLanguage ?? "en";
        var text = TunHealthCheckService.FormatReport(_report, locale);
        Clipboard.SetText(text);
        NoticeManager.Instance.Enqueue(ResUI.TunHealthCheckCopyOk);
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var jsonPath = Path.Combine(Utils.GetLogPath(), $"tun-health-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var json = TunHealthCheckService.ExportJson(_report);
            File.WriteAllText(jsonPath, json);
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TunHealthCheckReportExported, jsonPath));
        }
        catch (Exception ex)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TunHealthCheckExportFailed, ex.Message));
        }
    }

    private class LayerResultDisplay
    {
        public string LayerName { get; set; } = "";
        public string StatusText { get; set; } = "";
        public Brush StatusColor { get; set; } = BrushFromResource("DesignInk3Brush", Brushes.Gray);
        public string Duration { get; set; } = "";
        public bool IsExpanded { get; set; }
        public List<DetailItem> Details { get; set; } = [];
    }

    private class DetailItem
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private class FixItemDisplay
    {
        public HealthCheckFixId Id { get; set; }
        public bool IsSelected { get; set; }
        public string DisplayText { get; set; } = "";
    }
}
