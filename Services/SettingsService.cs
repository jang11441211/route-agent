using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using RouteAgent.Models;

namespace RouteAgent.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex)
        {
            Logger.Write("读取设置失败，使用默认设置：" + ex.Message);
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(settings, Options);
            var temp = AppPaths.SettingsPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, AppPaths.SettingsPath, true);
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            Logger.Write("保存设置失败：" + ex.Message);
        }
    }
}
