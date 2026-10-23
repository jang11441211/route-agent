using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RouteAgent.Models;
using RouteAgent.Services;
using MessageBox = System.Windows.MessageBox;

namespace RouteAgent.Views;

public partial class ProcessPickerWindow : Window
{
    private readonly ObservableCollection<ProcessItem> _items = new();
    private readonly ICollectionView _view;
    private bool _loaded;

    public ProcessPickerWindow(List<AdapterInfo> adapters, string? defaultAdapter)
    {
        InitializeComponent();

        // 高 DPI 小屏幕上避免窗口比工作区还高，导致底部按钮被任务栏/屏幕边缘切掉
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width - 40);
        Height = Math.Min(Height, work.Height - 40);

        AdapterCombo.ItemsSource = adapters;
        if (!string.IsNullOrWhiteSpace(defaultAdapter))
            AdapterCombo.SelectedValue = defaultAdapter;
        else if (adapters.Count > 0)
            AdapterCombo.SelectedIndex = 0;

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = Filter;
        ProcessList.ItemsSource = _view;

        Loaded += OnLoaded;
    }

    public IReadOnlyList<ProcessItem> SelectedProcesses { get; private set; } = Array.Empty<ProcessItem>();

    public string SelectedAdapter { get; private set; } = "";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        LoadingText.Visibility = Visibility.Visible;
        try
        {
            var list = await Task.Run(ProcessService.GetRunningProcesses);
            foreach (var item in list)
            {
                item.PropertyChanged += OnItemPropertyChanged;
                _items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Logger.Write("读取进程列表失败：" + ex.Message);
        }
        finally
        {
            LoadingText.Visibility = Visibility.Collapsed;
            ProcessList.Visibility = Visibility.Visible;
            UpdateCount();
            UpdateEmptyState();
        }
    }

    private bool Filter(object item)
    {
        if (item is not ProcessItem process) return false;
        var text = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return true;

        return process.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
               || process.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
               || process.ExecutablePath.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessItem.IsSelected)) UpdateCount();
    }

    private void UpdateCount()
    {
        SelectedCountText.Text = $"已选择 {_items.Count(i => i.IsSelected)} 项";
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SearchHint != null)
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        _view?.Refresh();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (EmptyText == null) return;

        // 列表还没加载完时不要显示“没有匹配”
        EmptyText.Visibility = ProcessList.Visibility == Visibility.Visible && _view != null && _view.IsEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请至少勾选一个程序。", "添加规则",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AdapterCombo.SelectedItem is not AdapterInfo adapter)
        {
            MessageBox.Show(this, "请选择出口网卡。", "添加规则",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedProcesses = selected;
        SelectedAdapter = adapter.Name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
