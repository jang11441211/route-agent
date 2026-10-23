using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RouteAgent.Models;
using RouteAgent.Services;
using RouteAgent.ViewModels;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RouteAgent.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private WinForms.NotifyIcon? _tray;
    private bool _exiting;
    private bool _suppressAutoStart;
    private bool _trayHintShown;
    private bool _logScrollQueued;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => MessageBox.Show(this, message, "RouteAgent",
                MessageBoxButton.OK, MessageBoxImage.Warning));

        _vm.Logs.CollectionChanged += OnLogsChanged;

        CreateTray();
    }

    public bool StartHidden { get; set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizeGlyph();
        await _vm.InitializeAsync();

        if (StartHidden)
        {
            Hide();
            ShowTrayHint();
        }
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _vm.Logs.Count == 0) return;
        if (Nav.SelectedIndex != 3 || _logScrollQueued) return;

        // 不能在 CollectionChanged 回调里直接 ScrollIntoView：内核日志密集追加时
        // 会触发嵌套布局，导致 ItemContainerGenerator 与 Logs 计数不一致而闪退。
        _logScrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _logScrollQueued = false;
            if (Nav.SelectedIndex == 3 && _vm.Logs.Count > 0)
                LogList.ScrollIntoView(_vm.Logs[^1]);
        }));
    }

    // ================= 窗口按钮 =================

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeGlyph();

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph == null || RestoreGlyph == null) return;

        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _vm.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            ShowTrayHint();
            return;
        }

        _vm.Save();
        try
        {
            Task.Run(() => _vm.StopAsync()).Wait(3500);
        }
        catch
        {
            // 退出时忽略错误
        }

        _tray?.Dispose();
        _tray = null;

        Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
    }

    // ================= 托盘 =================

    private void CreateTray()
    {
        System.Drawing.Icon icon;
        try
        {
            var path = Environment.ProcessPath;
            icon = path != null
                ? System.Drawing.Icon.ExtractAssociatedIcon(path) ?? System.Drawing.SystemIcons.Application
                : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "RouteAgent 应用分流",
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => RestoreFromTray());
        menu.Items.Add("启动分流", null, async (_, _) => await _vm.StartAsync());
        menu.Items.Add("停止分流", null, async (_, _) => await _vm.StopAsync());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        menu.Opening += (_, _) =>
        {
            if (menu.Items.Count >= 3)
            {
                menu.Items[1].Enabled = _vm.CanStart;
                menu.Items[2].Enabled = _vm.CanStop;
            }
        };

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ShowTrayHint()
    {
        if (_trayHintShown || _tray == null) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(2600, "RouteAgent",
            _vm.IsRunning ? "分流仍在后台运行。" : "程序已最小化到托盘。",
            WinForms.ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    // ================= 操作 =================

    private async void Start_Click(object sender, RoutedEventArgs e) => await _vm.StartAsync();

    private async void Stop_Click(object sender, RoutedEventArgs e) => await _vm.StopAsync();

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        _vm.Elevate();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddRuleWindow(_vm.Adapters.ToList(), _vm.DefaultAdapterName) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var rule = _vm.AddRuleFromExecutable(dialog.SelectedPath, dialog.SelectedAdapter);
            Logger.Write($"已添加规则：{rule.ProcessName} → {rule.AdapterName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "添加失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PickProcess_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProcessPickerWindow(_vm.Adapters.ToList(), _vm.DefaultAdapterName) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var selected = dialog.SelectedProcesses.ToList();
        _vm.AddRulesFromProcesses(selected, dialog.SelectedAdapter);
        Logger.Write($"从运行中的程序添加了 {selected.Count} 条规则 → {dialog.SelectedAdapter}");
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoutingRule rule })
            _vm.RemoveRule(rule);
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshAdapters();
        Logger.Write("已刷新网卡列表");
    }

    private void InstallCore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CoreInstallWindow(_vm) { Owner = this };
        dialog.ShowDialog();
    }

    private void OpenCoreDir_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.CoreDir);
        OpenInExplorer(AppPaths.CoreDir);
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.ConfigPath)) OpenInExplorer("/select,\"" + AppPaths.ConfigPath + "\"");
        else OpenInExplorer(AppPaths.Root);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.LogPath)) OpenInExplorer("/select,\"" + AppPaths.LogPath + "\"");
        else OpenInExplorer(AppPaths.Root);
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e) => _vm.ClearLogs();

    private void PauseLogs_Click(object sender, RoutedEventArgs e) => _vm.ToggleLogPause();

    private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressAutoStart) return;

        var desired = AutoStartToggle.IsChecked == true;
        var ok = _vm.ApplyAutoStart(desired, out var message);

        _suppressAutoStart = true;
        AutoStartToggle.IsChecked = _vm.Settings.AutoStartWithWindows;
        _suppressAutoStart = false;

        if (!ok)
            MessageBox.Show(this, message, "开机启动", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void OpenInExplorer(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Write("打开资源管理器失败：" + ex.Message);
        }
    }
}
