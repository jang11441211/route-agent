using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using RouteAgent.Models;

namespace RouteAgent.Services;

public static class NetworkService
{
    /// <summary>枚举可用于出口的物理/虚拟网卡。</summary>
    /// <param name="tunDevice">本程序 TUN 虚拟网卡名称，需要从出口网卡中排除，否则会形成环路。</param>
    public static List<AdapterInfo> GetAdapters(string? tunDevice = null)
    {
        var result = new List<AdapterInfo>();
        var defaultIp = GetDefaultRouteInterfaceIp();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                if (ni.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsOwnTunAdapter(ni, tunDevice))
                    continue;

                try
                {
                    var props = ni.GetIPProperties();
                    var unicast = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString())
                        .FirstOrDefault(ip => !ip.StartsWith("169.254.")) ?? "";

                    var gateway = props.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(g => g.Address.ToString())
                        .FirstOrDefault() ?? "";

                    var dns = string.Join(", ", props.DnsAddresses
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.ToString()));

                    int index = 0;
                    try { index = props.GetIPv4Properties()?.Index ?? 0; } catch { }

                    var isUp = ni.OperationalStatus == OperationalStatus.Up;
                    result.Add(new AdapterInfo
                    {
                        Name = ni.Name,
                        Description = ni.Description,
                        Index = index,
                        IPv4 = unicast,
                        Gateway = gateway,
                        Dns = dns,
                        IsUp = isUp,
                        StatusText = isUp ? "已连接" : "未连接",
                        IsDefault = !string.IsNullOrEmpty(defaultIp) && unicast == defaultIp,
                        TypeText = MapType(ni.NetworkInterfaceType),
                        SpeedText = FormatSpeed(ni.Speed),
                    });
                }
                catch
                {
                    // 个别虚拟网卡读取属性会抛异常，忽略即可
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Write("枚举网卡失败：" + ex.Message);
        }

        return result
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.IsUp)
            .ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>是否为 RouteAgent 自己的 TUN 虚拟网卡（选它作为出口会让流量在 TUN 里打转）。</summary>
    private static bool IsOwnTunAdapter(NetworkInterface ni, string? tunDevice)
    {
        if (!string.IsNullOrWhiteSpace(tunDevice) &&
            ni.Name.Equals(tunDevice.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return ni.Description.Contains("Meta Tunnel", StringComparison.OrdinalIgnoreCase)
               || ni.Description.Contains("mihomo", StringComparison.OrdinalIgnoreCase)
               || ni.Description.Contains("wintun", StringComparison.OrdinalIgnoreCase);
    }

    private static string MapType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet => "以太网",
        NetworkInterfaceType.GigabitEthernet => "以太网",
        NetworkInterfaceType.FastEthernetT => "以太网",
        NetworkInterfaceType.Wireless80211 => "无线",
        NetworkInterfaceType.Ppp => "拨号 / USB 共享",
        NetworkInterfaceType.Wwanpp => "移动网络",
        NetworkInterfaceType.Wwanpp2 => "移动网络",
        _ => "其它"
    };

    private static string FormatSpeed(long bps)
    {
        if (bps <= 0) return "-";
        var mbps = bps / 1_000_000d;
        return mbps >= 1000 ? $"{mbps / 1000:0.#} Gbps" : $"{mbps:0} Mbps";
    }

    /// <summary>读取 IPv4 默认路由对应的本机地址（忽略 TUN 虚拟网卡）。</summary>
    private static string? GetDefaultRouteInterfaceIp()
    {
        try
        {
            var psi = new ProcessStartInfo("route", "print -4")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2500);

            string? bestIp = null;
            var bestMetric = int.MaxValue;
            foreach (Match match in Regex.Matches(text,
                         @"^\s*0\.0\.0\.0\s+0\.0\.0\.0\s+\S+\s+(\d{1,3}(?:\.\d{1,3}){3})\s+(\d+)\s*$",
                         RegexOptions.Multiline))
            {
                var metric = int.Parse(match.Groups[2].Value);
                if (metric < bestMetric)
                {
                    bestMetric = metric;
                    bestIp = match.Groups[1].Value;
                }
            }
            return bestIp;
        }
        catch
        {
            return null;
        }
    }
}
