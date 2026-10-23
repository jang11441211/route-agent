using System.Windows;
using Application = System.Windows.Application;

namespace RouteAgent.Services;

/// <summary>深色 / 浅色主题切换。</summary>
public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static string Current { get; private set; } = Dark;

    /// <summary>替换调色板资源字典，界面通过 DynamicResource 实时刷新。</summary>
    public static void Apply(string? themeName)
    {
        var theme = string.Equals(themeName, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;
        var app = Application.Current;
        if (app == null) return;

        var dicts = app.Resources.MergedDictionaries;
        for (var i = dicts.Count - 1; i >= 0; i--)
        {
            var source = dicts[i].Source?.OriginalString;
            if (source != null && source.Contains("Palette.", StringComparison.OrdinalIgnoreCase))
                dicts.RemoveAt(i);
        }

        dicts.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Theme/Palette.{theme}.xaml", UriKind.Relative),
        });
        Current = theme;
    }

    public static string Normalize(string? value) =>
        string.Equals(value, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;

    public static string DisplayName(string? value) => Normalize(value) == Light ? "浅色" : "深色";
}
