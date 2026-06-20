namespace ServiceLib.HealthCheck.Models;

public enum HealthCheckStatus
{
    Pass,
    Warning,
    Fail,
    Skipped,
    Error
}

public record HealthCheckResult(
    string Layer,
    HealthCheckStatus Status,
    string Summary,
    TimeSpan Duration,
    IReadOnlyDictionary<string, object>? Details = null)
{
    public bool IsOk => Status == HealthCheckStatus.Pass;
    public bool IsSkipped => Status == HealthCheckStatus.Skipped;
}
