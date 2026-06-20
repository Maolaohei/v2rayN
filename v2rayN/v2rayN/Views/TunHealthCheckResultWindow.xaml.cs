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
            txtScoreLabel.Text = "健康评分:";
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
            LayerName = GetLayerNameChinese(r.Layer),
            StatusText = r.Status switch
            {
                HealthCheckStatus.Pass => ResUI.TunHealthCheckPass,
                HealthCheckStatus.Warning => ResUI.TunHealthCheckWarning,
                HealthCheckStatus.Fail => ResUI.TunHealthCheckFail,
                HealthCheckStatus.Skipped => "已跳过",
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
            new() { Key = "摘要", Value = result.Summary }
        };

        if (result.Details != null)
        {
            foreach (var kvp in result.Details)
            {
                if (kvp.Key == "health_score") continue;

                var value = kvp.Value switch
                {
                    bool b => b ? "是" : "否",
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
        "adapter" => "适配器",
        "ipv4" => "IPv4 地址",
        "ipv6" => "IPv6 地址",
        "has_default_route" => "默认路由",
        "hop_limit" => "跳数限制",
        "dns_server" => "DNS 服务器",
        "dns_port" => "DNS 端口",
        "doh_result" => "DoH 解析",
        "system_dns" => "系统 DNS",
        "dns_leak_detected" => "DNS 泄漏",
        "direct_domains" => "直连域名",
        "proxy_domains" => "代理域名",
        "loop_detected" => "环路检测",
        "tcp_ok" => "TCP 连接",
        "tls_ok" => "TLS 握手",
        "http_204" => "HTTP 204",
        "exit_ip" => "出口 IP",
        "latency_ms" => "延迟",
        "packet_loss" => "丢包率",
        "jitter" => "抖动",
        "website_results" => "网站测试结果",
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

    private static string GetLayerNameChinese(string layer) => layer switch
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
        var text = TunHealthCheckService.FormatReport(_report, "zh");
        Clipboard.SetText(text);
        NoticeManager.Instance.Enqueue("报告已复制到剪贴板");
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var jsonPath = Path.Combine(Utils.GetLogPath(), $"tun-health-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var json = TunHealthCheckService.ExportJson(_report);
            File.WriteAllText(jsonPath, json);
            NoticeManager.Instance.Enqueue($"报告已导出: {jsonPath}");
        }
        catch (Exception ex)
        {
            NoticeManager.Instance.Enqueue($"导出失败: {ex.Message}");
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
