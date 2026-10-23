using System.IO;

namespace RouteAgent.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RouteAgent");

    public static string CoreDir => Path.Combine(Root, "core");
    public static string DefaultCorePath => Path.Combine(CoreDir, "mihomo.exe");
    public static string ConfigPath => Path.Combine(Root, "config.yaml");
    public static string LogPath => Path.Combine(Root, "routeagent.log");
    public static string CoreLogPath => Path.Combine(Root, "core.log");
    public static string SettingsPath => Path.Combine(Root, "settings.json");

    public static string BundledCoreDir => Path.Combine(AppContext.BaseDirectory, "Assets", "core");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CoreDir);
    }
}

public static class Logger
{
    private static readonly object Sync = new();
    private static readonly List<string> Recent = new();

    public static event Action<string>? LineWritten;

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (Sync)
        {
            Recent.Add(line);
            if (Recent.Count > 2000) Recent.RemoveRange(0, Recent.Count - 2000);
            try
            {
                File.AppendAllText(AppPaths.LogPath, line + Environment.NewLine);
            }
            catch
            {
                // 日志写入失败不影响主流程
            }
        }

        // 日志订阅者（界面刷新）出错不能反过来把进程带崩
        try { LineWritten?.Invoke(line); } catch { }
    }

    public static IReadOnlyList<string> RecentLines
    {
        get { lock (Sync) return Recent.ToArray(); }
    }

    public static void Clear()
    {
        lock (Sync) Recent.Clear();
    }
}
