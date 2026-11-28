using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SystemWatch.Native;

namespace SystemWatch.Infrastructure;

/// <summary>Färbt die Windows-Titelleiste passend zum aktiven Design ein.</summary>
internal static class WindowTheming
{
    /// <summary>Übernimmt Seiten-, Text- und Rahmenfarbe des aktiven Designs.</summary>
    public static void ApplyTitleBar(Window window) => ApplyTitleBar(window,
        dark: !ThemeManager.IsLight,
        caption: ThemeManager.GetColor("PageColor", Color.FromRgb(0x10, 0x10, 0x10)),
        text: ThemeManager.GetColor("TextPrimaryColor", Color.FromRgb(0xF0, 0xEF, 0xEC)),
        border: ThemeManager.GetColor("WindowBorderColor", Color.FromRgb(0x2C, 0x2C, 0x2A)));

    public static void ApplyTitleBar(Window window, bool dark, Color caption, Color text, Color border)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

        int enabled = dark ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));

        // Ab Windows 11: exakte Farben (auf Windows 10 ohne Wirkung).
        int captionColor = ToColorRef(caption);
        int textColor = ToColorRef(text);
        int borderColor = ToColorRef(border);
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaCaptionColor, ref captionColor, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaTextColor, ref textColor, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
