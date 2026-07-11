using System.Diagnostics;
using System.Text.Json;
using ServiceLib.HealthCheck.Checks;
using ServiceLib.HealthCheck.Models;
using ServiceLib.Resx;

namespace ServiceLib.HealthCheck;

public class TunHealthCheckService
{
    private readonly Config? _config;

    public TunHealthCheckService(Config? config = null)
    {
        _config = config;
    }

    private bool IsZh
    {
        get
        {
            var locale = _config?.UiItem?.CurrentLanguage
                ?? System.Globalization.CultureInfo.CurrentUICulture.Name;
            return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    private string T(string en, string zh) => IsZh ? zh : en;

    public async Task<HealthCheckReport> RunFullCheckAsync(Func<string, Task>? progressFunc = null)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<HealthCheckResult>();
        var tunEnabled = _config?.TunModeItem?.EnableTun == true;
        var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);

        await ReportProgress(progressFunc, T(
            "Layer 1: Checking TUN interface...",
            "第 1 层：检查 TUN 接口..."));
        var layer1 = await RunCheckSafeAsync(() => new TunInterfaceCheck().CheckAsync());

        if (!tunEnabled)
        {
            // Not a hard failure: allow proxy-path diagnosis when TUN is off.
            if (layer1.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)
            {
                var details = new Dictionary<string, object>();
                if (layer1.Details != null)
                {
                    foreach (var kv in layer1.Details)
                    {
                        details[kv.Key] = kv.Value;
                    }
                }
                details["mode"] = "non_tun";
                details["adapter_status"] = layer1.Summary;
                results.Add(new HealthCheckResult(
                    "TUN Interface",
                    HealthCheckStatus.Skipped,
                    T("TUN is not enabled - adapter check skipped", "未开启 TUN - 已跳过接口检查"),
                    layer1.Duration,
                    details));
            }
            else
            {
                results.Add(layer1);
            }

            await ReportProgress(progressFunc, T(
                "TUN off: running proxy-path checks...",
                "未开启 TUN：正在检查代理链路..."));
            var proxyOnly = await Task.WhenAll(
                RunCheckSafeAsync(() => new DnsCheck().CheckAsync()),
                RunCheckSafeAsync(() => new RoutingCheck().CheckAsync(socksPort, tunEnabled: false, _config)),
                RunCheckSafeAsync(() => new OutboundCheck().CheckAsync(socksPort))
            );
            results.AddRange(proxyOnly);

            var hasCriticalFailure = proxyOnly.Any(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error);
            if (hasCriticalFailure)
            {
                results.Add(SkippedResult("Website Access",
                    T("Skipped - upstream layers have failures", "已跳过 - 上游检查失败")));
                results.Add(SkippedResult("Quality",
                    T("Skipped - upstream layers have failures", "已跳过 - 上游检查失败")));
            }
            else
            {
                await ReportProgress(progressFunc, T(
                    "Checking website access via SOCKS...",
                    "正在通过 SOCKS 检查网站访问..."));
                var layer5 = await RunCheckSafeAsync(() => new WebsiteCheck().CheckAsync(socksPort, tunEnabled: false));
                results.Add(layer5);

                if (layer5.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)
                {
                    results.Add(SkippedResult("Quality",
                        T("Skipped - website access failed, latency data unreliable",
                          "已跳过 - 网站访问失败，延迟数据不可靠")));
                }
                else
                {
                    await ReportProgress(progressFunc, T(
                        "Checking connection quality via SOCKS...",
                        "正在通过 SOCKS 检查连接质量..."));
                    var layer6 = await RunCheckSafeAsync(() => new QualityCheck().CheckAsync(socksPort));
                    results.Add(layer6);
                }
            }

            sw.Stop();
            return BuildReport(results, sw.Elapsed);
        }

        results.Add(layer1);

        if (layer1.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)
        {
            await ReportProgress(progressFunc, T(
                "TUN interface failed, skipping dependent layers...",
                "TUN 接口失败，跳过后续检查..."));
            results.Add(SkippedResult("DNS", T("Skipped - TUN interface not available", "已跳过 - TUN 接口不可用")));
            results.Add(SkippedResult("Routing", T("Skipped - TUN interface not available", "已跳过 - TUN 接口不可用")));
            results.Add(SkippedResult("Outbound", T("Skipped - TUN interface not available", "已跳过 - TUN 接口不可用")));
            results.Add(SkippedResult("Website Access", T("Skipped - TUN interface not available", "已跳过 - TUN 接口不可用")));
            results.Add(SkippedResult("Quality", T("Skipped - TUN interface not available", "已跳过 - TUN 接口不可用")));
            sw.Stop();
            return BuildReport(results, sw.Elapsed);
        }

        await ReportProgress(progressFunc, T(
            "Layers 2-4: Running parallel checks...",
            "第 2-4 层：并行检查 DNS / 路由 / 出站..."));
        var layer24 = await Task.WhenAll(
            RunCheckSafeAsync(() => new DnsCheck().CheckAsync()),
            RunCheckSafeAsync(() => new RoutingCheck().CheckAsync(socksPort, tunEnabled: true, _config)),
            RunCheckSafeAsync(() => new OutboundCheck().CheckAsync(socksPort))
        );
        results.AddRange(layer24);

        var hasCritical = layer24.Any(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error);

        await ReportProgress(progressFunc, T(
            "Layer 5: Checking website access via SOCKS...",
            "第 5 层：通过 SOCKS 检查网站访问..."));
        if (hasCritical)
        {
            results.Add(SkippedResult("Website Access",
                T("Skipped - upstream layers have failures", "已跳过 - 上游检查失败")));
            results.Add(SkippedResult("Quality",
                T("Skipped - upstream layers have failures", "已跳过 - 上游检查失败")));
            sw.Stop();
            return BuildReport(results, sw.Elapsed);
        }

        var layer5Tun = await RunCheckSafeAsync(() => new WebsiteCheck().CheckAsync(socksPort, tunEnabled: true));
        results.Add(layer5Tun);

        if (layer5Tun.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)
        {
            await ReportProgress(progressFunc, T(
                "Website access failed, skipping quality test...",
                "网站访问失败，跳过质量检测..."));
            results.Add(SkippedResult("Quality",
                T("Skipped - website access failed, latency data unreliable",
                  "已跳过 - 网站访问失败，延迟数据不可靠")));
            sw.Stop();
            return BuildReport(results, sw.Elapsed);
        }

        await ReportProgress(progressFunc, T(
            "Layer 6: Checking connection quality via SOCKS...",
            "第 6 层：通过 SOCKS 检查连接质量..."));
        var layer6Tun = await RunCheckSafeAsync(() => new QualityCheck().CheckAsync(socksPort));
        results.Add(layer6Tun);

        sw.Stop();
        return BuildReport(results, sw.Elapsed);
    }

    private static async Task<HealthCheckResult> RunCheckSafeAsync(Func<Task<HealthCheckResult>> checkFunc)
    {
        try
        {
            return await checkFunc();
        }
        catch (Exception ex)
        {
            var sw = Stopwatch.StartNew();
            sw.Stop();
            return new HealthCheckResult("Unknown", HealthCheckStatus.Error,
                $"Check crashed: {ex.Message}", sw.Elapsed,
                new Dictionary<string, object> { ["exception"] = ex.GetType().Name, ["error"] = ex.Message });
        }
    }

    private static HealthCheckResult SkippedResult(string layer, string reason)
    {
        return new HealthCheckResult(layer, HealthCheckStatus.Skipped, reason, TimeSpan.Zero);
    }

    private HealthCheckReport BuildReport(List<HealthCheckResult> results, TimeSpan totalDuration)
    {
        var overall = DetermineOverall(results);
        var report = new HealthCheckReport(overall, results, totalDuration);
        var locale = _config?.UiItem?.CurrentLanguage
            ?? System.Globalization.CultureInfo.CurrentUICulture.Name;
        var diagnosis = DiagnosisEngine.Diagnose(report, locale);
        var fixes = DiagnosisEngine.CollectFixes(report);
        return report with { Diagnosis = diagnosis, AvailableFixes = fixes };
    }

    public async Task<HealthCheckResult> RunSingleCheckAsync(string layer, Func<string, Task>? progressFunc = null)
    {
        await ReportProgress(progressFunc, T($"Running {layer} check...", $"正在运行 {layer} 检查..."));
        var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var tunEnabled = _config?.TunModeItem?.EnableTun == true;

        return layer.ToLowerInvariant() switch
        {
            "tun" or "tun interface" or "interface" => await RunCheckSafeAsync(() => new TunInterfaceCheck().CheckAsync()),
            "dns" => await RunCheckSafeAsync(() => new DnsCheck().CheckAsync()),
            "routing" or "route" => await RunCheckSafeAsync(() => new RoutingCheck().CheckAsync(socksPort, tunEnabled, _config)),
            "outbound" or "connection" => await RunCheckSafeAsync(() => new OutboundCheck().CheckAsync(socksPort)),
            "website" or "web" => await RunCheckSafeAsync(() => new WebsiteCheck().CheckAsync(socksPort, tunEnabled)),
            "quality" or "latency" => await RunCheckSafeAsync(() => new QualityCheck().CheckAsync(socksPort)),
            _ => new HealthCheckResult(layer, HealthCheckStatus.Error, $"Unknown layer: {layer}", TimeSpan.Zero)
        };
    }

    public static string FormatReport(HealthCheckReport report, string locale = "en")
    {
        return locale.StartsWith("zh")
            ? FormatReportChinese(report)
            : FormatReportEnglish(report);
    }

    public static string FormatReportEnglish(HealthCheckReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("       TUN Health Check Report");
        sb.AppendLine("========================================");
        sb.AppendLine();

        var scores = new List<int>();
        foreach (var r in report.Results)
        {
            var (icon, statusText) = r.Status switch
            {
                HealthCheckStatus.Pass => ("  +  ", "Pass"),
                HealthCheckStatus.Warning => ("  !  ", "Warning"),
                HealthCheckStatus.Fail => ("  x  ", "Fail"),
                HealthCheckStatus.Skipped => ("  -  ", "Skipped"),
                HealthCheckStatus.Error => ("  !  ", "Error"),
                _ => ("  ?  ", "")
            };

            var layerName = r.Layer.PadRight(16);
            sb.Append($"{icon}{layerName}  {statusText}  ({r.Duration.TotalMilliseconds:F0}ms)");

            if (r.Details != null && r.Details.TryGetValue("health_score", out var scoreObj)
                && scoreObj is int score)
            {
                sb.Append($"  {score}/100 ({GradeFromScore(score)})");
                scores.Add(score);
            }
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(r.Summary))
            {
                sb.AppendLine($"      {r.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"  Overall: {report.OverallStatus}  ({report.TotalDuration.TotalMilliseconds:F0}ms)");

        if (scores.Count > 0)
        {
            var avg = (int)scores.Average();
            sb.AppendLine($"  Health Score: {avg}/100 ({GradeFromScore(avg)})");
        }

        AppendDiagnosis(sb, report.Diagnosis);
        AppendFixes(sb, report.AvailableFixes, false);

        sb.AppendLine("========================================");
        return sb.ToString();
    }

    public static string FormatReportChinese(HealthCheckReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"  {ResUI.TunHealthCheckTitle}");
        sb.AppendLine("========================================");
        sb.AppendLine();

        var scores = new List<int>();
        foreach (var r in report.Results)
        {
            var (icon, statusText) = r.Status switch
            {
                HealthCheckStatus.Pass => ("  +  ", ResUI.TunHealthCheckPass),
                HealthCheckStatus.Warning => ("  !  ", ResUI.TunHealthCheckWarning),
                HealthCheckStatus.Fail => ("  x  ", ResUI.TunHealthCheckFail),
                HealthCheckStatus.Skipped => ("  -  ", ResUI.TunHealthCheckSkipped),
                HealthCheckStatus.Error => ("  !  ", ResUI.TunHealthCheckError),
                _ => ("  ?  ", "")
            };

            var layerName = GetLayerNameChinese(r.Layer).PadRight(16);
            sb.Append($"{icon}{layerName}  {statusText}  ({r.Duration.TotalMilliseconds:F0}ms)");

            if (r.Details != null && r.Details.TryGetValue("health_score", out var scoreObj)
                && scoreObj is int score)
            {
                sb.Append($"  ({score}/100 {GradeFromScore(score)})");
                scores.Add(score);
            }
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(r.Summary))
            {
                sb.AppendLine($"      {r.Summary}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("----------------------------------------");
        var overallText = report.OverallStatus switch
        {
            HealthCheckOverallStatus.AllPass => ResUI.TunHealthCheckAllPassed,
            HealthCheckOverallStatus.HasWarning => string.Format(ResUI.TunHealthCheckHasWarning,
                report.Results.Count(r => r.Status == HealthCheckStatus.Warning)),
            HealthCheckOverallStatus.HasFailure => string.Format(ResUI.TunHealthCheckHasFailure,
                report.Results.Count(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)),
            _ => ""
        };
        sb.AppendLine($"  {overallText}  ({string.Format(ResUI.TunHealthCheckDuration, $"{report.TotalDuration.TotalMilliseconds:F0}")})");

        if (scores.Count > 0)
        {
            var avg = (int)scores.Average();
            sb.AppendLine($"  {string.Format(ResUI.TunHealthCheckScore, avg, GradeFromScore(avg))}");
        }

        AppendDiagnosis(sb, report.Diagnosis, true);
        AppendFixes(sb, report.AvailableFixes, true);

        sb.AppendLine("========================================");
        return sb.ToString();
    }

    private static void AppendDiagnosis(System.Text.StringBuilder sb, List<string>? diagnosis, bool isChinese = false)
    {
        if (diagnosis == null || diagnosis.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"  {(isChinese ? ResUI.TunHealthCheckDiagnosis : "Auto Diagnosis:")}");
        foreach (var line in diagnosis)
        {
            sb.AppendLine($"  {line}");
        }
    }

    private static void AppendFixes(System.Text.StringBuilder sb, List<HealthCheckFixAction>? fixes, bool isChinese)
    {
        if (fixes == null || fixes.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"  {(isChinese ? ResUI.TunHealthCheckAvailableFixes : "Available Fixes:")}");
        foreach (var fix in fixes)
        {
            sb.AppendLine($"  - {fix.Title(isChinese)}: {fix.Description(isChinese)}");
        }
    }

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

    public static string ExportJson(HealthCheckReport report, bool maskSensitive = true)
    {
        var obj = new
        {
            overallStatus = report.OverallStatus.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            results = report.Results.Select(r => new
            {
                layer = r.Layer,
                status = r.Status.ToString(),
                summary = r.Summary,
                durationMs = (int)r.Duration.TotalMilliseconds,
                details = maskSensitive ? MaskSensitiveDetails(r.Details) : r.Details
            }),
            diagnosis = report.Diagnosis ?? [],
            availableFixes = (report.AvailableFixes ?? []).Select(f => f.Id.ToString()),
            timestamp = DateTime.UtcNow.ToString("o")
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static Dictionary<string, object>? MaskSensitiveDetails(IReadOnlyDictionary<string, object>? details)
    {
        if (details == null) return null;

        var masked = new Dictionary<string, object>(details);
        var sensitiveKeys = new[] { "ipv4", "exit_ip", "test_source_ip", "adapter", "server_ip", "server_host" };

        foreach (var key in sensitiveKeys)
        {
            if (masked.TryGetValue(key, out var val) && val is string strVal && !string.IsNullOrEmpty(strVal))
            {
                if (strVal.Contains('.'))
                {
                    var parts = strVal.Split('.');
                    if (parts.Length == 4)
                    {
                        masked[key] = $"{parts[0]}.{parts[1]}.xxx.xxx";
                    }
                }
            }
        }

        return masked;
    }

    private static HealthCheckOverallStatus DetermineOverall(List<HealthCheckResult> results)
    {
        var activeResults = results.Where(r => !r.IsSkipped).ToList();

        if (activeResults.Any(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error))
        {
            return HealthCheckOverallStatus.HasFailure;
        }
        if (activeResults.Any(r => r.Status == HealthCheckStatus.Warning))
        {
            return HealthCheckOverallStatus.HasWarning;
        }
        return HealthCheckOverallStatus.AllPass;
    }

    private static string GradeFromScore(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "F"
    };

    private static async Task ReportProgress(Func<string, Task>? progressFunc, string message)
    {
        if (progressFunc != null)
        {
            await progressFunc(message);
        }
    }
}
