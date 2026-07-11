using System.Windows.Media;
using ServiceLib.HealthCheck;
using ServiceLib.HealthCheck.Models;
using ServiceLib.Resx;

namespace v2rayN.Views;

public partial class TunHealthCheckResultWindow : Window
{
    private readonly HealthCheckReport _report;

    public TunHealthCheckResultWindow(HealthCheckReport report)
    {
        InitializeComponent();
        _report = report;

        btnClose.Click += (_, _) => Close();
        btnCopy.Click += BtnCopy_Click;
        btnExport.Click += BtnExport_Click;

        PopulateReport();
    }

    private void PopulateReport()
    {
        var overallColor = _report.OverallStatus switch
        {
            HealthCheckOverallStatus.AllPass => Brushes.Green,
            HealthCheckOverallStatus.HasWarning => new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)),
            HealthCheckOverallStatus.HasFailure => Brushes.Red,
            _ => Brushes.Gray
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
            // Prefer score body only for value text
            txtScore.Text = $"{avg}/100 ({GradeFromScore(avg)})";
            txtScore.Foreground = avg >= 80 ? Brushes.Green : avg >= 50 ? new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)) : Brushes.Red;
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
                HealthCheckStatus.Pass => Brushes.Green,
                HealthCheckStatus.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)),
                HealthCheckStatus.Fail => Brushes.Red,
                HealthCheckStatus.Skipped => Brushes.Gray,
                HealthCheckStatus.Error => Brushes.Red,
                _ => Brushes.Gray
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
        "hop_limit" => "Hop limit",
        "dns_server" => "DNS server",
        "dns_port" => "DNS port",
        "doh_result" => "DoH",
        "system_dns" => "System DNS",
        "dns_leak_detected" => "DNS leak",
        "direct_domains" => "Direct domains",
        "proxy_domains" => "Proxy domains",
        "loop_detected" => "Loop",
        "tcp_ok" => "TCP",
        "tls_ok" => "TLS",
        "http_204" => "HTTP 204",
        "exit_ip" => "Exit IP",
        "latency_ms" => "Latency",
        "packet_loss" => "Loss",
        "jitter" => "Jitter",
        "website_results" => "Websites",
        "proxy_port" => "Proxy port",
        "mode" => "Mode",
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
        public Brush StatusColor { get; set; } = Brushes.Gray;
        public string Duration { get; set; } = "";
        public bool IsExpanded { get; set; }
        public List<DetailItem> Details { get; set; } = [];
    }

    private class DetailItem
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
