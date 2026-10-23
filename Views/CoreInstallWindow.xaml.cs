using System.IO;
using System.Windows;
using RouteAgent.Services;
using RouteAgent.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace RouteAgent.Views;

public partial class CoreInstallWindow : Window
{
    private readonly MainViewModel _vm;

    public CoreInstallWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void AutoDownload_Click(object sender, RoutedEventArgs e)
    {
        await _vm.DownloadCoreAsync();

        if (_vm.IsCoreReady && CoreDownloader.IsDriverReady)
            MessageBox.Show(this, "内核与 TUN 驱动已安装完成，可以关闭此窗口并启动分流。", "RouteAgent",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void DownloadUrl_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.DownloadUrl))
        {
            MessageBox.Show(this, "请先粘贴内核下载地址（zip 压缩包或 mihomo.exe 直链）。", "RouteAgent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _vm.DownloadCoreFromUrlAsync();

        if (_vm.IsCoreReady && CoreDownloader.IsDriverReady)
            MessageBox.Show(this, "内核与 TUN 驱动已安装完成，可以关闭此窗口并启动分流。", "RouteAgent",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择内核文件",
            Filter = "内核压缩包 (*.zip)|*.zip|可执行文件 (*.exe)|*.exe|TUN 驱动 (*.dll)|*.dll|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true) return;

        await _vm.ImportCoreFileAsync(dialog.FileName);

        if (_vm.IsCoreReady && CoreDownloader.IsDriverReady)
            MessageBox.Show(this, "内核与 TUN 驱动已安装完成，可以关闭此窗口并启动分流。", "RouteAgent",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenDir_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.CoreDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + AppPaths.CoreDir + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Write("打开内核目录失败：" + ex.Message);
        }
    }
}
