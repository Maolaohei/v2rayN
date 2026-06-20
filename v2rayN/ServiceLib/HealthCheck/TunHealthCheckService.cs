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

    public async Task<HealthCheckReport> RunFullCheckAsync(Func<string, Task>? progressFunc = null)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<HealthCheckResult>();

        await ReportProgress(progressFunc, "Layer 1: Checking TUN interface...");
        var layer1 = await RunCheckSafeAsync(() => new TunInterfaceCheck().CheckAsync());
        results.Add(layer1);

        if (layer1.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)
        {
            await ReportProgress(progressFunc, "TUN interface failed, skipping dependent layers...");
            results.Add(SkippedResult("DNS", "Skipped — TUN interface not available"));
            results.Add(SkippedResult("Routing", "Skipped — TUN interface not available"));
            results.Add(SkippedResult("Outbound", "Skipped — TUN interface not available"));
            results.Add(SkippedResult("Website Access", "Skipped — TUN interface not available"));
            results.Add(SkippedResult("Quality", "Skipped — TUN interface not available"));
            sw.Stop();
            return BuildReport(results, sw.Elapsed);
        }

        await ReportProgress(progressFunc, "Layers 2-4: Running parallel checks...");
        var layer24 = await Task.WhenAll(
            RunCheckSafeAsync(() => new DnsCheck().CheckAsync()),
            RunCheckSafeAsync(() => new RoutingCheck().CheckAsync()),
            RunCheckSafeAsync(() => new OutboundCheck().CheckAsync())
        );
        results.AddRange(layer24);

        var hasCriticalFailure = layer24.Any(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error);

        await ReportProgress(progressFunc, "Layer 5: Checking website access...");
        if (hasCriticalFailure)
        {
            results.Add(SkippedResult("Website Access", "Skipped — upstream layers have failures"));
        }
        else
        {
            var layer5 = await RunCheckSafeAsync(new WebsiteCheck().CheckAsync);
            results.Add(layer5);

            if (layer5.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)
            {
                await ReportProgress(progressFunc, "Website access failed, skipping quality test...");
                results.Add(SkippedResult("Quality", "Skipped — website access failed, latency data unreliable"));
                sw.Stop();
                return BuildReport(results, sw.Elapsed);
            }
        }

        await ReportProgress(progressFunc, "Layer 6: Checking connection quality...");
        var layer6 = await RunCheckSafeAsync(new QualityCheck().CheckAsync);
        results.Add(layer6);

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

    private static HealthCheckReport BuildReport(List<HealthCheckResult> results, TimeSpan totalDuration)
    {
        var overall = DetermineOverall(results);
        var report = new HealthCheckReport(overall, results, totalDuration);
        var diagnosis = DiagnosisEngine.Diagnose(report);
        return report with { Diagnosis = diagnosis };
    }

    public async Task<HealthCheckResult> RunSingleCheckAsync(string layer, Func<string, Task>? progressFunc = null)
    {
        await ReportProgress(progressFunc, $"Running {layer} check...");

        return layer.ToLowerInvariant() switch
        {
            "tun" or "tun interface" or "interface" => await RunCheckSafeAsync(() => new TunInterfaceCheck().CheckAsync()),
            "dns" => await RunCheckSafeAsync(() => new DnsCheck().CheckAsync()),
            "routing" or "route" => await RunCheckSafeAsync(() => new RoutingCheck().CheckAsync()),
            "outbound" or "connection" => await RunCheckSafeAsync(() => new OutboundCheck().CheckAsync()),
            "website" or "web" => await RunCheckSafeAsync(() => new WebsiteCheck().CheckAsync()),
            "quality" or "latency" => await RunCheckSafeAsync(() => new QualityCheck().CheckAsync()),
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
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine("       TUN Health Check Report");
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine();

        var scores = new List<int>();
        foreach (var r in report.Results)
        {
            var (icon, statusText) = r.Status switch
            {
                HealthCheckStatus.Pass => ("  ✓  ", "Pass"),
                HealthCheckStatus.Warning => ("  ⚠  ", "Warning"),
                HealthCheckStatus.Fail => ("  ✗  ", "Fail"),
                HealthCheckStatus.Skipped => ("  ○  ", "Skipped"),
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
        }

        sb.AppendLine();
        sb.AppendLine("───────────────────────────────────────");
        sb.AppendLine($"  Overall: {report.OverallStatus}  ({report.TotalDuration.TotalMilliseconds:F0}ms)");

        if (scores.Count > 0)
        {
            var avg = (int)scores.Average();
            sb.AppendLine($"  Health Score: {avg}/100 ({GradeFromScore(avg)})");
        }

        AppendDiagnosis(sb, report.Diagnosis);

        sb.AppendLine("═══════════════════════════════════════");
        return sb.ToString();
    }

    public static string FormatReportChinese(HealthCheckReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine($"  {ResUI.TunHealthCheckTitle}");
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine();

        var scores = new List<int>();
        foreach (var r in report.Results)
        {
            var (icon, statusText) = r.Status switch
            {
                HealthCheckStatus.Pass => ("  ✓  ", ResUI.TunHealthCheckPass),
                HealthCheckStatus.Warning => ("  ⚠  ", ResUI.TunHealthCheckWarning),
                HealthCheckStatus.Fail => ("  ✗  ", ResUI.TunHealthCheckFail),
                HealthCheckStatus.Skipped => ("  ○  ", "已跳过"),
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
        }

        sb.AppendLine();
        sb.AppendLine("───────────────────────────────────────");
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

        sb.AppendLine("═══════════════════════════════════════");
        return sb.ToString();
    }

    private static void AppendDiagnosis(StringBuilder sb, List<string>? diagnosis, bool isChinese = false)
    {
        if (diagnosis == null || diagnosis.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"  {(isChinese ? ResUI.TunHealthCheckDiagnosis : "Auto Diagnosis:")}");
        foreach (var line in diagnosis)
        {
            sb.AppendLine($"  {line}");
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
        var sensitiveKeys = new[] { "ipv4", "exit_ip", "test_source_ip", "adapter" };

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
