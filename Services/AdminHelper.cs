using System.Diagnostics;
using System.Security.Principal;

namespace RouteAgent.Services;

public static class AdminHelper
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>以管理员身份重新启动当前程序（会弹出 UAC）。</summary>
    public static bool RelaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write("请求管理员权限失败：" + ex.Message);
            return false;
        }
    }
}
