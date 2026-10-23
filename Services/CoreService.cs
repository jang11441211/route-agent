using System.Diagnostics;
using System.IO;
using System.Text;
using RouteAgent.Models;

namespace RouteAgent.Services;

public sealed class CoreService
{
    private Process? _process;
    private readonly List<string> _recentLines = new();
    private readonly object _sync = new();

    public event Action<string>? LogReceived;
    public event Action? Exited;

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    public int? ProcessId => IsRunning ? _process?.Id : null;

    /// <summary>把随程序附带的 mihomo.exe / wintun.dll 安装到用户目录（首次运行）。</summary>
    public static void EnsureBundledInstalled()
    {
        try
        {
            if (!Directory.Exists(AppPaths.BundledCoreDir)) return;
            Directory.CreateDirectory(AppPaths.CoreDir);

            foreach (var source in Directory.GetFiles(AppPaths.BundledCoreDir))
            {
                var fileName = Path.GetFileName(source);

                // 官方包里的可执行文件叫 mihomo-windows-amd64-compatible.exe，统一落成 mihomo.exe
                var targetName = fileName.StartsWith("mihomo", StringComparison.OrdinalIgnoreCase) &&
                                 fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? "mihomo.exe"
                    : fileName;

                var target = Path.Combine(AppPaths.CoreDir, targetName);
                if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length)
                    continue;
                File.Copy(source, target, true);
                Logger.Write($"已安装内核文件：{targetName}");
            }
        }
        catch (Exception ex)
        {
            Logger.Write("安装内置内核失败：" + ex.Message);
        }
    }

    public static string? ResolveCorePath(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CorePath) && File.Exists(settings.CorePath))
            return settings.CorePath;
        if (File.Exists(AppPaths.DefaultCorePath))
            return AppPaths.DefaultCorePath;

        var bundled = Path.Combine(AppPaths.BundledCoreDir, "mihomo.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    public static async Task<string?> GetCoreVersionAsync(string corePath)
    {
        try
        {
            var psi = new ProcessStartInfo(corePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(corePath) ?? AppPaths.CoreDir,
            };
            psi.ArgumentList.Add("-v");

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var text = (output + "\n" + error).Trim();
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task StartAsync(string configPath, AppSettings settings)
    {
        var corePath = ResolveCorePath(settings)
                       ?? throw new InvalidOperationException("未找到 mihomo 内核，请先在「设置」里下载内核。");

        var workingDirectory = Path.GetDirectoryName(corePath) ?? AppPaths.CoreDir;

        var psi = new ProcessStartInfo(corePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(AppPaths.Root);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(configPath);

        lock (_sync) _recentLines.Clear();

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) OnLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) OnLog(e.Data); };
        process.Exited += (_, _) =>
        {
            OnLog("内核进程已退出");
            Exited?.Invoke();
        };

        if (!process.Start())
            throw new InvalidOperationException("无法启动 mihomo 内核进程。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        await Task.Delay(1600);

        if (process.HasExited)
        {
            var detail = string.Join(" | ", RecentLines().TakeLast(4));
            throw new InvalidOperationException(
                $"内核启动失败（退出码 {process.ExitCode}）。{detail}");
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process == null) return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(5000);
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            Logger.Write("停止内核时出现问题：" + ex.Message);
        }
        finally
        {
            process.Dispose();
        }
    }

    private void OnLog(string line)
    {
        lock (_sync)
        {
            _recentLines.Add(line);
            if (_recentLines.Count > 200) _recentLines.RemoveRange(0, _recentLines.Count - 200);
        }

        try
        {
            File.AppendAllText(AppPaths.CoreLogPath,
                $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
            // 忽略日志写入异常
        }

        LogReceived?.Invoke(line);
    }

    private List<string> RecentLines()
    {
        lock (_sync) return _recentLines.ToList();
    }
}
