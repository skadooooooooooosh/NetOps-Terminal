using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Core.Credentials;

namespace NetOpsTerminal.App.Views;

public sealed partial class CredentialProfilesWindow : ThemedWindow
{
    private readonly ICredentialProfileService? _credentialService;

    public CredentialProfilesWindow()
    {
        InitializeComponent();
    }

    public CredentialProfilesWindow(ICredentialProfileService credentialService)
        : this()
    {
        _credentialService = credentialService;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async void Add_OnClick(object? sender, RoutedEventArgs e)
    {
        var editor = new CredentialEditorWindow(null);
        CredentialEditorResult? result = await editor.ShowDialog<CredentialEditorResult?>(this);
        if (result is not null)
        {
            await SaveAsync(result);
        }
    }

    private async void Edit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CredentialListBox.SelectedItem is not CredentialProfile profile)
        {
            ShowError("Select a credential profile to edit.");
            return;
        }

        var editor = new CredentialEditorWindow(profile);
        CredentialEditorResult? result = await editor.ShowDialog<CredentialEditorResult?>(this);
        if (result is not null)
        {
            await SaveAsync(result);
        }
    }

    private async void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CredentialListBox.SelectedItem is not CredentialProfile profile)
        {
            ShowError("Select a credential profile to delete.");
            return;
        }

        bool confirmed = await new ConfirmationWindow(
            "Delete credential profile?",
            $"Delete “{profile.Name}” from NetOps Terminal and Windows Credential Manager? This is blocked while a saved device references it.",
            "Delete credential").ShowDialog<bool>(this);
        if (!confirmed || _credentialService is null)
        {
            return;
        }

        try
        {
            await _credentialService.DeleteAsync(profile.Id, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async Task SaveAsync(CredentialEditorResult result)
    {
        if (_credentialService is null)
        {
            return;
        }

        try
        {
            await _credentialService.SaveAsync(
                result.Request,
                result.Password,
                CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async Task RefreshAsync()
    {
        if (_credentialService is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<CredentialProfile> profiles = await _credentialService
                .GetAllAsync(CancellationToken.None);
            CredentialListBox.ItemsSource = profiles;
            EmptyText.IsVisible = profiles.Count == 0;
            ErrorText.IsVisible = false;
        }
        catch (Exception exception)
        {
            ShowError($"Credential profiles could not be loaded ({exception.GetType().Name}).");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
