using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Credentials;

namespace NetOpsTerminal.App.Views;

public sealed record DeviceImportDialogResult(DeviceImportConflictPolicy ConflictPolicy, Guid? FallbackCredentialId);

public sealed partial class DeviceImportReviewWindow : ThemedWindow
{
    public DeviceImportReviewWindow()
    {
        InitializeComponent();
    }

    public DeviceImportReviewWindow(DeviceImportPreview preview, IReadOnlyList<CredentialProfile> credentials)
        : this()
    {
        SummaryText.Text = $"{preview.DeviceCount} devices · {preview.ConflictCount} already saved · {preview.CredentialRequiredCount} need credentials";
        CredentialComboBox.ItemsSource = credentials;
        DevicesList.ItemsSource = preview.Devices;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        DeviceImportConflictPolicy policy = UpdateRadio.IsChecked == true
            ? DeviceImportConflictPolicy.Update
            : SkipRadio.IsChecked == true
                ? DeviceImportConflictPolicy.Skip
                : DeviceImportConflictPolicy.KeepBoth;
        Guid? credentialId = (CredentialComboBox.SelectedItem as CredentialProfile)?.Id;
        Close(new DeviceImportDialogResult(policy, credentialId));
    }
}
