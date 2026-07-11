using ServiceLib.HealthCheck.Models;

namespace ServiceLib.HealthCheck;

/// <summary>
/// Applies safe, reversible config-level fixes suggested by TUN health diagnosis.
/// Destructive / ambiguous fixes are not auto-applied.
/// </summary>
public sealed class HealthCheckAutoFixService
{
    private readonly Config _config;

    public HealthCheckAutoFixService(Config config)
    {
        _config = config;
    }

    public static HealthCheckFixAction? Describe(HealthCheckFixId id) => id switch
    {
        HealthCheckFixId.EnableAutoRouteStrictRoute => new(
            id,
            "Enable AutoRoute + StrictRoute",
            "开启 AutoRoute + StrictRoute",
            "Turn on TUN auto_route and strict_route, then reload core.",
            "开启 TUN 自动路由与严格路由，然后重载核心。",
            RequiresAdmin: false,
            RequiresReload: true),

        HealthCheckFixId.SetMtu1280 => new(
            id,
            "Set TUN MTU to 1280",
            "将 TUN MTU 设为 1280",
            "Reduce TUN MTU to 1280 to mitigate fragmentation issues.",
            "将 TUN MTU 降至 1280，缓解分片/MTU 问题。",
            RequiresAdmin: false,
            RequiresReload: true),

        HealthCheckFixId.ExcludeServerIpFromTun => new(
            id,
            "Exclude server IP from TUN",
            "将节点 IP 加入 TUN 路由排除",
            "Add current node IP to RouteExcludeAddress to avoid routing loops.",
            "把当前节点 IP 加入 RouteExcludeAddress，避免路由环路。",
            RequiresAdmin: false,
            RequiresReload: true),

        HealthCheckFixId.RebootAsAdmin => new(
            id,
            "Restart as administrator",
            "以管理员身份重启",
            "Relaunch v2rayN elevated so Wintun/TUN can start.",
            "以管理员权限重启 v2rayN，以便创建 Wintun/TUN。",
            RequiresAdmin: true,
            RequiresReload: false,
            IsSafeAuto: true),

        HealthCheckFixId.EnableTun => new(
            id,
            "Enable TUN mode",
            "开启 TUN 模式",
            "Enable TUN (requires admin). Legacy process protect will be turned off.",
            "开启 TUN（需要管理员）。将关闭进程劫持模式。",
            RequiresAdmin: true,
            RequiresReload: true),

        HealthCheckFixId.ReloadCore => new(
            id,
            "Reload core",
            "重载核心",
            "Save config (if needed) and reload Xray/sing-box.",
            "保存配置（如需要）并重载 Xray/sing-box。",
            RequiresAdmin: false,
            RequiresReload: true),

        _ => null
    };

    public async Task<List<HealthCheckFixResult>> ApplyAsync(
        IEnumerable<HealthCheckFixId> fixIds,
        CancellationToken ct = default)
    {
        var results = new List<HealthCheckFixResult>();
        var ordered = fixIds
            .Distinct()
            .OrderBy(id => id switch
            {
                HealthCheckFixId.EnableAutoRouteStrictRoute => 10,
                HealthCheckFixId.SetMtu1280 => 20,
                HealthCheckFixId.ExcludeServerIpFromTun => 30,
                HealthCheckFixId.EnableTun => 40,
                HealthCheckFixId.ReloadCore => 80,
                HealthCheckFixId.RebootAsAdmin => 90,
                _ => 50
            })
            .ToList();

        var needSave = false;
        var needReload = false;
        var needRebootAdmin = false;
        var wantEnableTun = false;

        foreach (var id in ordered)
        {
            ct.ThrowIfCancellationRequested();
            switch (id)
            {
                case HealthCheckFixId.EnableAutoRouteStrictRoute:
                    results.Add(ApplyAutoRoute());
                    needSave = true;
                    needReload = true;
                    break;

                case HealthCheckFixId.SetMtu1280:
                    results.Add(ApplyMtu1280());
                    needSave = true;
                    needReload = true;
                    break;

                case HealthCheckFixId.ExcludeServerIpFromTun:
                    results.Add(await ApplyExcludeServerIpAsync());
                    needSave = true;
                    needReload = true;
                    break;

                case HealthCheckFixId.EnableTun:
                    wantEnableTun = true;
                    break;

                case HealthCheckFixId.ReloadCore:
                    needReload = true;
                    results.Add(new HealthCheckFixResult(
                        id, true, false,
                        "Core reload scheduled.",
                        "已安排重载核心。"));
                    break;

                case HealthCheckFixId.RebootAsAdmin:
                    needRebootAdmin = true;
                    results.Add(new HealthCheckFixResult(
                        id, true, false,
                        "Admin restart scheduled.",
                        "已安排以管理员身份重启。"));
                    break;
            }
        }

        if (wantEnableTun)
        {
            var tunResult = await ApplyEnableTunAsync();
            results.Add(tunResult);
            if (tunResult.Success && !tunResult.Skipped)
            {
                needSave = true;
                // RebootAsAdmin path already exits; otherwise reload after save.
                if (!needRebootAdmin)
                {
                    needReload = true;
                }
            }
        }

        if (needSave)
        {
            await ConfigHandler.SaveConfig(_config);
        }

        // Reboot-as-admin exits process; do it last and skip reload.
        if (needRebootAdmin)
        {
            await AppManager.Instance.RebootAsAdmin();
            return results;
        }

        if (needReload)
        {
            AppEvents.ReloadRequested.Publish();
        }

        return results;
    }

    private HealthCheckFixResult ApplyAutoRoute()
    {
        _config.TunModeItem ??= new TunModeItem();
        var changed = false;
        if (!_config.TunModeItem.AutoRoute)
        {
            _config.TunModeItem.AutoRoute = true;
            changed = true;
        }
        if (!_config.TunModeItem.StrictRoute)
        {
            _config.TunModeItem.StrictRoute = true;
            changed = true;
        }

        if (!changed)
        {
            return new HealthCheckFixResult(
                HealthCheckFixId.EnableAutoRouteStrictRoute, true, true,
                "AutoRoute/StrictRoute already enabled.",
                "AutoRoute/StrictRoute 已开启，无需修改。");
        }

        return new HealthCheckFixResult(
            HealthCheckFixId.EnableAutoRouteStrictRoute, true, false,
            "Enabled AutoRoute and StrictRoute.",
            "已开启 AutoRoute 与 StrictRoute。");
    }

    private HealthCheckFixResult ApplyMtu1280()
    {
        _config.TunModeItem ??= new TunModeItem();
        if (_config.TunModeItem.Mtu == 1280)
        {
            return new HealthCheckFixResult(
                HealthCheckFixId.SetMtu1280, true, true,
                "MTU already 1280.",
                "MTU 已是 1280，无需修改。");
        }

        var old = _config.TunModeItem.Mtu;
        _config.TunModeItem.Mtu = 1280;
        return new HealthCheckFixResult(
            HealthCheckFixId.SetMtu1280, true, false,
            $"MTU changed from {(old <= 0 ? "default" : old.ToString())} to 1280.",
            $"MTU 已从 {(old <= 0 ? "默认" : old.ToString())} 调整为 1280。");
    }

    private async Task<HealthCheckFixResult> ApplyExcludeServerIpAsync()
    {
        _config.TunModeItem ??= new TunModeItem();
        _config.TunModeItem.RouteExcludeAddress ??= new List<string>();

        var server = await ConfigHandler.GetDefaultServer(_config);
        if (server == null || server.Address.IsNullOrEmpty())
        {
            return new HealthCheckFixResult(
                HealthCheckFixId.ExcludeServerIpFromTun, false, false,
                "No default server address to exclude.",
                "没有可排除的当前节点地址。");
        }

        var addr = server.Address.Trim();
        // Domain names are not useful as route excludes; only add IP-looking values.
        if (!IPAddress.TryParse(addr, out _))
        {
            return new HealthCheckFixResult(
                HealthCheckFixId.ExcludeServerIpFromTun, false, false,
                $"Server address is a domain ({addr}), not an IP. Resolve/exclude manually.",
                $"节点地址是域名（{addr}）而非 IP，请手动解析后排除。");
        }

        if (_config.TunModeItem.RouteExcludeAddress.Any(x =>
                string.Equals(x?.Trim(), addr, StringComparison.OrdinalIgnoreCase)))
        {
            return new HealthCheckFixResult(
                HealthCheckFixId.ExcludeServerIpFromTun, true, true,
                $"Server IP {addr} already in route exclude list.",
                $"节点 IP {addr} 已在路由排除列表中。");
        }

        _config.TunModeItem.RouteExcludeAddress.Add(addr);
        return new HealthCheckFixResult(
            HealthCheckFixId.ExcludeServerIpFromTun, true, false,
            $"Added {addr} to RouteExcludeAddress.",
            $"已将 {addr} 加入 RouteExcludeAddress。");
    }

    private async Task<HealthCheckFixResult> ApplyEnableTunAsync()
    {
        _config.TunModeItem ??= new TunModeItem();

        if (_config.TunModeItem.EnableTun)
        {
            return new HealthCheckFixResult(
                HealthCheckFixId.EnableTun, true, true,
                "TUN already enabled.",
                "TUN 已开启，无需修改。");
        }

        if (Utils.IsWindows() && !Utils.IsAdministrator())
        {
            // Caller will still attempt reboot if RebootAsAdmin was selected;
            // here we reboot immediately because EnableTun requires elevation.
            await AppManager.Instance.RebootAsAdmin();
            return new HealthCheckFixResult(
                HealthCheckFixId.EnableTun, true, false,
                "Not admin - restarting elevated to enable TUN.",
                "当前非管理员，正在以管理员重启以开启 TUN。");
        }

        _config.TunModeItem.EnableTun = true;
        _config.TunModeItem.EnableLegacyProtect = false;
        if (!_config.TunModeItem.AutoRoute)
        {
            _config.TunModeItem.AutoRoute = true;
        }
        if (!_config.TunModeItem.StrictRoute)
        {
            _config.TunModeItem.StrictRoute = true;
        }

        return new HealthCheckFixResult(
            HealthCheckFixId.EnableTun, true, false,
            "TUN enabled (legacy protect off).",
            "已开启 TUN（已关闭进程劫持）。");
    }
}
