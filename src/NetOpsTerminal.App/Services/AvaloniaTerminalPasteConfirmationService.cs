using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.Services;

public sealed class AvaloniaTerminalPasteConfirmationService
    : ITerminalPasteConfirmationService
{
    public ValueTask<bool> ConfirmAsync(TerminalPasteConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ValueTask<bool>(ShowAsync(request));
    }

    private static Task<bool> ShowAsync(TerminalPasteConfirmationRequest request)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(
            async () =>
            {
                try
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    var dialog = new TerminalPasteConfirmationWindow(request);
                    bool confirmed = await dialog.ShowDialog<bool>(owner);
                    completion.TrySetResult(confirmed);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });

        return completion.Task;
    }
}
