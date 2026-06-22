namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    private void GenInbounds()
    {
        try
        {
            var listen = "0.0.0.0";
            var listenPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            _coreConfig.inbounds = [];
            var inbound = BuildInbound(_config.Inbound.First(), EInboundProtocol.socks, true);
            var isUsingLocalMixedPort = _node.Address == Global.Loopback && _node.Port == listenPort;

            // When NetBridge is active, ensure mixed inbound accepts UDP if protocol mode requires it
            if (_config.TunModeItem.EnableLegacyProtect)
            {
                var nbProtocolMode = _config.NetBridgeItem?.ProtocolMode ?? "TCP";
                if (nbProtocolMode is "UDP" or "BOTH" or "TCP+UDP")
                {
                    inbound.settings.udp = true;
                }
            }

            // v2.1.0: CoreDirect mode adds netbridge inbound for direct ProxyBridgeCore connection
            var forwardMode = _config.NetBridgeItem?.ForwardMode ?? "Bridge";
            if (forwardMode == "CoreDirect" && _config.TunModeItem.EnableLegacyProtect)
            {
                var nbTcpPort = _config.NetBridgeItem?.CoreDirectTcpPort ?? 35000;
                var nbInbound = new Inbounds4Ray
                {
                    tag = "netbridge",
                    protocol = "netbridge",
                    listen = Global.Loopback,
                    port = nbTcpPort,
                    settings = new()
                    {
                        listenPort = (uint)nbTcpPort
                    }
                };
                _coreConfig.inbounds.Add(nbInbound);
            }

            if (!context.IsTunEnabled || !isUsingLocalMixedPort)
            {
                _coreConfig.inbounds.Add(inbound);

                if (_config.Inbound.First().SecondLocalPortEnabled)
                {
                    var inbound2 = BuildInbound(_config.Inbound.First(), EInboundProtocol.socks2, true);
                    _coreConfig.inbounds.Add(inbound2);
                }

                if (_config.Inbound.First().AllowLANConn)
                {
                    if (_config.Inbound.First().NewPort4LAN)
                    {
                        var inbound3 = BuildInbound(_config.Inbound.First(), EInboundProtocol.socks3, true);
                        inbound3.listen = listen;
                        _coreConfig.inbounds.Add(inbound3);

                        // auth
                        if (_config.Inbound.First().User.IsNotEmpty() && _config.Inbound.First().Pass.IsNotEmpty())
                        {
                            inbound3.settings.auth = "password";
                            inbound3.settings.accounts =
                            [
                                new()
                                {
                                    user = _config.Inbound.First().User,
                                    pass = _config.Inbound.First().Pass,
                                },

                            ];
                        }
                    }
                    else
                    {
                        inbound.listen = listen;
                    }
                }
            }

            if (context.IsTunEnabled)
            {
                var tunMtu = _config.TunModeItem.Mtu > 0 ? _config.TunModeItem.Mtu : Global.TunMtus.First();
                var tunInbound =
                    JsonUtils.Deserialize<Inbounds4Ray>(EmbedUtils.GetEmbedText(Global.V2raySampleTunInbound)) ??
                    new Inbounds4Ray();
                tunInbound.settings.name = context.IsMacOS ? $"utun{Random.Shared.Next(0, 999)}" : Global.V2rayTunName;
                tunInbound.settings.MTU = tunMtu;
                if (!_config.TunModeItem.EnableIPv6Address)
                {
                    tunInbound.settings.gateway = ["172.18.0.1/30"];
                    tunInbound.settings.autoSystemRoutingTable = ["0.0.0.0/0"];
                }
                var bindInterface = _config.CoreBasicItem.BindInterface?.TrimEx();
                if (!bindInterface.IsNullOrEmpty())
                {
                    tunInbound.settings.autoOutboundsInterface = bindInterface;
                }
                tunInbound.sniffing = inbound.sniffing;

                if (_config.TunModeItem.RouteExcludeAddress is { Count: > 0 })
                {
                    var wholeInternet = IPNetwork2.Parse("0.0.0.0/0");
                    var wholeInternetV6 = IPNetwork2.Parse("::/0");

                    var excludeList = _config.TunModeItem.RouteExcludeAddress.Select(IPNetwork2.Parse)
                        .Where(x => x != null).ToList();

                    var includeList = new List<IPNetwork2> { wholeInternet };
                    var includeListV6 = new List<IPNetwork2> { wholeInternetV6 };

                    const int maxSubnets = 256;
                    foreach (var exclude in excludeList)
                    {
                        var temp = new List<IPNetwork2>();
                        if (exclude.AddressFamily == AddressFamily.InterNetwork)
                        {
                            foreach (var net in includeList)
                            {
                                temp.AddRange(net.Subtract(exclude));
                            }
                            includeList = temp;
                        }
                        else if (exclude.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            foreach (var net in includeListV6)
                            {
                                temp.AddRange(net.Subtract(exclude));
                            }
                            includeListV6 = temp;
                        }

                        if (includeList.Count + includeListV6.Count > maxSubnets)
                        {
                            Logging.SaveLog($"RouteExcludeAddress produced {includeList.Count + includeListV6.Count} subnets, exceeding limit {maxSubnets}. Falling back to exclude list only.");
                            includeList = [wholeInternet];
                            includeListV6 = [wholeInternetV6];
                            break;
                        }
                    }

                    if (includeList.Count + includeListV6.Count <= maxSubnets)
                    {
                        includeList = IPNetwork2.Supernet(includeList.ToArray()).ToList();
                        includeListV6 = IPNetwork2.Supernet(includeListV6.ToArray()).ToList();
                    }

                    if (_config.TunModeItem.EnableIPv6Address)
                    {
                        tunInbound.settings.autoSystemRoutingTable = includeList.Select(x => x.ToString())
                            .Concat(includeListV6.Select(x => x.ToString())).ToList();
                    }
                    else
                    {
                        tunInbound.settings.autoSystemRoutingTable = includeList.Select(x => x.ToString()).ToList();
                    }
                }

                _coreConfig.inbounds.Add(tunInbound);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            throw;
        }
    }

    private Inbounds4Ray BuildInbound(InItem inItem, EInboundProtocol protocol, bool bSocks)
    {
        var result = EmbedUtils.GetEmbedText(Global.V2raySampleInbound);
        if (result.IsNullOrEmpty())
        {
            return new();
        }

        var inbound = JsonUtils.Deserialize<Inbounds4Ray>(result);
        if (inbound == null)
        {
            return new();
        }
        inbound.tag = protocol.ToString();
        inbound.port = inItem.LocalPort + (int)protocol;
        inbound.protocol = nameof(EInboundProtocol.mixed);
        inbound.settings.udp = inItem.UdpEnabled;
        inbound.sniffing.enabled = inItem.SniffingEnabled;
        inbound.sniffing.destOverride = inItem.DestOverride;
        inbound.sniffing.routeOnly = inItem.RouteOnly;

        return inbound;
    }
}
