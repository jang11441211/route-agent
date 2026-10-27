using System.IO;
using System.Windows;
using RouteAgent.Models;
using RouteAgent.Services;
using MessageBox = System.Windows.MessageBox;

namespace RouteAgent.Views;

public partial class AddRuleWindow : Window
{
    public AddRuleWindow(List<AdapterInfo> adapters, string? defaultAdapter)
    {
        InitializeComponent();

        AdapterCombo.ItemsSource = adapters;
        if (!string.IsNullOrWhiteSpace(defaultAdapter))
            AdapterCombo.SelectedValue = defaultAdapter;
        else if (adapters.Count > 0)
            AdapterCombo.SelectedIndex = 0;
    }

    public string SelectedPath { get; private set; } = "";

    public string SelectedAdapter { get; private set; } = "";

    public bool IncludeChildren => ChildrenCheck.IsChecked == true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "应用程序 (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        PathBox.Text = dialog.FileName;
        AppIcon.Source = ProcessService.ExtractIcon(dialog.FileName);
        OkButton.IsEnabled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathBox.Text) || !File.Exists(PathBox.Text))
        {
            MessageBox.Show(this, "请选择一个有效的应用程序。", "添加应用",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AdapterCombo.SelectedItem is not AdapterInfo adapter)
        {
            MessageBox.Show(this, "请选择这条规则的出口网卡。", "添加应用",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedPath = PathBox.Text;
        SelectedAdapter = adapter.Name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
