using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace NetOpsTerminal.App.Views;

/// <summary>
/// Applies the application's dark caption and rounded native corners to every
/// top-level window on Windows. Other platforms keep their native window chrome.
/// </summary>
public class ThemedWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwcpRound = 2;

    // COLORREF uses 0x00BBGGRR ordering. These match App.axaml semantic colors.
    private const int CaptionColor = 0x0025160D; // #0D1625
    private const int BorderColor = 0x00473221; // #213247
    private const int TextColor = 0x00F2E6DC; // #DCE6F2

    public ThemedWindow()
    {
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        Opened -= OnOpened;

        IntPtr? platformHandle = TryGetPlatformHandle()?.Handle;
        if (!OperatingSystem.IsWindows() || platformHandle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        int enabled = 1;
        if (SetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled) < 0)
        {
            _ = SetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled);
        }

        int captionColor = CaptionColor;
        int borderColor = BorderColor;
        int textColor = TextColor;
        int cornerPreference = DwmwcpRound;
        _ = SetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor);
        _ = SetWindowAttribute(handle, DwmwaBorderColor, ref borderColor);
        _ = SetWindowAttribute(handle, DwmwaTextColor, ref textColor);
        _ = SetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference);
    }

    private static int SetWindowAttribute(IntPtr handle, int attribute, ref int value)
    {
        return DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
