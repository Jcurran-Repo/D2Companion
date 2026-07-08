using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace D2Companion.App;

/// <summary>
/// Paints the native title bar and window border with the Diablo II palette from Theme.xaml.
/// Uses the Windows 11 DWM caption-color attributes; on builds that don't support them it
/// falls back to plain dark chrome.
/// </summary>
internal static class TitleBarTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Call once per window; safe to call before the HWND exists.</summary>
    public static void Apply(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyNow(window);
        else
            window.SourceInitialized += (_, _) => ApplyNow(window);
    }

    private static void ApplyNow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        var captionResult = SetColor(hwnd, DwmwaCaptionColor, ThemeColor("D2.WindowBgBrush"));
        SetColor(hwnd, DwmwaTextColor, ThemeColor("D2.GoldBrush"));
        SetColor(hwnd, DwmwaBorderColor, ThemeColor("D2.BorderBrush"));

        // Windows 10 doesn't know the color attributes; at least ask for dark chrome.
        if (captionResult != 0)
        {
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
    }

    private static int SetColor(IntPtr hwnd, int attribute, Color color)
    {
        // DWM takes a COLORREF: 0x00BBGGRR.
        var colorRef = color.R | color.G << 8 | color.B << 16;
        return DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
    }

    private static Color ThemeColor(string brushKey)
        => ((SolidColorBrush)Application.Current.Resources[brushKey]).Color;
}
