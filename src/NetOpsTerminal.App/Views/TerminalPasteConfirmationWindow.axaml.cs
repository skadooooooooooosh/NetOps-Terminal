using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.App.Services;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.Views;

public sealed partial class TerminalPasteConfirmationWindow : ThemedWindow
{
    public TerminalPasteConfirmationWindow()
    {
        InitializeComponent();
    }

    public TerminalPasteConfirmationWindow(TerminalPasteConfirmationRequest request)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);
        HeadingText.Text = request.LineCount == 1
            ? "Paste 1 line?"
            : $"Paste {request.LineCount} lines?";
        RiskText.Text = request.ContainsControlSequences
            ? "This clipboard content also contains control characters. They are shown as hexadecimal markers in the preview."
            : "Only the text shown below will be sent. Cancel if the commands or target session are not correct.";
        PreviewTextBox.Text = TerminalPastePreviewFormatter.Format(request.Text);
    }

    public TerminalPasteConfirmationWindow(
        string heading,
        string riskMessage,
        string text,
        string actionLabel)
        : this()
    {
        HeadingText.Text = heading;
        RiskText.Text = riskMessage;
        PreviewTextBox.Text = TerminalPastePreviewFormatter.Format(text);
        PasteButton.Content = actionLabel;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Paste_OnClick(object? sender, RoutedEventArgs e) => Close(true);
}
