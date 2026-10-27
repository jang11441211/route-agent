using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RouteAgent.Models;

namespace RouteAgent.Services;

/// <summary>把界面上的规则翻译成 mihomo 配置（TUN + 进程规则 + 网卡绑定）。</summary>
public static class ConfigBuilder
{
    public static string Build(AppSettings settings, IReadOnlyList<AdapterInfo> adapters)
    {
        var rules = settings.Rules
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.ProcessName))
            .ToList();

        // 为每条规则用到的网卡生成一个 direct 出站，并绑定该网卡的名称
        var adapterOrder = new List<string>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.AdapterName)) continue;
            if (!adapterOrder.Contains(rule.AdapterName, StringComparer.OrdinalIgnoreCase))
                adapterOrder.Add(rule.AdapterName);
        }

        var tagOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < adapterOrder.Count; i++)
            tagOf[adapterOrder[i]] = $"nic-{i + 1}";

        var sb = new StringBuilder();
        sb.AppendLine("# ================================================================");
        sb.AppendLine("# 由 RouteAgent 自动生成，每次启动分流时都会重新写入。");
        sb.AppendLine($"# 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# ================================================================");
        sb.AppendLine("mixed-port: 0");
        sb.AppendLine("allow-lan: false");
        sb.AppendLine("mode: rule");
        sb.AppendLine($"log-level: {Quote(settings.LogLevel)}");
        sb.AppendLine("ipv6: false");
        sb.AppendLine("find-process-mode: always");
        sb.AppendLine("unified-delay: true");
        sb.AppendLine("tcp-concurrent: true");
        sb.AppendLine();

        if (settings.EnableDnsHijack)
        {
            var servers = settings.Nameservers
                .Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            if (servers.Count == 0) servers.AddRange(new[] { "223.5.5.5", "119.29.29.29" });

            sb.AppendLine("dns:");
            sb.AppendLine("  enable: true");
            sb.AppendLine("  ipv6: false");
            sb.AppendLine("  enhanced-mode: fake-ip");
            sb.AppendLine("  fake-ip-range: 198.18.0.1/16");
            sb.AppendLine("  default-nameserver:");
            sb.AppendLine("    - 223.5.5.5");
            sb.AppendLine("    - 119.29.29.29");
            sb.AppendLine("  nameserver:");
            foreach (var server in servers)
                sb.AppendLine($"    - {Quote(server)}");
            sb.AppendLine();
        }

        sb.AppendLine("tun:");
        sb.AppendLine("  enable: true");
        sb.AppendLine("  stack: gvisor");
        sb.AppendLine($"  device: {Quote(settings.TunDevice)}");
        sb.AppendLine("  mtu: 1500");
        sb.AppendLine("  auto-route: true");
        sb.AppendLine("  auto-detect-interface: true");
        sb.AppendLine("  strict-route: false");
        if (settings.EnableDnsHijack)
        {
            sb.AppendLine("  dns-hijack:");
            sb.AppendLine("    - \"any:53\"");
        }
        sb.AppendLine();

        if (adapterOrder.Count > 0)
        {
            sb.AppendLine("proxies:");
            foreach (var adapterName in adapterOrder)
            {
                sb.AppendLine($"  - name: {Quote(tagOf[adapterName])}");
                sb.AppendLine("    type: direct");
                sb.AppendLine($"    interface-name: {Quote(adapterName)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("rules:");
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.AdapterName)) continue;
            if (!tagOf.TryGetValue(rule.AdapterName, out var tag)) continue;

            var processName = rule.ProcessName.Trim();
            if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName += ".exe";

            // 勾选“含子进程”时，先用安装目录正则覆盖同目录下的其它 exe，
            // 再保留进程名规则兜底（程序被复制到别的目录时仍能命中）。
            if (rule.IncludeChildren)
            {
                var dirPattern = BuildDirectoryPattern(rule.ExecutablePath);
                if (dirPattern.Length > 0)
                    sb.AppendLine($"  - PROCESS-PATH-REGEX,{dirPattern},{tag}");
            }

            sb.AppendLine($"  - PROCESS-NAME,{processName},{tag}");
        }
        sb.AppendLine("  - MATCH,DIRECT");

        return sb.ToString();
    }

    private static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>把程序所在目录转成大小写不敏感的路径正则，匹配该目录下的所有可执行文件。</summary>
    private static string BuildDirectoryPattern(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return "";

        var dir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(dir)) return "";

        return "(?i)^" + Regex.Escape(dir) + "\\\\.*";
    }
}
