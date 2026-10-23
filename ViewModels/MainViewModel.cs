using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using RouteAgent.Models;
using RouteAgent.Services;
using Application = System.Windows.Application;

namespace RouteAgent.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CoreService _core = new();
    private bool _isRunning;
    private bool _isBusy;
    private bool _isAdmin;
    private bool _isCoreReady;
    private string _statusText = "未运行";
    private string _coreVersionText = "未安装";
    private string _downloadText = "";
    private string _downloadUrl = "";
    private bool _stopping;
    private bool _saving;

    /// <summary>日志页最多显示的行数，超出后从头部丢弃，保持滚动刷新。</summary>
    private const int MaxLogLines = 1500;

    private readonly List<string> _pendingLogs = new();
    private bool _isLogPaused;

    public MainViewModel()
    {
        Settings = SettingsService.Load();

        foreach (var rule in Settings.Rules) Rules.Add(rule);

        Rules.CollectionChanged += OnRulesChanged;
        foreach (var rule in Rules) rule.PropertyChanged += OnRuleChanged;
        Settings.PropertyChanged += (_, e) =>
        {
            // Rules 由 Save() 自己同步，不能再触发 Save，否则会无限递归
            if (e.PropertyName != nameof(AppSettings.Rules)) Save();
        };

        _core.LogReceived += line => OnUi(() => AppendLog("[内核] " + line));
        _core.Exited += () => OnUi(HandleCoreExited);
        Logger.LineWritten += line => OnUi(() => AppendLog(line));

        foreach (var line in Logger.RecentLines.TakeLast(300)) Logs.Add(line);

        IsAdmin = AdminHelper.IsElevated;
        if (!IsAdmin) StatusText = "需要管理员权限";
    }

    public AppSettings Settings { get; }

    public ObservableCollection<AdapterInfo> Adapters { get; } = new();
    public ObservableCollection<RoutingRule> Rules { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public event Action<string>? ErrorOccurred;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            RaiseState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            RaiseState();
        }
    }

    public bool IsAdmin
    {
        get => _isAdmin;
        private set
        {
            if (!Set(ref _isAdmin, value)) return;
            RaiseState();
        }
    }

    public bool IsCoreReady
    {
        get => _isCoreReady;
        private set
        {
            if (!Set(ref _isCoreReady, value)) return;
            OnPropertyChanged(nameof(CoreStatusText));
            RaiseState();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string CoreStatusText => !IsCoreReady
        ? "未安装内核"
        : CoreDownloader.IsDriverReady ? "内核已就绪" : "缺少 TUN 驱动";

    public string DownloadUrl
    {
        get => _downloadUrl;
        set => Set(ref _downloadUrl, value);
    }

    public string CoreVersionText
    {
        get => _coreVersionText;
        private set => Set(ref _coreVersionText, value);
    }

    public string DownloadText
    {
        get => _downloadText;
        private set => Set(ref _downloadText, value);
    }

    public bool HasRules => Rules.Count > 0;

    public string RuleSummary => $"{Rules.Count} 条规则 · {Rules.Count(r => r.Enabled)} 条已启用";

    public string AdapterSummary => $"{Adapters.Count} 张网卡";

    public bool CanStart => IsAdmin && IsCoreReady && !IsRunning && !IsBusy;

    public bool CanStop => IsRunning && !IsBusy;

    public bool ShowAdminBanner => !IsAdmin;

    public string ConfigPath => AppPaths.ConfigPath;
    public string CoreDirectory => AppPaths.CoreDir;
    public string LogPath => AppPaths.LogPath;

    /// <summary>外观主题，切换后立即生效并保存。</summary>
    public string Theme
    {
        get => ThemeService.Normalize(Settings.Theme);
        set
        {
            var theme = ThemeService.Normalize(value);
            if (theme == ThemeService.Normalize(Settings.Theme)) return;
            Settings.Theme = theme;
            ThemeService.Apply(theme);
            OnPropertyChanged();
        }
    }

    public string? DefaultAdapterName =>
        Adapters.FirstOrDefault(a => a.IsDefault && a.IsUp)?.Name
        ?? Adapters.FirstOrDefault(a => a.IsUp)?.Name
        ?? Adapters.FirstOrDefault()?.Name;

    public async Task InitializeAsync()
    {
        RefreshAdapters();

        foreach (var rule in Rules)
        {
            if (rule.Icon == null && File.Exists(rule.ExecutablePath))
                rule.Icon = ProcessService.ExtractIcon(rule.ExecutablePath);
        }

        await RefreshCoreInfoAsync();
        Settings.AutoStartWithWindows = AutoStartService.IsEnabled();

        if (Settings.AutoStartRouting && IsAdmin && IsCoreReady && Rules.Any(r => r.Enabled))
            await StartAsync();
    }

    public void RefreshAdapters()
    {
        var latest = NetworkService.GetAdapters(Settings.TunDevice);
        var names = latest.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 不能直接 Clear()：集合清空会让每条规则的 ComboBox 选中项变为 null，
        // TwoWay 绑定随即把 null 写回 AdapterName，表现为“选了网卡后名字消失”。
        for (int i = Adapters.Count - 1; i >= 0; i--)
        {
            if (!names.Contains(Adapters[i].Name)) Adapters.RemoveAt(i);
        }

        foreach (var adapter in latest)
        {
            var existing = Adapters.FirstOrDefault(a =>
                string.Equals(a.Name, adapter.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) Adapters.Add(adapter);
            else existing.CopyFrom(adapter);
        }

        OnPropertyChanged(nameof(DefaultAdapterName));
        OnPropertyChanged(nameof(AdapterSummary));
    }

    public async Task RefreshCoreInfoAsync()
    {
        var corePath = CoreService.ResolveCorePath(Settings);
        IsCoreReady = corePath != null;
        CoreVersionText = corePath == null
            ? "未安装"
            : await CoreService.GetCoreVersionAsync(corePath) ?? "无法读取版本";

        if (corePath != null)
            Logger.Write("内核路径：" + corePath);

        OnPropertyChanged(nameof(CoreStatusText));
    }

    public async Task StartAsync()
    {
        if (IsRunning || IsBusy) return;

        if (!IsAdmin)
        {
            StatusText = "需要管理员权限";
            ErrorOccurred?.Invoke("创建 TUN 虚拟网卡需要管理员权限，请点击「以管理员身份重启」。");
            return;
        }

        if (!IsCoreReady)
        {
            StatusText = "未安装内核";
            ErrorOccurred?.Invoke("还没有安装 mihomo 内核，请到「设置」页下载。");
            return;
        }

        var active = Rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.AdapterName)).ToList();
        if (active.Count == 0)
        {
            StatusText = "没有可用的规则";
            ErrorOccurred?.Invoke("请先添加至少一条「应用 → 网卡」规则，并选择出口网卡。");
            return;
        }

        IsBusy = true;
        StatusText = "正在启动…";

        try
        {
            RefreshAdapters();

            var yaml = ConfigBuilder.Build(Settings, Adapters.ToList());
            Directory.CreateDirectory(AppPaths.Root);
            await File.WriteAllTextAsync(AppPaths.ConfigPath, yaml, Encoding.UTF8);
            Logger.Write("已生成配置：" + AppPaths.ConfigPath);
            Logger.Write("开始启动内核…");

            await _core.StartAsync(AppPaths.ConfigPath, Settings);

            _stopping = false;
            IsRunning = true;
            StatusText = "运行中";
            Logger.Write($"分流已开启，共 {active.Count} 条生效规则");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            StatusText = "启动失败";
            Logger.Write("启动失败：" + ex.Message);
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning || IsBusy) return;

        IsBusy = true;
        StatusText = "正在停止…";
        _stopping = true;

        try
        {
            await _core.StopAsync();
            Logger.Write("分流已停止");
        }
        catch (Exception ex)
        {
            Logger.Write("停止失败：" + ex.Message);
        }
        finally
        {
            IsRunning = false;
            StatusText = "未运行";
            IsBusy = false;
        }
    }

    public async Task ToggleAsync()
    {
        if (IsRunning) await StopAsync();
        else await StartAsync();
    }

    public RoutingRule AddRuleFromExecutable(string path, string adapterName)
    {
        var processName = ProcessService.GetProcessNameFromPath(path);
        if (string.IsNullOrWhiteSpace(processName))
            throw new InvalidOperationException("无法识别该程序的文件名。");

        var existing = Rules.FirstOrDefault(r =>
            string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.ExecutablePath = path;
            existing.AdapterName = adapterName;
            existing.Enabled = true;
            return existing;
        }

        string displayName = "";
        try { displayName = FileVersionInfo.GetVersionInfo(path).FileDescription ?? ""; } catch { }

        var rule = new RoutingRule
        {
            ProcessName = processName,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(processName)
                : displayName,
            ExecutablePath = path,
            AdapterName = adapterName,
            Icon = ProcessService.ExtractIcon(path),
        };

        Rules.Add(rule);
        return rule;
    }

    public void AddRulesFromProcesses(IEnumerable<ProcessItem> processes, string adapterName)
    {
        foreach (var item in processes)
        {
            if (string.IsNullOrWhiteSpace(item.ExecutablePath)) continue;
            AddRuleFromExecutable(item.ExecutablePath, adapterName);
        }
    }

    public void RemoveRule(RoutingRule rule)
    {
        if (Rules.Remove(rule))
            Logger.Write($"已删除规则：{rule.Title}");
    }

    public void ClearLogs()
    {
        Logs.Clear();
        _pendingLogs.Clear();
        OnPropertyChanged(nameof(LogPausedText));
        Logger.Clear();
    }

    public Task DownloadCoreAsync() =>
        RunInstallAsync("准备下载…", (p, t) => CoreDownloader.DownloadAsync(p, t));

    public Task DownloadCoreFromUrlAsync() =>
        RunInstallAsync("准备下载…", (p, t) => CoreDownloader.DownloadFromUrlAsync(DownloadUrl, p, t));

    public Task ImportCoreFileAsync(string path) =>
        RunInstallAsync("准备导入…", (p, t) =>
        {
            CoreDownloader.InstallFromFile(path, p, t);
            return Task.CompletedTask;
        });

    private async Task RunInstallAsync(string initialText,
        Func<IProgress<string>, CancellationToken, Task> action)
    {
        if (IsBusy) return;

        IsBusy = true;
        DownloadText = initialText;

        try
        {
            var progress = new Progress<string>(text => DownloadText = text);
            await action(progress, CancellationToken.None);

            DownloadText = CoreDownloader.IsDriverReady
                ? "内核与 TUN 驱动已就绪"
                : "内核已就绪，但缺少 wintun.dll";

            await RefreshCoreInfoAsync();
            Logger.Write("内核安装完成");
        }
        catch (Exception ex)
        {
            DownloadText = "操作失败";
            Logger.Write("内核安装失败：" + ex.Message);
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool ApplyAutoStart(bool enable, out string message)
    {
        if (enable && !IsAdmin)
        {
            message = "设置开机启动需要管理员权限，请先以管理员身份重启。";
            return false;
        }

        var ok = enable
            ? AutoStartService.Enable(out message)
            : AutoStartService.Disable(out message);

        if (ok) Settings.AutoStartWithWindows = enable;
        else Settings.AutoStartWithWindows = AutoStartService.IsEnabled();

        return ok;
    }

    public void Elevate()
    {
        if (AdminHelper.RelaunchElevated())
            Application.Current.Shutdown();
    }

    public void Save()
    {
        if (_saving) return;
        _saving = true;
        try
        {
            Settings.Rules = Rules.ToList();
            SettingsService.Save(Settings);
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (RoutingRule rule in e.NewItems) rule.PropertyChanged += OnRuleChanged;
        if (e.OldItems != null)
            foreach (RoutingRule rule in e.OldItems) rule.PropertyChanged -= OnRuleChanged;

        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(RuleSummary));
        Save();
    }

    private void OnRuleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RuleSummary));
        Save();
    }

    private void HandleCoreExited()
    {
        IsRunning = false;
        if (_stopping)
        {
            StatusText = "未运行";
            return;
        }

        StatusText = "内核已退出";
        ErrorOccurred?.Invoke("分流内核意外退出，请查看「日志」页了解原因。");
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(ShowAdminBanner));
    }

    private void AppendLog(string line)
    {
        // 暂停时只是不再往界面追加，日志文件仍由 Logger 正常写入
        if (_isLogPaused)
        {
            _pendingLogs.Add(line);
            while (_pendingLogs.Count > MaxLogLines) _pendingLogs.RemoveAt(0);
            OnPropertyChanged(nameof(LogPausedText));
            return;
        }

        AddLogLine(line);
    }

    private void AddLogLine(string line)
    {
        Logs.Add(line);
        while (Logs.Count > MaxLogLines) Logs.RemoveAt(0);
    }

    /// <summary>暂停 / 继续日志滚动。暂停期间新增的日志先缓存，继续时一次性补上。</summary>
    public void ToggleLogPause()
    {
        _isLogPaused = !_isLogPaused;
        OnPropertyChanged(nameof(IsLogPaused));
        OnPropertyChanged(nameof(LogPauseButtonText));

        if (!_isLogPaused)
        {
            foreach (var line in _pendingLogs) AddLogLine(line);
            _pendingLogs.Clear();
        }

        OnPropertyChanged(nameof(LogPausedText));
    }

    public bool IsLogPaused => _isLogPaused;

    public string LogPauseButtonText => _isLogPaused ? "继续" : "暂停";

    public string LogPausedText => _isLogPaused ? $"已暂停 · {_pendingLogs.Count} 行待显示" : "";

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
