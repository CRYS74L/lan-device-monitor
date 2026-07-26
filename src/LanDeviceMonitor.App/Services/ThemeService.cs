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
            ["MicaPanelColor"] = "#08FFFFFF",
            ["MicaCardColor"] = "#2AFFFFFF",
            ["MicaStrongColor"] = "#48FFFFFF",
            ["SurfaceColor"] = "#2AFFFFFF",
            ["SurfaceStrongColor"] = "#48FFFFFF",
            ["WindowColor"] = "#08F1F5FC",
            ["SidebarColor"] = "#08F6F9FF",
            ["DeviceIconColor"] = "#76EDF3FF",
            ["DangerTextColor"] = "#C4362E",
            ["DangerSurfaceColor"] = "#90FFF0F0",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["BorderColor"] = "#A8DCE2EC",
            ["ControlColor"] = "#8CFFFFFF",
            ["ControlHoverColor"] = "#B8FFFFFF",
            ["ComboBoxColor"] = "#D9FFFFFF",
            ["ComboBoxHoverColor"] = "#EDFFFFFF",
            ["ComboBoxPopupColor"] = "#F2FFFFFF",
            ["QuietControlColor"] = "#78E9EDF5",
            ["ListItemColor"] = "#52FFFFFF",
            ["ListItemHoverColor"] = "#8AF0F3F8",
            ["DeviceListItemHoverColor"] = "#E2FFFFFF",
            ["DeviceListItemSelectedHoverColor"] = "#DCF2F6FF",
            ["OnlineCardColor"] = "#84DDF4E7",
            ["OfflineCardColor"] = "#84FDECEC",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141",
            ["TimelineOnlineDotColor"] = "#4BF6A4",
            ["TimelineOfflineDotColor"] = "#F98B8B",
            ["TimelineItemColor"] = "#70F5F7FB",
            ["TimelineItemBorderColor"] = "#70DCE2EC",
            ["TimelineLineColor"] = "#A3B3C9",
            ["TimelineDurationColor"] = "#DCE8EDF5",
            ["ScrollThumbColor"] = "#88666666",
            ["ScrollThumbHoverColor"] = "#92626262"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#7EA2FF",
            ["AccentHoverColor"] = "#91B1FF",
            ["AccentPressedColor"] = "#6D91F4",
            ["AccentLightColor"] = "#59314B76",
            ["MicaPanelColor"] = "#1018202C",
            ["MicaCardColor"] = "#3E28313F",
            ["MicaStrongColor"] = "#60303A49",
            ["SurfaceColor"] = "#3E28313F",
            ["SurfaceStrongColor"] = "#60303A49",
            ["WindowColor"] = "#10171E29",
            ["SidebarColor"] = "#1018212E",
            ["DeviceIconColor"] = "#663A5C86",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#7A55282D",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A465166",
            ["ControlColor"] = "#80303A49",
            ["ControlHoverColor"] = "#A83A4658",
            ["ComboBoxColor"] = "#D9303A49",
            ["ComboBoxHoverColor"] = "#E63A4658",
            ["ComboBoxPopupColor"] = "#F22A3341",
            ["QuietControlColor"] = "#78323C4B",
            ["ListItemColor"] = "#52252E3A",
            ["ListItemHoverColor"] = "#8A344051",
            ["DeviceListItemHoverColor"] = "#A84A5B73",
            ["DeviceListItemSelectedHoverColor"] = "#80466286",
            ["OnlineCardColor"] = "#84264B3B",
            ["OfflineCardColor"] = "#844A2C35",
            ["StatusOnlineColor"] = "#A82A5A45",
            ["StatusOfflineColor"] = "#A85A303A",
            ["StatusOnlineTextColor"] = "#9DE3BE",
            ["StatusOfflineTextColor"] = "#FFB6B6",
            ["TimelineOnlineDotColor"] = "#64F7AE",
            ["TimelineOfflineDotColor"] = "#FAA0A0",
            ["TimelineItemColor"] = "#8A252E3A",
            ["TimelineItemBorderColor"] = "#A0465166",
            ["TimelineLineColor"] = "#8190A6",
            ["TimelineDurationColor"] = "#B03A4658",
            ["ScrollThumbColor"] = "#96C2C2C2",
            ["ScrollThumbHoverColor"] = "#A0BFBFBF"
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
