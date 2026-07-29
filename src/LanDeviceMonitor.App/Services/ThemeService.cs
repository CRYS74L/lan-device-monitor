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
            ["AccentLightColor"] = "#DEFFFFFF",
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
            ["CaptionHoverColor"] = "#C8D6D6D6",
            ["CaptionPressedColor"] = "#D8BEBEBE",
            ["ToolTipColor"] = "#F2FFFFFF",
            ["WarningSurfaceColor"] = "#F0FFF4E5",
            ["ProgressInfoColor"] = "#DCEBF1FF",
            ["ProgressSuccessColor"] = "#DCDDF5E8",
            ["ProgressErrorColor"] = "#E6FEE2E2",
            ["ListItemColor"] = "#98FFFFFF",
            ["ListItemHoverColor"] = "#C0FFFFFF",
            ["DeviceListItemColor"] = "#99FFFFFF",
            ["DeviceListItemSelectedColor"] = "#E6FFFFFF",
            ["DeviceListItemHoverColor"] = "#BFFFFFFF",
            ["DeviceListItemSelectedHoverColor"] = "#00FFFFFF",
            ["OnlineCardColor"] = "#84D9F8E6",
            ["OfflineCardColor"] = "#84FFEAEA",
            ["OnlineCardBorderColor"] = "#704CC88A",
            ["OfflineCardBorderColor"] = "#40E36C6C",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141",
            ["TimelineOnlineDotColor"] = "#68D9A3",
            ["TimelineOfflineDotColor"] = "#FF8585",
            ["TimelineItemColor"] = "#40FFFFFF",
            ["TimelineItemBorderColor"] = "#70D6D6D6",
            ["TimelineLineColor"] = "#A8A8A8",
            ["TimelineDurationColor"] = "#80FAFAFA",
            ["ScrollThumbColor"] = "#88666666",
            ["ScrollThumbHoverColor"] = "#8F666666"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#7EA2FF",
            ["AccentHoverColor"] = "#91B1FF",
            ["AccentPressedColor"] = "#6D91F4",
            ["AccentLightColor"] = "#74616161",
            ["MicaPanelColor"] = "#09202020",
            ["MicaCardColor"] = "#54505050",
            ["MicaStrongColor"] = "#66585858",
            ["SurfaceColor"] = "#54505050",
            ["SurfaceStrongColor"] = "#66585858",
            ["WindowColor"] = "#09181818",
            ["SidebarColor"] = "#09181818",
            ["DeviceIconColor"] = "#4D3A5C86",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#5C55282D",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A555555",
            ["ControlColor"] = "#7E585858",
            ["ControlHoverColor"] = "#96686868",
            ["ComboBoxColor"] = "#7E585858",
            ["ComboBoxHoverColor"] = "#96686868",
            ["ComboBoxPopupColor"] = "#AD464646",
            ["QuietControlColor"] = "#7E505050",
            ["CaptionHoverColor"] = "#B05C5C5C",
            ["CaptionPressedColor"] = "#C06A6A6A",
            ["ToolTipColor"] = "#B83E3E3E",
            ["WarningSurfaceColor"] = "#806B532D",
            ["ProgressInfoColor"] = "#705A6170",
            ["ProgressSuccessColor"] = "#7042574C",
            ["ProgressErrorColor"] = "#705C4248",
            ["ListItemColor"] = "#66505050",
            ["ListItemHoverColor"] = "#84606060",
            ["DeviceListItemColor"] = "#66505050",
            ["DeviceListItemSelectedColor"] = "#74616161",
            ["DeviceListItemHoverColor"] = "#96707070",
            ["DeviceListItemSelectedHoverColor"] = "#846A6A6A",
            ["OnlineCardColor"] = "#6319583D",
            ["OfflineCardColor"] = "#635B1B2E",
            ["OnlineCardBorderColor"] = "#9067DDA6",
            ["OfflineCardBorderColor"] = "#8CE36E82",
            ["StatusOnlineColor"] = "#7E2A5A45",
            ["StatusOfflineColor"] = "#7E5A303A",
            ["StatusOnlineTextColor"] = "#9DE3BE",
            ["StatusOfflineTextColor"] = "#FFB6B6",
            ["TimelineOnlineDotColor"] = "#6CEFAE",
            ["TimelineOfflineDotColor"] = "#FF9B9B",
            ["TimelineItemColor"] = "#30585858",
            ["TimelineItemBorderColor"] = "#A0525252",
            ["TimelineLineColor"] = "#898989",
            ["TimelineDurationColor"] = "#A08E8E8E",
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
