using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using NetOpsTerminal.App.Services;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.Views;

public sealed partial class TerminalPasteConfirmationWindow : ThemedWindow
{
    public TerminalPasteConfirmationWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            FitToWorkingArea();
            Dispatcher.UIThread.Post(ResetPreviewPosition, DispatcherPriority.Loaded);
        };
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

    private void FitToWorkingArea()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen)
            return;

        // WorkingArea is in physical pixels; window dimensions use logical DIPs.
        const double margin = 16;
        double availableWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - margin * 2);
        double availableHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - margin * 2);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);

        int inset = (int)Math.Ceiling(margin * screen.Scaling);
        int right = screen.WorkingArea.Right - inset - (int)Math.Ceiling(Width * screen.Scaling);
        int bottom = screen.WorkingArea.Bottom - inset - (int)Math.Ceiling(Height * screen.Scaling);
        Position = new PixelPoint(
            Math.Clamp(Position.X, screen.WorkingArea.X + inset, Math.Max(screen.WorkingArea.X + inset, right)),
            Math.Clamp(Position.Y, screen.WorkingArea.Y + inset, Math.Max(screen.WorkingArea.Y + inset, bottom)));
    }
    private void ResetPreviewPosition()
    {
        PreviewTextBox.CaretIndex = 0;
        PreviewTextBox.SelectionStart = 0;
        PreviewTextBox.SelectionEnd = 0;
        if (PreviewTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
            scroll.Offset = Vector.Zero;
    }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Paste_OnClick(object? sender, RoutedEventArgs e) => Close(true);
}
