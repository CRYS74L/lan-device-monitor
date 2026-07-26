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
            ["MicaPanelColor"] = "#0CFFFFFF",
            ["MicaCardColor"] = "#28FFFFFF",
            ["MicaStrongColor"] = "#44FFFFFF",
            ["SurfaceColor"] = "#28FFFFFF",
            ["SurfaceStrongColor"] = "#44FFFFFF",
            ["WindowColor"] = "#08FFFFFF",
            ["SidebarColor"] = "#08FFFFFF",
            ["DeviceIconColor"] = "#76EDF3FF",
            ["DangerTextColor"] = "#C4362E",
            ["DangerSurfaceColor"] = "#90FFF0F0",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["BorderColor"] = "#9AD4D4D4",
            ["ControlColor"] = "#8CFFFFFF",
            ["ControlHoverColor"] = "#B8FFFFFF",
            ["ComboBoxColor"] = "#8CFFFFFF",
            ["ComboBoxHoverColor"] = "#B8FFFFFF",
            ["ComboBoxPopupColor"] = "#E6FFFFFF",
            ["QuietControlColor"] = "#78F2F2F2",
            ["ListItemColor"] = "#52FFFFFF",
            ["ListItemHoverColor"] = "#8AFFFFFF",
            ["DeviceListItemHoverColor"] = "#E2FFFFFF",
            ["DeviceListItemSelectedHoverColor"] = "#DCF2F6FF",
            ["OnlineCardColor"] = "#84DDF4E7",
            ["OfflineCardColor"] = "#84FDECEC",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141",
            ["TimelineOnlineDotColor"] = "#68D9A3",
            ["TimelineOfflineDotColor"] = "#FF8585",
            ["TimelineItemColor"] = "#70FFFFFF",
            ["TimelineItemBorderColor"] = "#70D6D6D6",
            ["TimelineLineColor"] = "#A8A8A8",
            ["TimelineDurationColor"] = "#DCEDEDED",
            ["ScrollThumbColor"] = "#88666666",
            ["ScrollThumbHoverColor"] = "#96666666"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#7EA2FF",
            ["AccentHoverColor"] = "#91B1FF",
            ["AccentPressedColor"] = "#6D91F4",
            ["AccentLightColor"] = "#59314B76",
            ["MicaPanelColor"] = "#0C202020",
            ["MicaCardColor"] = "#322A2A2A",
            ["MicaStrongColor"] = "#50303030",
            ["SurfaceColor"] = "#322A2A2A",
            ["SurfaceStrongColor"] = "#50303030",
            ["WindowColor"] = "#0C181818",
            ["SidebarColor"] = "#0C181818",
            ["DeviceIconColor"] = "#663A5C86",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#7A55282D",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A555555",
            ["ControlColor"] = "#802F2F2F",
            ["ControlHoverColor"] = "#A83A3A3A",
            ["ComboBoxColor"] = "#802F2F2F",
            ["ComboBoxHoverColor"] = "#A83A3A3A",
            ["ComboBoxPopupColor"] = "#E62A2A2A",
            ["QuietControlColor"] = "#78353535",
            ["ListItemColor"] = "#522A2A2A",
            ["ListItemHoverColor"] = "#8A3A3A3A",
            ["DeviceListItemHoverColor"] = "#A84A4A4A",
            ["DeviceListItemSelectedHoverColor"] = "#80466286",
            ["OnlineCardColor"] = "#84264B3B",
            ["OfflineCardColor"] = "#844A2C35",
            ["StatusOnlineColor"] = "#A82A5A45",
            ["StatusOfflineColor"] = "#A85A303A",
            ["StatusOnlineTextColor"] = "#9DE3BE",
            ["StatusOfflineTextColor"] = "#FFB6B6",
            ["TimelineOnlineDotColor"] = "#6CEFAE",
            ["TimelineOfflineDotColor"] = "#FF9B9B",
            ["TimelineItemColor"] = "#8A2B2B2B",
            ["TimelineItemBorderColor"] = "#A0525252",
            ["TimelineLineColor"] = "#898989",
            ["TimelineDurationColor"] = "#B03A3A3A",
            ["ScrollThumbColor"] = "#96C2C2C2",
            ["ScrollThumbHoverColor"] = "#A5C2C2C2"
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
