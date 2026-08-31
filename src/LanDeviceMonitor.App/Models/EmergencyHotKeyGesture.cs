using System.Windows.Input;

namespace LanDeviceMonitor.Models;

internal readonly record struct EmergencyHotKeyGesture(
    uint Modifiers,
    uint VirtualKey)
{
    internal const uint ModifierAlt = 0x0001;
    internal const uint ModifierControl = 0x0002;
    internal const uint ModifierShift = 0x0004;
    internal const uint ModifierWin = 0x0008;
    internal const uint SupportedModifiers =
        ModifierAlt | ModifierControl | ModifierShift | ModifierWin;
    internal const uint DefaultModifiers = SupportedModifiers;
    internal const uint DefaultVirtualKey = 0x70;
    internal const uint NoRepeatModifier = 0x4000;

    internal static EmergencyHotKeyGesture Default =>
        new(DefaultModifiers, DefaultVirtualKey);

    public string DisplayText => FormatDisplayText(Modifiers, VirtualKey);

    internal uint RegistrationModifiers => Modifiers | NoRepeatModifier;

    internal static bool TryCreate(
        uint modifiers,
        uint virtualKey,
        out EmergencyHotKeyGesture gesture)
    {
        modifiers &= SupportedModifiers;
        if (CountModifiers(modifiers) < 2 || IsModifierKey(virtualKey))
        {
            gesture = default;
            return false;
        }

        gesture = new EmergencyHotKeyGesture(modifiers, virtualKey);
        return true;
    }

    private static int CountModifiers(uint modifiers)
    {
        var count = 0;
        foreach (var modifier in new[]
                 {
                     ModifierAlt,
                     ModifierControl,
                     ModifierShift,
                     ModifierWin
                 })
        {
            if ((modifiers & modifier) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsModifierKey(uint virtualKey) =>
        virtualKey is 0 or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C;

    private static string FormatDisplayText(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>(5);
        if ((modifiers & ModifierControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModifierAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModifierShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModifierWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(virtualKey));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(uint virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Back => "Backspace",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            _ => key.ToString()
        };
    }
}
