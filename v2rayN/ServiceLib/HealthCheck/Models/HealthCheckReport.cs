namespace ServiceLib.HealthCheck.Models;

public enum HealthCheckOverallStatus
{
    AllPass,
    HasWarning,
    HasFailure
}

public record HealthCheckReport(
    HealthCheckOverallStatus OverallStatus,
    List<HealthCheckResult> Results,
    TimeSpan TotalDuration,
    List<string>? Diagnosis = null)
{
    public string Summary => OverallStatus switch
    {
        HealthCheckOverallStatus.AllPass => "All checks passed",
        HealthCheckOverallStatus.HasWarning => $"Passed with warnings ({Results.Count(r => r.Status == HealthCheckStatus.Warning)} warnings)",
        HealthCheckOverallStatus.HasFailure => $"Failures detected ({Results.Count(r => r.Status is HealthCheckStatus.Fail or HealthCheckStatus.Error)} failures)",
        _ => "Unknown"
    };

    public HealthCheckResult? GetResult(string layer) =>
        Results.FirstOrDefault(r => r.Layer == layer);

    public bool IsLayerCriticalFail(string layer)
    {
        var r = GetResult(layer);
        return r is { Status: HealthCheckStatus.Fail or HealthCheckStatus.Error };
    }
}
