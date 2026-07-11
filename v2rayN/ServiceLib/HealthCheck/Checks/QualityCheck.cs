using System.Diagnostics;
using System.Net;
using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck.Checks;

public class QualityCheck
{
    private const int TestRounds = 3;

    public async Task<HealthCheckResult> CheckAsync(int? proxyPort = null)
    {
        var sw = Stopwatch.StartNew();
        var details = new Dictionary<string, object>();

        try
        {
            var port = proxyPort ?? AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            details["proxy_port"] = port;
            details["probe_mode"] = "socks5";

            var rtts = new List<int>();
            var ttfbs = new List<int>();
            var failures = 0;

            var urls = new[]
            {
                "https://www.google.com/generate_204",
                "https://www.gstatic.com/generate_204"
            };

            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AllowAutoRedirect = false,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            for (var i = 0; i < TestRounds; i++)
            {
                foreach (var url in urls)
                {
                    try
                    {
                        var (ttfb, totalTime) = await MeasureHttpAsync(http, url);
                        rtts.Add(totalTime);
                        ttfbs.Add(ttfb);
                    }
                    catch
                    {
                        failures++;
                    }
                }
            }

            details["test_rounds"] = TestRounds;
            details["total_requests"] = TestRounds * urls.Length;
            details["failures"] = failures;

            if (rtts.Count == 0)
            {
                sw.Stop();
                return new HealthCheckResult("Quality", HealthCheckStatus.Fail,
                    "All quality test requests via SOCKS failed", sw.Elapsed, details);
            }

            var avgRtt = (int)rtts.Average();
            var maxRtt = rtts.Max();
            var minRtt = rtts.Min();
            var avgTtfb = (int)ttfbs.Average();
            var lossRate = (double)failures / (TestRounds * urls.Length);

            details["avg_rtt_ms"] = avgRtt;
            details["min_rtt_ms"] = minRtt;
            details["max_rtt_ms"] = maxRtt;
            details["avg_ttfb_ms"] = avgTtfb;
            details["loss_rate"] = lossRate;

            var rttScore = ScoreRtt(avgRtt);
            var ttfbScore = ScoreTtfb(avgTtfb);
            var lossScore = ScoreLoss(lossRate);
            var totalScore = (rttScore + ttfbScore + lossScore) / 3;

            details["rtt_grade"] = GradeFromScore(rttScore);
            details["ttfb_grade"] = GradeFromScore(ttfbScore);
            details["loss_grade"] = GradeFromScore(lossScore);
            details["health_score"] = totalScore;

            var status = totalScore >= 80 ? HealthCheckStatus.Pass
                : totalScore >= 50 ? HealthCheckStatus.Warning
                : HealthCheckStatus.Fail;

            var grade = GradeFromScore(totalScore);
            sw.Stop();
            return new HealthCheckResult("Quality", status,
                $"Health Score: {totalScore}/100 ({grade}) via SOCKS", sw.Elapsed, details);
        }
        catch (Exception ex)
        {
            details["error"] = ex.Message;
            sw.Stop();
            return new HealthCheckResult("Quality", HealthCheckStatus.Error,
                $"Quality check failed: {ex.Message}", sw.Elapsed, details);
        }
    }

    private static int ScoreRtt(int avgMs) => avgMs switch
    {
        < 100 => 100,
        < 200 => 85,
        < 350 => 70,
        < 500 => 50,
        < 800 => 30,
        _ => 10
    };

    private static int ScoreTtfb(int avgMs) => avgMs switch
    {
        < 200 => 100,
        < 400 => 85,
        < 800 => 70,
        < 1500 => 50,
        < 3000 => 30,
        _ => 10
    };

    private static int ScoreLoss(double rate) => rate switch
    {
        < 0.01 => 100,
        < 0.05 => 85,
        < 0.10 => 70,
        < 0.20 => 50,
        < 0.35 => 30,
        _ => 10
    };

    private static string GradeFromScore(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "F"
    };

    private static async Task<(int TtfbMs, int TotalMs)> MeasureHttpAsync(HttpClient http, string url)
    {
        var measureSw = Stopwatch.StartNew();
        var response = await http.GetAsync(url);
        var ttfb = (int)measureSw.ElapsedMilliseconds;

        await response.Content.ReadAsStringAsync();
        measureSw.Stop();
        var total = (int)measureSw.ElapsedMilliseconds;

        return (ttfb, total);
    }
}
