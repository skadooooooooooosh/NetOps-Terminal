using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Security.Ssh;

namespace NetOpsTerminal.App.Views;

public sealed partial class SshHostKeyPromptWindow : ThemedWindow
{
    public SshHostKeyPromptWindow()
    {
        InitializeComponent();
    }

    public SshHostKeyPromptWindow(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        SshHostKey? trustedKey,
        bool isChanged)
        : this()
    {
        DataContext = new SshHostKeyPromptViewModel(
            endpoint,
            presentedKey,
            trustedKey,
            isChanged);
    }

    private void TrustOnce_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(SshHostKeyPromptDecision.TrustOnce);
    }

    private void TrustAndSave_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(SshHostKeyPromptDecision.TrustAndSave);
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(SshHostKeyPromptDecision.Cancel);
    }
}
