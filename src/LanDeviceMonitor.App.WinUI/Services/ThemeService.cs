using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

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
            ["MicaPanelColor"] = "#0CFFFFFF",
            ["MicaCardColor"] = "#70FFFFFF",
            ["DeviceIconColor"] = "#76EDF3FF",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["BorderColor"] = "#9AD4D4D4",
            ["ControlColor"] = "#B8FFFFFF",
            ["ControlHoverColor"] = "#DCFFFFFF",
            ["QuietControlColor"] = "#B0F5F5F5",
            ["CaptionHoverColor"] = "#2B000000",
            ["CaptionPressedColor"] = "#3D000000",
            ["CloseHoverColor"] = "#FF525C",
            ["ListItemColor"] = "#98FFFFFF",
            ["ListItemSelectedColor"] = "#C0FFFFFF",
            ["DeviceListItemColor"] = "#99FFFFFF",
            ["DeviceListItemSelectedColor"] = "#C0FFFFFF",
            ["DeviceListItemHoverColor"] = "#BFFFFFFF",
            ["OnlineCardColor"] = "#84D9F8E6",
            ["OnlineCardBorderColor"] = "#704CC88A",
            ["OfflineCardColor"] = "#84FFEAEA",
            ["OfflineCardBorderColor"] = "#40E36C6C",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["AccentColor"] = "#7EA2FF",
            ["MicaPanelColor"] = "#09202020",
            ["MicaCardColor"] = "#54505050",
            ["DeviceIconColor"] = "#4D3A5C86",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["BorderColor"] = "#8A555555",
            ["ControlColor"] = "#7E585858",
            ["ControlHoverColor"] = "#96686868",
            ["QuietControlColor"] = "#7E505050",
            ["CaptionHoverColor"] = "#52000000",
            ["CaptionPressedColor"] = "#68000000",
            ["CloseHoverColor"] = "#C42B2B",
            ["ListItemColor"] = "#66505050",
            ["ListItemSelectedColor"] = "#84606060",
            ["DeviceListItemColor"] = "#66505050",
            ["DeviceListItemSelectedColor"] = "#6E616161",
            ["DeviceListItemHoverColor"] = "#96707070",
            ["OnlineCardColor"] = "#6319583D",
            ["OnlineCardBorderColor"] = "#9067DDA6",
            ["OfflineCardColor"] = "#635B1B2E",
            ["OfflineCardBorderColor"] = "#8CE36E82",
            ["StatusOnlineColor"] = "#7E2A5A45",
            ["StatusOfflineColor"] = "#7E5A303A",
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
            return key?.GetValue("AppsUseLightTheme") is int value &&
                   value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Apply(
        string? mode,
        ResourceDictionary resources,
        FrameworkElement root)
    {
        CurrentMode = NormalizeMode(mode);
        var isDark = ResolveIsDark(CurrentMode);
        root.RequestedTheme = isDark
            ? ElementTheme.Dark
            : ElementTheme.Light;

        var palette = isDark ? DarkPalette : LightPalette;
        foreach (var (colorKey, colorValue) in palette)
        {
            var color = ParseColor(colorValue);
            var brushKey = colorKey.Replace(
                "Color",
                "Brush",
                StringComparison.Ordinal);
            if (resources[brushKey] is SolidColorBrush brush)
            {
                brush.Color = color;
            }
        }

        return isDark;
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        var value = hex.TrimStart('#');
        var argb = Convert.ToUInt32(value, 16);
        return Windows.UI.Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }
}
