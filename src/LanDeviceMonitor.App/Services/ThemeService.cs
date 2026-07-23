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
            ["SurfaceColor"] = "#C7FFFFFF",
            ["SurfaceStrongColor"] = "#B8FFFFFF",
            ["WindowColor"] = "#80F1F5FC",
            ["SidebarColor"] = "#60F6F9FF",
            ["DeviceIconColor"] = "#D6EDF3FF",
            ["DangerTextColor"] = "#C4362E",
            ["DangerSurfaceColor"] = "#FFF0F0",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["BorderColor"] = "#A8DCE2EC",
            ["ControlColor"] = "#D9FFFFFF",
            ["ControlHoverColor"] = "#F2FFFFFF",
            ["QuietControlColor"] = "#C6E9EDF5",
            ["ListItemColor"] = "#6AFFFFFF",
            ["ListItemHoverColor"] = "#C7F0F3F8",
            ["OnlineCardColor"] = "#EDF8F1",
            ["OfflineCardColor"] = "#FDECEC",
            ["StatusOnlineColor"] = "#DDF5E8",
            ["StatusOfflineColor"] = "#FCE8E8",
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
            ["SurfaceColor"] = "#C51F2733",
            ["SurfaceStrongColor"] = "#C2262F3D",
            ["WindowColor"] = "#99171E29",
            ["SidebarColor"] = "#6018212E",
            ["DeviceIconColor"] = "#663A5C86",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#7A55282D",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A465166",
            ["ControlColor"] = "#D42A3443",
            ["ControlHoverColor"] = "#F0364254",
            ["QuietControlColor"] = "#D1323C4B",
            ["ListItemColor"] = "#8A252E3A",
            ["ListItemHoverColor"] = "#D2344051",
            ["OnlineCardColor"] = "#B8264B3B",
            ["OfflineCardColor"] = "#B84A2C35",
            ["StatusOnlineColor"] = "#D02A5A45",
            ["StatusOfflineColor"] = "#D05A303A",
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
