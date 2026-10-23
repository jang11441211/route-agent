using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using RouteAgent.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RouteAgent;

public partial class App : Application
{
    public static bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureDirectories();
        CoreService.EnsureBundledInstalled();
        StartMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        Logger.Write($"RouteAgent 启动，管理员权限：{(AdminHelper.IsElevated ? "是" : "否")}");

        ThemeService.Apply(SettingsService.Load().Theme);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        base.OnStartup(e);

        var window = new Views.MainWindow { StartHidden = StartMinimized };
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Write("未处理异常：" + e.Exception);
        MessageBox.Show("程序遇到错误：\n" + e.Exception.Message, "RouteAgent",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
