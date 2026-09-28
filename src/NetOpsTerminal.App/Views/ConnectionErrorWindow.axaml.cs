using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetOpsTerminal.App.Views;

public sealed partial class ConnectionErrorWindow : ThemedWindow
{
    public ConnectionErrorWindow()
    {
        InitializeComponent();
    }

    public ConnectionErrorWindow(string userMessage, string technicalDetails)
        : this()
    {
        UserMessageText.Text = userMessage;
        TechnicalDetailsText.Text = technicalDetails;
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
