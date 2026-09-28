using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.Security.Ssh;

namespace NetOpsTerminal.App.Services;

public sealed class AvaloniaSshHostKeyPrompt : ISshHostKeyPrompt
{
    public Task<SshHostKeyPromptDecision> PromptUnknownAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        CancellationToken cancellationToken)
    {
        return ShowAsync(endpoint, presentedKey, trustedKey: null, isChanged: false, cancellationToken);
    }

    public async Task ShowChangedAsync(
        SshHostEndpoint endpoint,
        SshHostKey? trustedKey,
        SshHostKey presentedKey,
        CancellationToken cancellationToken)
    {
        _ = await ShowAsync(endpoint, presentedKey, trustedKey, isChanged: true, cancellationToken);
    }

    private static Task<SshHostKeyPromptDecision> ShowAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        SshHostKey? trustedKey,
        bool isChanged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<SshHostKeyPromptDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        SshHostKeyPromptWindow? dialog = null;

        CancellationTokenRegistration registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(
                () => dialog?.Close(SshHostKeyPromptDecision.Cancel)));

        Dispatcher.UIThread.Post(
            async () =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                    {
                        throw new InvalidOperationException("The SSH host-key prompt requires an active main window.");
                    }

                    dialog = new SshHostKeyPromptWindow(
                        endpoint,
                        presentedKey,
                        trustedKey,
                        isChanged);
                    SshHostKeyPromptDecision decision = await dialog
                        .ShowDialog<SshHostKeyPromptDecision>(owner);
                    completion.TrySetResult(decision);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            });

        return completion.Task;
    }
}
