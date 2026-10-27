using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace RouteAgent.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>一条分流规则：某个应用固定走某张网卡。</summary>
public sealed class RoutingRule : ObservableObject
{
    private string _processName = "";
    private string _displayName = "";
    private string _executablePath = "";
    private string _adapterName = "";
    private bool _enabled = true;
    private bool _includeChildren;
    private ImageSource? _icon;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>进程名，例如 chrome.exe（mihomo 的 PROCESS-NAME 匹配项）。</summary>
    public string ProcessName
    {
        get => _processName;
        set { if (Set(ref _processName, value)) OnPropertyChanged(nameof(Title)); }
    }

    public string DisplayName
    {
        get => _displayName;
        set { if (Set(ref _displayName, value)) OnPropertyChanged(nameof(Title)); }
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (Set(ref _executablePath, value)) OnPropertyChanged(nameof(CanIncludeChildren));
        }
    }

    public string AdapterName
    {
        get => _adapterName;
        set
        {
            if (Set(ref _adapterName, value)) OnPropertyChanged(nameof(AdapterGroupName));
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>是否把该程序安装目录下的其它进程（子进程）也一并走同一张网卡。</summary>
    public bool IncludeChildren
    {
        get => _includeChildren;
        set => Set(ref _includeChildren, value);
    }

    /// <summary>没有可执行文件路径时无法按目录匹配子进程。</summary>
    [JsonIgnore]
    public bool CanIncludeChildren => !string.IsNullOrWhiteSpace(ExecutablePath);

    /// <summary>规则列表的分组标题：按出口网卡归类，未选网卡的单独一组。</summary>
    [JsonIgnore]
    public string AdapterGroupName => string.IsNullOrWhiteSpace(AdapterName) ? "未分配网卡" : AdapterName;

    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? ProcessName : DisplayName;

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(ExecutablePath) ? ProcessName : ExecutablePath;

    [JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set { if (Set(ref _icon, value)) OnPropertyChanged(nameof(HasIcon)); }
    }

    [JsonIgnore]
    public bool HasIcon => _icon != null;
}

/// <summary>系统中可用于出口的网卡。</summary>
public sealed class AdapterInfo : ObservableObject
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Index { get; set; }
    public string IPv4 { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string Dns { get; set; } = "";
    public string StatusText { get; set; } = "";
    public bool IsUp { get; set; }
    public bool IsDefault { get; set; }
    public string TypeText { get; set; } = "";
    public string SpeedText { get; set; } = "";

    public string Display => string.IsNullOrEmpty(IPv4) ? Name : $"{Name}  ·  {IPv4}";

    public string StatusColor => IsUp ? "#FF34D399" : "#FF667085";

    // 自定义 ComboBox 模板下 DisplayMemberPath 不生效，直接让 ToString 返回展示文本
    public override string ToString() => Display;

    /// <summary>就地更新网卡数据，保留实例（重建集合会丢失下拉框选中项）。</summary>
    public void CopyFrom(AdapterInfo other)
    {
        Description = other.Description;
        Index = other.Index;
        IPv4 = other.IPv4;
        Gateway = other.Gateway;
        Dns = other.Dns;
        StatusText = other.StatusText;
        IsUp = other.IsUp;
        IsDefault = other.IsDefault;
        TypeText = other.TypeText;
        SpeedText = other.SpeedText;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class ProcessItem : ObservableObject
{
    private bool _isSelected;

    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public ImageSource? Icon { get; set; }
    public bool HasIcon => Icon != null;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string Subtitle => string.IsNullOrWhiteSpace(ExecutablePath) ? ProcessName : ExecutablePath;
}

public sealed class AppSettings : ObservableObject
{
    private List<RoutingRule> _rules = new();
    private bool _autoStartRouting;
    private bool _minimizeToTray = true;
    private bool _autoStartWithWindows;
    private bool _enableDnsHijack;
    private string _nameservers = "223.5.5.5, 119.29.29.29";
    private string _logLevel = "info";
    private string _tunDevice = "RouteAgent";
    private string _corePath = "";
    private string _theme = "Dark";

    public List<RoutingRule> Rules
    {
        get => _rules;
        set => Set(ref _rules, value);
    }

    /// <summary>启动应用后自动开启分流。</summary>
    public bool AutoStartRouting
    {
        get => _autoStartRouting;
        set => Set(ref _autoStartRouting, value);
    }

    /// <summary>关闭主窗口时最小化到托盘。</summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => Set(ref _minimizeToTray, value);
    }

    /// <summary>开机自动启动（计划任务，管理员权限静默启动）。</summary>
    public bool AutoStartWithWindows
    {
        get => _autoStartWithWindows;
        set => Set(ref _autoStartWithWindows, value);
    }

    /// <summary>接管 DNS（fake-ip）。默认关闭，交给系统 DNS 客户端处理。</summary>
    public bool EnableDnsHijack
    {
        get => _enableDnsHijack;
        set => Set(ref _enableDnsHijack, value);
    }

    public string Nameservers
    {
        get => _nameservers;
        set => Set(ref _nameservers, value);
    }

    public string LogLevel
    {
        get => _logLevel;
        set => Set(ref _logLevel, value);
    }

    public string TunDevice
    {
        get => _tunDevice;
        set => Set(ref _tunDevice, value);
    }

    /// <summary>自定义内核路径，留空使用默认位置。</summary>
    public string CorePath
    {
        get => _corePath;
        set => Set(ref _corePath, value);
    }

    /// <summary>外观主题：Dark / Light。</summary>
    public string Theme
    {
        get => _theme;
        set => Set(ref _theme, value);
    }
}
