using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NetOpsTerminal.App.Views;

/// <summary>Uses the shared application palette for native Windows captions.</summary>
public class ThemedWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwcpRound = 2;

    public ThemedWindow()
    {
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        IntPtr? platformHandle = TryGetPlatformHandle()?.Handle;
        if (!OperatingSystem.IsWindows() || platformHandle is not { } handle || handle == IntPtr.Zero)
            return;

        int enabled = 1;
        if (SetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled) < 0)
            _ = SetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled);

        ApplyResourceColor(handle, DwmwaCaptionColor, "AppSurfaceBrush");
        ApplyResourceColor(handle, DwmwaBorderColor, "AppBorderBrush");
        ApplyResourceColor(handle, DwmwaTextColor, "AppTextBrush");
        int cornerPreference = DwmwcpRound;
        _ = SetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference);
    }

    private void ApplyResourceColor(IntPtr handle, int attribute, string key)
    {
        if (Avalonia.Application.Current?.TryGetResource(key, ActualThemeVariant, out object? value) == true
            && value is ISolidColorBrush brush)
        {
            // Windows COLORREF is 0x00BBGGRR.
            Color color = brush.Color;
            int colorRef = color.R | color.G << 8 | color.B << 16;
            _ = SetWindowAttribute(handle, attribute, ref colorRef);
        }
    }

    private static int SetWindowAttribute(IntPtr handle, int attribute, ref int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf<int>());

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
