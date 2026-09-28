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
        SummaryText.Text = $"{preview.DeviceCount} devices in {preview.GroupCount} groups - {preview.ConflictCount} conflicts - {preview.CredentialRequiredCount} need credentials";
        ConflictComboBox.ItemsSource = new[] { "Keep both as copies", "Skip imported duplicate", "Update matching device" };
        ConflictComboBox.SelectedIndex = 0;
        CredentialComboBox.ItemsSource = credentials;
        DevicesList.ItemsSource = preview.Devices;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        DeviceImportConflictPolicy policy = ConflictComboBox.SelectedIndex switch
        {
            1 => DeviceImportConflictPolicy.Skip,
            2 => DeviceImportConflictPolicy.Update,
            _ => DeviceImportConflictPolicy.KeepBoth,
        };
        Guid? credentialId = (CredentialComboBox.SelectedItem as CredentialProfile)?.Id;
        Close(new DeviceImportDialogResult(policy, credentialId));
    }
}
