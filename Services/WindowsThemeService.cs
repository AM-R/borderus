using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;

namespace Borderus.Services;

internal readonly record struct WindowsThemePalette(
    bool IsDark,
    MediaColor Accent,
    MediaColor AccentText,
    MediaColor AccentHover,
    MediaColor AccentPressed,
    MediaColor Page,
    MediaColor Card,
    MediaColor Control,
    MediaColor ControlHover,
    MediaColor ControlPressed,
    MediaColor Border,
    MediaColor Text,
    MediaColor SecondaryText,
    MediaColor DisabledText,
    MediaColor Preview,
    MediaColor InactiveTitle);

internal static class WindowsThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    internal static WindowsThemePalette Read()
    {
        bool isDark = ReadDword(PersonalizeKey, "AppsUseLightTheme", 0) == 0;
        MediaColor accent = ReadAccentColor();
        return isDark
            ? new WindowsThemePalette(true, accent, Colors.White, Blend(accent, Colors.White, 0.16), Blend(accent, Colors.Black, 0.18),
                Color(32, 32, 32), Color(43, 43, 43), Color(51, 51, 51), Color(62, 62, 62), Color(45, 45, 45),
                Color(82, 82, 82), Color(255, 255, 255), Color(200, 200, 200), Color(128, 128, 128),
                Color(23, 23, 23), Color(65, 65, 65))
            : new WindowsThemePalette(false, accent, Colors.White, Blend(accent, Colors.Black, 0.08), Blend(accent, Colors.Black, 0.16),
                Color(243, 243, 243), Colors.White, Colors.White, Color(229, 229, 229), Color(204, 204, 204),
                Color(173, 173, 173), Color(0, 0, 0), Color(96, 96, 96), Color(109, 109, 109),
                Colors.White, Color(230, 230, 230));
    }

    private static MediaColor ReadAccentColor()
    {
        uint value = unchecked((uint)ReadDword(DwmKey, "ColorizationColor", unchecked((int)0xFFC55A11)));
        byte alpha = (byte)(value >> 24);
        byte red = (byte)(value >> 16);
        byte green = (byte)(value >> 8);
        byte blue = (byte)value;
        return alpha == 0 ? MediaColor.FromRgb(0, 120, 215) : MediaColor.FromRgb(red, green, blue);
    }

    private static int ReadDword(string keyPath, string valueName, int fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) is int value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static MediaColor Color(byte red, byte green, byte blue) => MediaColor.FromRgb(red, green, blue);

    private static MediaColor Blend(MediaColor source, MediaColor target, double amount) => MediaColor.FromRgb(
        (byte)Math.Round(source.R + (target.R - source.R) * amount),
        (byte)Math.Round(source.G + (target.G - source.G) * amount),
        (byte)Math.Round(source.B + (target.B - source.B) * amount));

    private static class Colors
    {
        internal static readonly MediaColor Black = MediaColor.FromRgb(0, 0, 0);
        internal static readonly MediaColor White = MediaColor.FromRgb(255, 255, 255);
    }
}
