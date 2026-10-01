using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NetOpsTerminal.App.ViewModels;

namespace NetOpsTerminal.App.Views;

public sealed partial class WorkspacePaneView : UserControl
{
    public WorkspacePaneView()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? MainViewModel =>
        this.FindAncestorOfType<MainWindow>()?.DataContext as MainWindowViewModel;

    private void Pane_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalPaneViewModel pane })
        {
            MainViewModel?.ActivatePane(pane);
        }
    }

    private async void RestorePane_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalPaneViewModel pane } && MainViewModel is { } viewModel)
        {
            await viewModel.RestorePaneAsync(pane);
        }
    }

    private void CancelConnection_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalPaneViewModel pane })
            MainViewModel?.CancelPaneConnection(pane);
    }

    private async void RetryConnection_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: TerminalPaneViewModel pane }
            || MainViewModel is not { } viewModel)
            return;

        if (pane.Launch?.Kind == NetOpsTerminal.Application.Workspaces.WorkspaceLaunchKind.SavedDevice)
            await viewModel.RetrySavedPaneAsync(pane);
        else if (this.FindAncestorOfType<MainWindow>() is { } window)
            await window.RetryQuickConnectAsync(pane);
    }

    private void FindNext_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalPaneViewModel { Terminal: { } terminal } })
        {
            terminal.FindNext();
        }
    }

    private void FindPrevious_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalPaneViewModel { Terminal: { } terminal } })
        {
            terminal.FindPrevious();
        }
    }

    private void CloseFind_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalPaneViewModel { Terminal: { } terminal } })
        {
            terminal.CloseFind();
        }
    }

    private void FindText_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: TerminalPaneViewModel { Terminal: { } terminal } })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                terminal.FindPrevious();
            }
            else
            {
                terminal.FindNext();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            terminal.CloseFind();
            e.Handled = true;
        }
    }

    private async void Splitter_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not GridSplitter { DataContext: SplitPaneViewModel split, Parent: Grid grid } splitter
            || MainViewModel is not { } viewModel)
        {
            return;
        }

        double ratio;
        if (splitter.Tag?.ToString() == "Right")
        {
            double available = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
            ratio = available <= 0 ? split.Ratio : grid.ColumnDefinitions[0].ActualWidth / available;
        }
        else
        {
            double available = grid.RowDefinitions[0].ActualHeight + grid.RowDefinitions[2].ActualHeight;
            ratio = available <= 0 ? split.Ratio : grid.RowDefinitions[0].ActualHeight / available;
        }

        await viewModel.UpdateSplitRatioAsync(split, ratio);
    }
}
