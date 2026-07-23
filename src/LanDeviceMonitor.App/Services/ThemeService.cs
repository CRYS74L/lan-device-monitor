using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

public static class ThemeService
{
    public const string SystemMode = "System";
    public const string LightMode = "Light";
    public const string DarkMode = "Dark";
    public static string CurrentMode { get; private set; } = SystemMode;

    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#4D7EF2",
            ["AccentHoverColor"] = "#416FDF",
            ["AccentPressedColor"] = "#3560CB",
            ["AccentLightColor"] = "#C9EAF0FF",
            ["MicaPanelColor"] = "#26FFFFFF",
            ["MicaCardColor"] = "#72FFFFFF",
            ["MicaStrongColor"] = "#98FFFFFF",
            ["SurfaceColor"] = "#72FFFFFF",
            ["SurfaceStrongColor"] = "#98FFFFFF",
            ["WindowColor"] = "#26F1F5FC",
            ["SidebarColor"] = "#26F6F9FF",
            ["DeviceIconColor"] = "#76EDF3FF",
            ["DangerTextColor"] = "#C4362E",
            ["DangerSurfaceColor"] = "#90FFF0F0",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["BorderColor"] = "#A8DCE2EC",
            ["ControlColor"] = "#8CFFFFFF",
            ["ControlHoverColor"] = "#B8FFFFFF",
            ["QuietControlColor"] = "#78E9EDF5",
            ["ListItemColor"] = "#52FFFFFF",
            ["ListItemHoverColor"] = "#8AF0F3F8",
            ["OnlineCardColor"] = "#84DDF4E7",
            ["OfflineCardColor"] = "#84FDECEC",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#7EA2FF",
            ["AccentHoverColor"] = "#91B1FF",
            ["AccentPressedColor"] = "#6D91F4",
            ["AccentLightColor"] = "#59314B76",
            ["MicaPanelColor"] = "#2618202C",
            ["MicaCardColor"] = "#7028313F",
            ["MicaStrongColor"] = "#94303A49",
            ["SurfaceColor"] = "#7028313F",
            ["SurfaceStrongColor"] = "#94303A49",
            ["WindowColor"] = "#26171E29",
            ["SidebarColor"] = "#2618212E",
            ["DeviceIconColor"] = "#663A5C86",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#7A55282D",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A465166",
            ["ControlColor"] = "#80303A49",
            ["ControlHoverColor"] = "#A83A4658",
            ["QuietControlColor"] = "#78323C4B",
            ["ListItemColor"] = "#52252E3A",
            ["ListItemHoverColor"] = "#8A344051",
            ["OnlineCardColor"] = "#84264B3B",
            ["OfflineCardColor"] = "#844A2C35",
            ["StatusOnlineColor"] = "#A82A5A45",
            ["StatusOfflineColor"] = "#A85A303A",
            ["StatusOnlineTextColor"] = "#9DE3BE",
            ["StatusOfflineTextColor"] = "#FFB6B6"
        };

    public static string NormalizeMode(string? mode) =>
        mode is LightMode or DarkMode ? mode : SystemMode;

    public static bool ResolveIsDark(string? mode)
    {
        var normalized = NormalizeMode(mode);
        if (normalized == DarkMode)
        {
            return true;
        }

        if (normalized == LightMode)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Apply(string? mode)
    {
        CurrentMode = NormalizeMode(mode);
        var isDark = ResolveIsDark(CurrentMode);
        var palette = isDark ? DarkPalette : LightPalette;
        var resources = System.Windows.Application.Current.Resources;

        foreach (var (colorKey, colorValue) in palette)
        {
            var color = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(colorValue);
            resources[colorKey] = color;
            var brushKey = colorKey.Replace("Color", "Brush", StringComparison.Ordinal);
            if (resources[brushKey] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                resources[brushKey] = new SolidColorBrush(color);
            }
        }

        return isDark;
    }
}
