using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Core.Credentials;

namespace NetOpsTerminal.App.Views;

public sealed class CredentialEditorResult(
    CredentialProfileEditRequest request,
    string? password)
{
    public CredentialProfileEditRequest Request { get; } = request;

    public string? Password { get; } = password;

    public override string ToString() => $"{Request.Name} (password [REDACTED])";
}

public sealed partial class CredentialEditorWindow : ThemedWindow
{
    private readonly Guid? _profileId;

    public CredentialEditorWindow()
    {
        InitializeComponent();
    }

    public CredentialEditorWindow(CredentialProfile? profile)
        : this()
    {
        if (profile is null)
        {
            return;
        }

        _profileId = profile.Id;
        HeadingText.Text = "Edit credential profile";
        NameTextBox.Text = profile.Name;
        UsernameTextBox.Text = profile.Username;
        PasswordLabel.Text = "New password (optional)";
        PasswordHint.Text = "Leave blank to keep the existing protected password.";
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        PasswordTextBox.Text = string.Empty;
        base.OnClosing(e);
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        PasswordTextBox.Text = string.Empty;
        Close(null);
    }

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        ValidationErrorText.IsVisible = false;
        try
        {
            string name = NameTextBox.Text ?? string.Empty;
            string username = UsernameTextBox.Text ?? string.Empty;
            string password = PasswordTextBox.Text ?? string.Empty;
            _ = new CredentialProfile(
                _profileId ?? Guid.NewGuid(),
                name,
                username,
                "validation-reference",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            if (_profileId is null && password.Length == 0)
            {
                throw new ArgumentException("Enter a password for the new credential profile.");
            }

            var result = new CredentialEditorResult(
                new CredentialProfileEditRequest(_profileId, name, username),
                password.Length == 0 ? null : password);
            PasswordTextBox.Text = string.Empty;
            Close(result);
        }
        catch (ArgumentException exception)
        {
            ValidationErrorText.Text = exception.Message;
            ValidationErrorText.IsVisible = true;
        }
    }
}
