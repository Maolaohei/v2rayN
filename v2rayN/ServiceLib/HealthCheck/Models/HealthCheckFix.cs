namespace ServiceLib.HealthCheck.Models;

public enum HealthCheckFixId
{
    EnableAutoRouteStrictRoute,
    SetMtu1280,
    ExcludeServerIpFromTun,
    RebootAsAdmin,
    EnableTun,
    ReloadCore,
}

public record HealthCheckFixAction(
    HealthCheckFixId Id,
    string TitleEn,
    string TitleZh,
    string DescriptionEn,
    string DescriptionZh,
    bool RequiresAdmin = false,
    bool RequiresReload = true,
    bool IsSafeAuto = true)
{
    public string Title(bool zh) => zh ? TitleZh : TitleEn;
    public string Description(bool zh) => zh ? DescriptionZh : DescriptionEn;
}

public record HealthCheckFixResult(
    HealthCheckFixId Id,
    bool Success,
    bool Skipped,
    string MessageEn,
    string MessageZh)
{
    public string Message(bool zh) => zh ? MessageZh : MessageEn;
}
