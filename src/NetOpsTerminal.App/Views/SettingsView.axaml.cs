using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.Views;

public sealed partial class SettingsView : UserControl
{
    public event EventHandler? ImportDevicesRequested;
    public event EventHandler? ExportDevicesRequested;
    public event EventHandler? ExcelTemplateRequested;
    public event EventHandler? OpenLogsFolderRequested;

    public SettingsView()
    {
        InitializeComponent();
    }

    private void ImportDevices_OnClick(object? sender, RoutedEventArgs e)
        => ImportDevicesRequested?.Invoke(this, EventArgs.Empty);

    private void ExportDevices_OnClick(object? sender, RoutedEventArgs e)
        => ExportDevicesRequested?.Invoke(this, EventArgs.Empty);

    private void ExcelTemplate_OnClick(object? sender, RoutedEventArgs e)
        => ExcelTemplateRequested?.Invoke(this, EventArgs.Empty);

    private void OpenLogsFolder_OnClick(object? sender, RoutedEventArgs e)
        => OpenLogsFolderRequested?.Invoke(this, EventArgs.Empty);

    private async void DecreaseFont_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.TerminalFontSize > TerminalFontSizePolicy.Minimum)
        {
            await viewModel.ChangeTerminalFontSizeAsync(viewModel.TerminalFontSize - 1);
        }
    }

    private async void IncreaseFont_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.TerminalFontSize < TerminalFontSizePolicy.Maximum)
        {
            await viewModel.ChangeTerminalFontSizeAsync(viewModel.TerminalFontSize + 1);
        }
    }

    private async void ResetFont_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ChangeTerminalFontSizeAsync(TerminalFontSizePolicy.Default);
        }
    }
}
