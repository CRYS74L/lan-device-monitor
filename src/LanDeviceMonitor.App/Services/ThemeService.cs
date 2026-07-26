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
            ["AccentLightColor"] = "#CAFFFFFF",
            ["MicaPanelColor"] = "#0CFFFFFF",
            ["MicaCardColor"] = "#70FFFFFF",
            ["MicaStrongColor"] = "#88FFFFFF",
            ["SurfaceColor"] = "#70FFFFFF",
            ["SurfaceStrongColor"] = "#88FFFFFF",
            ["WindowColor"] = "#08FFFFFF",
            ["SidebarColor"] = "#08FFFFFF",
            ["DeviceIconColor"] = "#76EDF3FF",
            ["DangerTextColor"] = "#C4362E",
            ["DangerSurfaceColor"] = "#90FFF0F0",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["BorderColor"] = "#9AD4D4D4",
            ["ControlColor"] = "#B8FFFFFF",
            ["ControlHoverColor"] = "#DCFFFFFF",
            ["ComboBoxColor"] = "#B8FFFFFF",
            ["ComboBoxHoverColor"] = "#DCFFFFFF",
            ["ComboBoxPopupColor"] = "#E6FFFFFF",
            ["QuietControlColor"] = "#B0F5F5F5",
            ["ListItemColor"] = "#98FFFFFF",
            ["ListItemHoverColor"] = "#C0FFFFFF",
            ["DeviceListItemHoverColor"] = "#FAFFFFFF",
            ["DeviceListItemSelectedHoverColor"] = "#D8FFFFFF",
            ["OnlineCardColor"] = "#84D9F8E6",
            ["OfflineCardColor"] = "#84FFEAEA",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141",
            ["TimelineOnlineDotColor"] = "#68D9A3",
            ["TimelineOfflineDotColor"] = "#FF8585",
            ["TimelineItemColor"] = "#B0FFFFFF",
            ["TimelineItemBorderColor"] = "#70D6D6D6",
            ["TimelineLineColor"] = "#A8A8A8",
            ["TimelineDurationColor"] = "#F0F4F4F4",
            ["ScrollThumbColor"] = "#88666666",
            ["ScrollThumbHoverColor"] = "#8F666666"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#7EA2FF",
            ["AccentHoverColor"] = "#91B1FF",
            ["AccentPressedColor"] = "#6D91F4",
            ["AccentLightColor"] = "#8D585858",
            ["MicaPanelColor"] = "#0C202020",
            ["MicaCardColor"] = "#70505050",
            ["MicaStrongColor"] = "#88585858",
            ["SurfaceColor"] = "#70505050",
            ["SurfaceStrongColor"] = "#88585858",
            ["WindowColor"] = "#0C181818",
            ["SidebarColor"] = "#0C181818",
            ["DeviceIconColor"] = "#663A5C86",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#7A55282D",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A555555",
            ["ControlColor"] = "#A8585858",
            ["ControlHoverColor"] = "#C8686868",
            ["ComboBoxColor"] = "#A8585858",
            ["ComboBoxHoverColor"] = "#C8686868",
            ["ComboBoxPopupColor"] = "#E6464646",
            ["QuietControlColor"] = "#A8505050",
            ["ListItemColor"] = "#88505050",
            ["ListItemHoverColor"] = "#B0606060",
            ["DeviceListItemHoverColor"] = "#C8707070",
            ["DeviceListItemSelectedHoverColor"] = "#B05A5A5A",
            ["OnlineCardColor"] = "#8419583D",
            ["OfflineCardColor"] = "#845B1B2E",
            ["StatusOnlineColor"] = "#A82A5A45",
            ["StatusOfflineColor"] = "#A85A303A",
            ["StatusOnlineTextColor"] = "#9DE3BE",
            ["StatusOfflineTextColor"] = "#FFB6B6",
            ["TimelineOnlineDotColor"] = "#6CEFAE",
            ["TimelineOfflineDotColor"] = "#FF9B9B",
            ["TimelineItemColor"] = "#B0585858",
            ["TimelineItemBorderColor"] = "#A0525252",
            ["TimelineLineColor"] = "#898989",
            ["TimelineDurationColor"] = "#D0686868",
            ["ScrollThumbColor"] = "#96C2C2C2",
            ["ScrollThumbHoverColor"] = "#9EC2C2C2"
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
