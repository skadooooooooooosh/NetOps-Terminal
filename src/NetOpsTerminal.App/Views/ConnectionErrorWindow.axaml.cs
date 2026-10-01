using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetOpsTerminal.App.Views;

public sealed partial class ConnectionErrorWindow : ThemedWindow
{
    public ConnectionErrorWindow()
    {
        InitializeComponent();
    }

    public ConnectionErrorWindow(string userMessage, string technicalDetails, string? heading = null)
        : this()
    {
        if (heading is not null)
        {
            Title = heading;
            TitleText.Text = heading;
            HeadingText.Text = heading;
        }

        UserMessageText.Text = userMessage;
        TechnicalDetailsText.Text = technicalDetails;
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
