using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetOpsTerminal.App.Views;

public sealed partial class ConfirmationWindow : ThemedWindow
{
    public ConfirmationWindow()
    {
        InitializeComponent();
    }

    public ConfirmationWindow(string heading, string message, string confirmText, string cancelText = "Cancel")
        : this()
    {
        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        if (confirmText.StartsWith("Delete", StringComparison.OrdinalIgnoreCase))
        {
            Title = "Confirm deletion";
            TitleText.Text = Title;
            ConfirmButton.Classes.Remove("accent");
            ConfirmButton.Classes.Add("destructive");
        }
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm_OnClick(object? sender, RoutedEventArgs e) => Close(true);
}
