using System.Diagnostics;

namespace RouteAgent.Services;

/// <summary>通过计划任务实现「开机静默以管理员权限启动」。</summary>
public static class AutoStartService
{
    private const string TaskName = "RouteAgent_AutoStart";

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);

            using var process = Process.Start(psi);
            if (process == null) return false;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(4000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Enable(out string message)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            message = "无法获取程序路径";
            return false;
        }

        return Run(
            new[]
            {
                "/Create", "/F", "/SC", "ONLOGON", "/RL", "HIGHEST",
                "/TN", TaskName,
                "/TR", $"\"{exe}\" --minimized",
            }, out message);
    }

    public static bool Disable(out string message)
        => Run(new[] { "/Delete", "/TN", TaskName, "/F" }, out message);

    private static bool Run(string[] arguments, out string message)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi);
            if (process == null)
            {
                message = "无法启动 schtasks";
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(8000);

            message = process.ExitCode == 0
                ? "已更新开机启动设置"
                : $"设置失败（退出码 {process.ExitCode}）：{error}{output}".Trim();
            Logger.Write("开机启动：" + message);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            message = "设置失败：" + ex.Message;
            Logger.Write("开机启动：" + message);
            return false;
        }
    }
}
