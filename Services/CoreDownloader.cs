using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace RouteAgent.Services;

/// <summary>下载或导入 mihomo 内核与 wintun 驱动。</summary>
public static class CoreDownloader
{
    public const string Version = "v1.19.2";
    public const string WintunVersion = "0.14.1";
    public const string WintunUrl = "https://www.wintun.net/builds/wintun-" + WintunVersion + ".zip";

    /// <summary>镜像前缀，空串表示直连 GitHub。</summary>
    private static readonly string[] Mirrors =
    {
        "https://ghfast.top/",
        "https://gh.llkk.cc/",
        "https://hub.gitmirror.com/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://github.moeyy.xyz/",
        "",
    };

    private static readonly string[] AssetNames =
    {
        $"mihomo-windows-amd64-compatible-{Version}.zip",
        $"mihomo-windows-amd64-{Version}.zip",
    };

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        // 超时由每次请求自己控制：等响应头 20 秒，传输阶段单独放宽
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RouteAgent/1.0");
        return client;
    }

    public static bool IsReady => File.Exists(AppPaths.DefaultCorePath);

    public static bool IsDriverReady => File.Exists(Path.Combine(AppPaths.CoreDir, "wintun.dll"));

    /// <summary>按内置镜像列表下载内核与驱动（已安装则跳过内核）。</summary>
    public static async Task DownloadAsync(IProgress<string>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(AppPaths.CoreDir);
        var errors = new List<string>();

        if (!IsReady)
        {
            foreach (var mirror in Mirrors)
            {
                foreach (var asset in AssetNames)
                {
                    var url = $"{mirror}https://github.com/MetaCubeX/mihomo/releases/download/{Version}/{asset}";
                    try
                    {
                        progress?.Report($"下载内核：{HostOf(url)}");
                        await InstallFromUrlAsync(url, progress, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{HostOf(url)}：{Short(ex.Message)}");
                        continue;
                    }

                    if (IsReady) break;
                }

                if (IsReady) break;
            }
        }

        if (!IsReady)
        {
            throw new InvalidOperationException(
                "所有下载源都失败了。可以点「本地导入」选择下载好的 zip/exe，或在下方填入可用的下载地址。\n" +
                string.Join("\n", errors.TakeLast(4)));
        }

        await EnsureDriverAsync(progress, token);
        Logger.Write("内核与驱动安装完成：" + AppPaths.CoreDir);
    }

    /// <summary>只从指定地址下载（支持 zip 压缩包或 mihomo.exe 直链）。</summary>
    public static async Task DownloadFromUrlAsync(string url, IProgress<string>? progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("下载地址为空", nameof(url));

        Directory.CreateDirectory(AppPaths.CoreDir);
        progress?.Report("下载内核：自定义地址");
        await InstallFromUrlAsync(url.Trim(), progress, token);

        if (!IsReady) throw new InvalidOperationException("下载完成，但没有找到 mihomo.exe");

        await EnsureDriverAsync(progress, token);
        Logger.Write("内核已从自定义地址安装：" + url);
    }

    /// <summary>导入本地文件：mihomo 的 zip 包、mihomo.exe 或 wintun.dll。</summary>
    public static void InstallFromFile(string filePath, IProgress<string>? progress, CancellationToken token)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("找不到文件", filePath);

        Directory.CreateDirectory(AppPaths.CoreDir);
        progress?.Report("正在导入：" + Path.GetFileName(filePath));

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".zip")
        {
            using var archive = ZipFile.OpenRead(filePath);
            var coreEntry = FindCoreEntry(archive)
                            ?? throw new InvalidOperationException("压缩包里没有 mihomo 可执行文件");
            coreEntry.ExtractToFile(AppPaths.DefaultCorePath, true);
            progress?.Report("已导入 mihomo.exe");

            var driverEntry = FindEntry(archive, "wintun.dll", "amd64");
            if (driverEntry != null)
            {
                driverEntry.ExtractToFile(DriverPath, true);
                progress?.Report("已导入 wintun.dll");
            }

            Logger.Write("已从压缩包导入内核：" + filePath);
        }
        else if (Path.GetFileName(filePath).Equals("wintun.dll", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(filePath, DriverPath, true);
            progress?.Report("已导入 wintun.dll");
            Logger.Write("已导入 TUN 驱动：" + filePath);
        }
        else
        {
            File.Copy(filePath, AppPaths.DefaultCorePath, true);
            progress?.Report("已导入 mihomo.exe");
            Logger.Write("已导入内核：" + filePath);
        }

        if (!IsDriverReady && !Path.GetFileName(filePath).Equals("wintun.dll", StringComparison.OrdinalIgnoreCase))
            progress?.Report("提示：还没有 wintun.dll，TUN 模式无法启动，可继续导入或在线下载。");
    }

    /// <summary>只有驱动缺失时单独补下载 wintun。</summary>
    public static async Task EnsureDriverAsync(IProgress<string>? progress, CancellationToken token)
    {
        if (IsDriverReady) return;

        var zipPath = Path.Combine(Path.GetTempPath(), "routeagent-wintun.zip");
        try
        {
            progress?.Report("下载 TUN 驱动（wintun）…");
            await DownloadFileAsync(WintunUrl, zipPath, progress, token);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = FindEntry(archive, "wintun.dll", "amd64")
                            ?? throw new InvalidOperationException("压缩包中没有 amd64/wintun.dll");
                entry.ExtractToFile(DriverPath, true);
            }

            progress?.Report("wintun 驱动已就绪");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Write("wintun 下载失败：" + ex.Message);
            throw new InvalidOperationException(
                "内核已就绪，但 TUN 驱动 wintun.dll 下载失败：" + Short(ex.Message) +
                "\n可手动下载 " + WintunUrl + " ，导入其中的 bin\\amd64\\wintun.dll。");
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    private static string DriverPath => Path.Combine(AppPaths.CoreDir, "wintun.dll");

    private static async Task InstallFromUrlAsync(string url, IProgress<string>? progress, CancellationToken token)
    {
        var isZip = url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var temp = Path.Combine(Path.GetTempPath(),
            "routeagent-download" + (isZip ? ".zip" : ".exe"));

        try
        {
            await DownloadFileAsync(url, temp, progress, token);

            if (isZip)
            {
                using var archive = ZipFile.OpenRead(temp);
                var entry = FindCoreEntry(archive)
                            ?? throw new InvalidOperationException("压缩包里没有 mihomo 可执行文件");
                entry.ExtractToFile(AppPaths.DefaultCorePath, true);
            }
            else
            {
                File.Copy(temp, AppPaths.DefaultCorePath, true);
            }

            progress?.Report("内核文件已就绪");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>官方 zip 里的可执行文件名为 mihomo-windows-amd64-compatible.exe 等，按前缀匹配。</summary>
    private static ZipArchiveEntry? FindCoreEntry(ZipArchive archive)
    {
        return archive.Entries.FirstOrDefault(e =>
            e.Name.StartsWith("mihomo", StringComparison.OrdinalIgnoreCase) &&
            e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string fileName, string? folderHint = null)
    {
        return archive.Entries.FirstOrDefault(e =>
                   string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase) &&
                   (folderHint == null || e.FullName.Contains(folderHint, StringComparison.OrdinalIgnoreCase)))
               ?? archive.Entries.FirstOrDefault(e =>
                   string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task DownloadFileAsync(string url, string targetPath, IProgress<string>? progress,
        CancellationToken token)
    {
        // 连接 / 响应头阶段：20 秒内没动静就判定该源不可用，快速换下一个
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        headerCts.CancelAfter(TimeSpan.FromSeconds(20));

        HttpResponseMessage response;
        try
        {
            response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("连接超时（20 秒无响应）");
        }

        using var res = response;
        res.EnsureSuccessStatusCode();

        var total = res.Content.Headers.ContentLength ?? 0;
        await using var source = await res.Content.ReadAsStreamAsync(token);
        await using var destination = File.Create(targetPath);

        // 传输阶段单独给 5 分钟，避免慢速镜像被误判为失败
        using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        bodyCts.CancelAfter(TimeSpan.FromMinutes(5));

        var buffer = new byte[81920];
        long read = 0;
        int count;
        var lastReport = 0;
        while ((count = await source.ReadAsync(buffer, bodyCts.Token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), bodyCts.Token);
            read += count;
            if (total > 0 && Environment.TickCount - lastReport > 400)
            {
                lastReport = Environment.TickCount;
                progress?.Report($"下载中 {read / 1024d / 1024d:0.0} / {total / 1024d / 1024d:0.0} MB");
            }
        }

        if (total > 0 && read != total)
            throw new IOException($"下载不完整（{read}/{total} 字节）");
    }

    private static string HostOf(string url)
    {
        try
        {
            var uri = new Uri(url);
            var inner = uri.Query.TrimStart('?');
            return string.IsNullOrEmpty(inner) ? uri.Host : uri.Host + " → " + HostOf(inner);
        }
        catch
        {
            return url;
        }
    }

    private static string Short(string message)
    {
        var line = message.Split('\n')[0].Trim();
        return line.Length > 120 ? line[..120] + "…" : line;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
