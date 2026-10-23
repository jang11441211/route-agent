using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RouteAgent.Models;
using Icon = System.Drawing.Icon;

namespace RouteAgent.Services;

public static class ProcessService
{
    /// <summary>枚举当前正在运行、可访问到路径的进程（按进程名去重）。</summary>
    public static List<ProcessItem> GetRunningProcesses()
    {
        var map = new Dictionary<string, ProcessItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var fileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(fileName) ||
                    !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (map.ContainsKey(fileName)) continue;

                string description = "";
                try { description = process.MainModule?.FileVersionInfo?.FileDescription ?? ""; } catch { }

                map[fileName] = new ProcessItem
                {
                    ProcessName = fileName,
                    DisplayName = string.IsNullOrWhiteSpace(description)
                        ? Path.GetFileNameWithoutExtension(fileName)
                        : description,
                    ExecutablePath = path,
                    Icon = ExtractIcon(path),
                };
            }
            catch
            {
                // 受保护进程 / 权限不足，跳过
            }
            finally
            {
                process.Dispose();
            }
        }

        return map.Values
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    public static ImageSource? ExtractIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    public static string? GetProcessNameFromPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
