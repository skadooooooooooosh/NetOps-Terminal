using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.Application.Devices;

namespace NetOpsTerminal.App.Views;

public sealed record DeviceExportDialogResult(
    DeviceTransferFormat Format,
    bool SelectedOnly,
    bool IncludeFavorites,
    bool IncludeRecent);

public sealed partial class DeviceExportWindow : ThemedWindow
{
    public DeviceExportWindow()
    {
        InitializeComponent();
    }

    public DeviceExportWindow(bool hasSelection, string selectionLabel)
        : this()
    {
        ScopeComboBox.ItemsSource = hasSelection
            ? new[] { "All saved devices and groups", $"Selected: {selectionLabel}" }
            : new[] { "All saved devices and groups" };
        ScopeComboBox.SelectedIndex = 0;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(new DeviceExportDialogResult(
            FormatComboBox.SelectedIndex == 1 ? DeviceTransferFormat.Excel : DeviceTransferFormat.Json,
            ScopeComboBox.SelectedIndex == 1,
            FavoritesCheckBox.IsChecked == true,
            RecentCheckBox.IsChecked == true));
    }
}
