using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetOpsTerminal.App.Views;

public sealed partial class DeviceTransferResultWindow : ThemedWindow
{
    public DeviceTransferResultWindow()
    {
        InitializeComponent();
    }

    public DeviceTransferResultWindow(string heading, string message)
        : this()
    {
        HeadingText.Text = heading;
        MessageText.Text = message;
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
