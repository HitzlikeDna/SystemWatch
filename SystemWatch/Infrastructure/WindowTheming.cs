using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SystemWatch.Native;

namespace SystemWatch.Infrastructure;

/// <summary>Färbt die Windows-Titelleiste passend zum dunklen Design ein.</summary>
internal static class WindowTheming
{
    public static void ApplyDarkTitleBar(Window window, Color caption, Color text, Color border)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

        int enabled = 1;
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
