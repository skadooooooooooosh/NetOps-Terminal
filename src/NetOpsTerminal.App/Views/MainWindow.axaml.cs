using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;
using NetOpsTerminal.App.Input;
using NetOpsTerminal.App.Services;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Terminal.Abstractions;
using NetOpsTerminal.Terminal.RoyalTerminal;
using NetOpsTerminal.Application.Workspaces;

namespace NetOpsTerminal.App.Views;

public sealed partial class MainWindow : ThemedWindow, IDisposable
{
    private sealed record ExplorerDragPayload(IReadOnlyList<DeviceExplorerNodeViewModel> Nodes);

    private static readonly DataFormat<ExplorerDragPayload> ExplorerNodeFormat =
        DataFormat.CreateInProcessFormat<ExplorerDragPayload>("netops-explorer-nodes");
    private ExplorerDragPayload? _dragCandidate;
    private DeviceExplorerNodeViewModel? _deviceDropTarget;
    private static readonly DataFormat<WorkspaceTabViewModel> WorkspaceTabFormat =
        DataFormat.CreateInProcessFormat<WorkspaceTabViewModel>("netops-workspace-tab");
    private WorkspaceTabViewModel? _tabDragCandidate;
    private PointerPressedEventArgs? _tabDragPressEvent;
    private Point _tabDragStart;
    private bool _tabDragInProgress;
    private Point _dragStart;
    private PointerPressedEventArgs? _dragPressEvent;
    private bool _dragInProgress;
    private bool _isResizingSidebar;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;
    private bool _shutdownInProgress;
    private bool _shutdownComplete;
    private readonly ICredentialProfileService? _credentialProfileService;
    private readonly ISerialPortCatalog? _serialPortCatalog;
    private readonly IDeviceTransferService? _deviceTransferService;
    private readonly ISessionLogsFolderOpener? _sessionLogsFolderOpener;
    private readonly IDeviceWebAccessService? _deviceWebAccessService;
    private CancellationTokenSource? _deviceSearchCancellation;

    public MainWindow()
    {
        InitializeComponent();
        DeviceTree.AddHandler(
            InputElement.PointerPressedEvent, DeviceTree_OnPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        DeviceTree.AddHandler(
            InputElement.PointerMovedEvent, DeviceTree_OnPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        DeviceTree.AddHandler(
            InputElement.PointerReleasedEvent, DeviceTree_OnPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SettingsPanel.ImportDevicesRequested += ImportDevices_OnRequested;
        SettingsPanel.ExportDevicesRequested += ExportDevices_OnRequested;
        SettingsPanel.ExcelTemplateRequested += ExcelTemplate_OnRequested;
        SettingsPanel.OpenLogsFolderRequested += OpenLogsFolder_OnRequested;
        TerminalTabsControl.SelectionChanged += TerminalTabs_OnSelectionChanged;
        TerminalTabsControl.AddHandler(
            InputElement.PointerPressedEvent, TerminalTabs_OnPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        TerminalTabsControl.AddHandler(
            InputElement.PointerMovedEvent, TerminalTabs_OnPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        TerminalTabsControl.AddHandler(
            InputElement.PointerReleasedEvent, TerminalTabs_OnPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Activated += MainWindow_OnActivated;
        AddHandler(
            InputElement.KeyDownEvent,
            MainWindow_OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.KeyUpEvent,
            SuppressPaneNavigationKeyUp,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerWheelChangedEvent,
            TerminalHost_OnPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this(viewModel, null, null)
    {
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        ICredentialProfileService? credentialProfileService)
        : this(viewModel, credentialProfileService, null)
    {
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        ICredentialProfileService? credentialProfileService,
        ISerialPortCatalog? serialPortCatalog,
        IDeviceTransferService? deviceTransferService = null,
        ISessionLogsFolderOpener? sessionLogsFolderOpener = null,
        IDeviceWebAccessService? deviceWebAccessService = null)
        : this()
    {
        DataContext = viewModel;
        _credentialProfileService = credentialProfileService;
        _serialPortCatalog = serialPortCatalog;
        _deviceTransferService = deviceTransferService;
        _sessionLogsFolderOpener = sessionLogsFolderOpener;
        _deviceWebAccessService = deviceWebAccessService;
        Opened += MainWindow_OnOpened;
    }

    public void Dispose()
    {
        _deviceSearchCancellation?.Cancel();
        _deviceSearchCancellation?.Dispose();
        _deviceSearchCancellation = null;
    }

    private async void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void MainWindow_OnActivated(object? sender, EventArgs e)
    {
        RequestSelectedTerminalFocus();
    }

    private void TerminalTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RequestSelectedTerminalFocus();
    }

    private void TerminalTabs_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _tabDragCandidate = null;
        _tabDragPressEvent = null;
        if (DataContext is not MainWindowViewModel { WorkspaceTabs.Count: > 1 }
            || !e.GetCurrentPoint(TerminalTabsControl).Properties.IsLeftButtonPressed
            || IsWithinTabButton(e.Source)
            || FindTabItem(e.Source)?.DataContext is not WorkspaceTabViewModel tab)
            return;

        _tabDragCandidate = tab;
        _tabDragPressEvent = e;
        _tabDragStart = e.GetPosition(TerminalTabsControl);
    }

    private async void TerminalTabs_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDragInProgress
            || _tabDragCandidate is not { } tab
            || _tabDragPressEvent is not { } pressEvent
            || !e.GetCurrentPoint(TerminalTabsControl).Properties.IsLeftButtonPressed
            || Math.Abs(e.GetPosition(TerminalTabsControl).X - _tabDragStart.X) < 6
                && Math.Abs(e.GetPosition(TerminalTabsControl).Y - _tabDragStart.Y) < 6)
            return;

        _tabDragCandidate = null;
        _tabDragPressEvent = null;
        _tabDragInProgress = true;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(WorkspaceTabFormat, tab));
            await DragDrop.DoDragDropAsync(pressEvent, transfer, DragDropEffects.Move);
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The tab could not be dragged.", exception.Message).ShowDialog(this);
        }
        finally
        {
            ClearTabDropIndicators();
            _tabDragInProgress = false;
        }
    }

    private void TerminalTabs_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _tabDragCandidate = null;
        _tabDragPressEvent = null;
    }

    private void TerminalTab_OnDragOver(object? sender, DragEventArgs e)
    {
        WorkspaceTabViewModel? source = e.DataTransfer.TryGetValue(WorkspaceTabFormat);
        TabItem? item = FindTabDropTarget(e);
        WorkspaceTabViewModel? target = item?.DataContext as WorkspaceTabViewModel;
        bool insertAfter = item is not null
            && e.GetPosition(item).X >= item.Bounds.Width / 2;
        if (source is not null && target is not null
            && DataContext is MainWindowViewModel viewModel
            && viewModel.CanMoveWorkspaceTab(source, target, insertAfter))
        {
            ShowTabDropIndicator(target, insertAfter);
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            ClearTabDropIndicators();
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void TerminalTab_OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearTabDropIndicators();
    }

    private async void TerminalTab_OnDrop(object? sender, DragEventArgs e)
    {
        WorkspaceTabViewModel? source = e.DataTransfer.TryGetValue(WorkspaceTabFormat);
        TabItem? item = FindTabDropTarget(e);
        WorkspaceTabViewModel? target = item?.DataContext as WorkspaceTabViewModel;
        bool insertAfter = item is not null
            && e.GetPosition(item).X >= item.Bounds.Width / 2;
        ClearTabDropIndicators();
        if (source is null || target is null
            || DataContext is not MainWindowViewModel viewModel
            || !viewModel.CanMoveWorkspaceTab(source, target, insertAfter))
            return;

        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;
        try
        {
            await viewModel.MoveWorkspaceTabAsync(source, target, insertAfter);
            TerminalTabsControl.SelectedItem = source;
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The tab could not be moved.", exception.Message).ShowDialog(this);
        }
    }

    private TabItem? FindTabDropTarget(DragEventArgs e)
        => FindTabItem(e.Source)
            ?? FindTabItem(TerminalTabsControl.InputHitTest(
                e.GetPosition(TerminalTabsControl)));

    private static TabItem? FindTabItem(object? source)
    {
        Visual? visual = source as Visual;
        return visual as TabItem
            ?? visual?.GetVisualAncestors().OfType<TabItem>().FirstOrDefault();
    }

    private static bool IsWithinTabButton(object? source)
    {
        Visual? visual = source as Visual;
        return visual is Button
            || visual?.GetVisualAncestors()
                .TakeWhile(ancestor => ancestor is not TabItem)
                .OfType<Button>().Any() == true;
    }

    private void ShowTabDropIndicator(WorkspaceTabViewModel target, bool insertAfter)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        foreach (WorkspaceTabViewModel tab in viewModel.WorkspaceTabs)
        {
            tab.IsDropBefore = ReferenceEquals(tab, target) && !insertAfter;
            tab.IsDropAfter = ReferenceEquals(tab, target) && insertAfter;
        }
    }

    private void ClearTabDropIndicators()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        foreach (WorkspaceTabViewModel tab in viewModel.WorkspaceTabs)
        {
            tab.IsDropBefore = false;
            tab.IsDropAfter = false;
        }
    }

    private void RequestSelectedTerminalFocus()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is not MainWindowViewModel { SelectedTerminal: { } selected })
                {
                    return;
                }

                RoyalTerminalHost? terminalHost = TerminalTabsControl
                    .GetVisualDescendants()
                    .OfType<RoyalTerminalHost>()
                    .FirstOrDefault(
                        candidate => candidate.IsEffectivelyVisible
                            && ReferenceEquals(candidate.Session, selected.Session));
                terminalHost?.RequestTerminalFocus();
            },
            DispatcherPriority.Loaded);
    }

    private void MinimizeWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleMaximizeWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void QuickConnect_OnClick(object? sender, RoutedEventArgs e)
    {
        await OpenQuickConnectAsync();
    }

    private async Task OpenQuickConnectAsync(TerminalPaneViewModel? targetPane = null)
    {
        var dialog = new QuickConnectWindow(_serialPortCatalog);
        TerminalSessionRequest? request = await dialog.ShowDialog<TerminalSessionRequest?>(this);
        if (request is not null && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.OpenRemoteAsync(
                request, dialog.BackspaceSendsControlH, dialog.BackspaceSendsDelete, targetPane);
        }
    }

    internal Task RetryQuickConnectAsync(TerminalPaneViewModel pane) =>
        OpenQuickConnectAsync(pane);

    private async void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (e.Key == Key.R && alt && !control && !shift
            && DataContext is MainWindowViewModel { SelectedTerminal: { } activeTerminal })
        {
            e.Handled = true;
            if (activeTerminal.ReconnectCommand.CanExecute(null))
            {
                await activeTerminal.ReconnectCommand.ExecuteAsync(null);
            }
        }
        else if (e.Key == Key.P && alt && !control && !shift
            && DataContext is MainWindowViewModel powerShellViewModel)
        {
            e.Handled = true;
            await powerShellViewModel.NewPowerShellCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.C && alt && !control && !shift
            && DataContext is MainWindowViewModel cmdViewModel)
        {
            e.Handled = true;
            await cmdViewModel.NewCommandPromptCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.N && control && !shift && !alt)
        {
            e.Handled = true;
            await OpenQuickConnectAsync();
        }
        else if (e.Key == Key.W && control && !shift && !alt
            && DataContext is MainWindowViewModel closeViewModel)
        {
            e.Handled = true;
            await closeViewModel.CloseSelectedTerminalAsync();
        }
        else if (e.Key == Key.Tab && control && !alt
            && DataContext is MainWindowViewModel tabViewModel)
        {
            tabViewModel.SelectNextTerminal(reverse: shift);
            e.Handled = true;
        }
        else if (e.Key == Key.L && alt && !control && !shift
            && DataContext is MainWindowViewModel inputLockViewModel)
        {
            inputLockViewModel.ToggleActivePaneInputLock();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && alt && !control && !shift
            && DataContext is MainWindowViewModel { SelectedWorkspace.ActivePane: { } splitRightPane } splitRightViewModel)
        {
            e.Handled = true;
            await splitRightViewModel.SplitPaneAsync(splitRightPane, PaneSplitDirection.Right);
        }
        else if (e.Key == Key.Down && alt && !control && !shift
            && DataContext is MainWindowViewModel { SelectedWorkspace.ActivePane: { } splitDownPane } splitDownViewModel)
        {
            e.Handled = true;
            await splitDownViewModel.SplitPaneAsync(splitDownPane, PaneSplitDirection.Down);
        }
        else if (e.Key == Key.G && alt && !control && !shift
            && DataContext is MainWindowViewModel { SelectedTerminal: { } loggingTerminal })
        {
            e.Handled = true;
            await loggingTerminal.ToggleLoggingCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.X && alt && !control && !shift
            && DataContext is MainWindowViewModel { SelectedTerminal: { } disconnectTerminal })
        {
            e.Handled = true;
            await disconnectTerminal.DisconnectCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.OemQuestion && control && !alt && !shift)
        {
            e.Handled = true;
            KeyboardShortcutsButton.Flyout?.ShowAt(KeyboardShortcutsButton);
        }
        else if (e.Key == Key.F && control && !alt && !shift
            && DataContext is MainWindowViewModel { SelectedTerminal: { } findTerminal })
        {
            findTerminal.OpenFind();
            RequestSelectedFindFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.F6 && !control && !alt
            && DataContext is MainWindowViewModel paneFocusViewModel)
        {
            e.Handled = true;
            paneFocusViewModel.FocusNextPane(reverse: shift);
            RequestSelectedTerminalFocus();
        }
        else if (control && !alt
            && e.Key is Key.Add or Key.OemPlus
            && DataContext is MainWindowViewModel zoomInViewModel)
        {
            e.Handled = true;
            await zoomInViewModel.ChangeTerminalFontSizeAsync(
                (zoomInViewModel.SelectedTerminal?.FontSize
                    ?? zoomInViewModel.TerminalFontSize) + 1);
        }
        else if (control && !alt
            && e.Key is Key.Subtract or Key.OemMinus
            && DataContext is MainWindowViewModel zoomOutViewModel)
        {
            e.Handled = true;
            await zoomOutViewModel.ChangeTerminalFontSizeAsync(
                (zoomOutViewModel.SelectedTerminal?.FontSize
                    ?? zoomOutViewModel.TerminalFontSize) - 1);
        }
        else if (control && !alt
            && e.Key is Key.D0 or Key.NumPad0
            && DataContext is MainWindowViewModel resetZoomViewModel)
        {
            e.Handled = true;
            await resetZoomViewModel.ChangeTerminalFontSizeAsync(
                TerminalFontSizePolicy.Default);
        }
        else if (e.Key == Key.Escape && KeyboardShortcutsButton.Flyout?.IsOpen == true)
        {
            KeyboardShortcutsButton.Flyout.Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape
            && DataContext is MainWindowViewModel { IsSidebarExpanded: true } explorerViewModel)
        {
            explorerViewModel.CloseDeviceExplorer();
            RequestSelectedTerminalFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape
            && DataContext is MainWindowViewModel { IsSettingsVisible: true } viewModel)
        {
            viewModel.ShowWorkspaceCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Workspace_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { IsSidebarExpanded: true } viewModel)
        {
            viewModel.CloseDeviceExplorerUnlessPinned();
        }
    }

    private async void RefreshDevices_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.DeviceExplorer.RefreshAsync();
        }
    }

    private async void ImportDevices_OnClick(object? sender, RoutedEventArgs e) => await ImportDevicesAsync();

    private async void ExportDevices_OnClick(object? sender, RoutedEventArgs e) => await ExportDevicesAsync();

    private async void ImportDevices_OnRequested(object? sender, EventArgs e) => await ImportDevicesAsync();

    private async void ExportDevices_OnRequested(object? sender, EventArgs e) => await ExportDevicesAsync();

    private async void ExcelTemplate_OnClick(object? sender, RoutedEventArgs e) => await SaveExcelTemplateAsync();

    private async void ExcelTemplate_OnRequested(object? sender, EventArgs e) => await SaveExcelTemplateAsync();

    private async void OpenLogsFolder_OnRequested(object? sender, EventArgs e)
    {
        if (_sessionLogsFolderOpener is null)
        {
            return;
        }

        try
        {
            _sessionLogsFolderOpener.Open();
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The session logs folder could not be opened.",
                exception.Message).ShowDialog(this);
        }
    }

    private async Task ImportDevicesAsync()
    {
        if (_deviceTransferService is null || DataContext is not MainWindowViewModel viewModel) return;
        string stage = "reading the file";
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import devices",
                AllowMultiple = false,
                FileTypeFilter = [DeviceTransferFileType(), ExcelDeviceFileType(), CsvDeviceFileType()],
            });
            if (files.Count == 0) return;
            await using Stream stream = await files[0].OpenReadAsync();
            if (stream.CanSeek && stream.Length > 5 * 1024 * 1024)
                throw new InvalidDataException("Device import files are limited to 5 MB.");
            DeviceTransferFormat format = Path.GetExtension(files[0].Name).ToUpperInvariant() switch
            {
                ".XLSX" => DeviceTransferFormat.Excel,
                ".CSV" => DeviceTransferFormat.Csv,
                _ => DeviceTransferFormat.Json,
            };
            string json = await _deviceTransferService.ConvertToJsonAsync(stream, format, CancellationToken.None);
            stage = "reviewing devices";
            DeviceImportPreview preview = await _deviceTransferService.ReviewImportAsync(json, CancellationToken.None);
            var dialog = new DeviceImportReviewWindow(preview, viewModel.DeviceExplorer.Credentials);
            DeviceImportDialogResult? choice = await dialog.ShowDialog<DeviceImportDialogResult?>(this);
            if (choice is null) return;
            stage = "saving devices";
            DeviceImportResult result = await _deviceTransferService.ImportAsync(
                json, choice.ConflictPolicy, choice.FallbackCredentialId, CancellationToken.None);
            stage = "refreshing Device Explorer";
            await viewModel.DeviceExplorer.RefreshAsync();
            stage = "showing the result";
            await ShowTransferResultAsync("Import complete",
                $"{result.ImportedCount} added, {result.UpdatedCount} updated, {result.SkippedCount} skipped, {result.CredentialRequiredCount} require credentials.");
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow($"Device import failed while {stage}.", $"{exception.GetType().Name}: {exception.Message}", "Device import failed").ShowDialog(this);
        }
    }

    private async Task ExportDevicesAsync()
    {
        if (_deviceTransferService is null || DataContext is not MainWindowViewModel viewModel) return;
        try
        {
            IReadOnlyList<Guid> selectedIds = viewModel.DeviceExplorer.GetSelectedDeviceIds();
            string label = viewModel.DeviceExplorer.SelectedNode?.Label ?? "current selection";
            var dialog = new DeviceExportWindow(selectedIds.Count > 0, label);
            DeviceExportDialogResult? choice = await dialog.ShowDialog<DeviceExportDialogResult?>(this);
            if (choice is null) return;
            IReadOnlyCollection<Guid>? ids = choice.SelectedOnly ? selectedIds : null;
            var exportOptions = new DeviceExportOptions(
                ids, choice.IncludeFavorites, choice.IncludeRecent);
            bool excel = choice.Format == DeviceTransferFormat.Excel;
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export devices",
                SuggestedFileName = excel
                    ? "workspace.netops-devices.xlsx"
                    : "workspace.netops-devices.json",
                DefaultExtension = excel ? "xlsx" : "json",
                FileTypeChoices = excel
                    ? [ExcelDeviceFileType()]
                    : [DeviceTransferFileType()],
            });
            if (file is null) return;
            await using Stream stream = await file.OpenWriteAsync();
            if (excel)
            {
                await _deviceTransferService.WriteExcelExportAsync(
                    exportOptions, stream, CancellationToken.None);
            }
            else
            {
                string json = await _deviceTransferService.ExportAsync(
                    exportOptions, CancellationToken.None);
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                    stream.SetLength(0);
                }

                await using var writer = new StreamWriter(
                    stream, new System.Text.UTF8Encoding(false));
                await writer.WriteAsync(json);
            }

            await ShowTransferResultAsync("Export complete",
                "Device metadata was exported without passwords, keys, secrets, or trusted host keys.");
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow("Devices could not be exported.", exception.Message).ShowDialog(this);
        }
    }

    private async Task SaveExcelTemplateAsync()
    {
        if (_deviceTransferService is null) return;
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Excel device template",
                SuggestedFileName = "NetOps-Device-Import-Template.xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices = [ExcelDeviceFileType()],
            });
            if (file is null) return;
            await using Stream stream = await file.OpenWriteAsync();
            await _deviceTransferService.WriteExcelTemplateAsync(stream, CancellationToken.None);
            await ShowTransferResultAsync("Excel template saved",
                "Enter devices on the Devices sheet, save the workbook, then choose Import devices.");
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow("The Excel template could not be saved.", exception.Message).ShowDialog(this);
        }
    }

    private static FilePickerFileType DeviceTransferFileType() => new("NetOps Terminal devices")
    {
        Patterns = ["*.netops-devices.json", "*.json"],
        MimeTypes = ["application/json"],
    };

    private static FilePickerFileType ExcelDeviceFileType() => new("Excel workbook")
    {
        Patterns = ["*.xlsx"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
    };

    private static FilePickerFileType CsvDeviceFileType() => new("Comma-separated values")
    {
        Patterns = ["*.csv"],
        MimeTypes = ["text/csv"],
    };

    private Task ShowTransferResultAsync(string heading, string message)
        => new DeviceTransferResultWindow(heading, message).ShowDialog(this);

    private void OpenFavorites_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DeviceExplorer.ShowFavoritesCommand.Execute(null);
            if (viewModel.IsSidebarCollapsed)
            {
                viewModel.ToggleSidebarCommand.Execute(null);
            }
        }
    }

    private void OpenRecent_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DeviceExplorer.ShowRecentCommand.Execute(null);
            if (viewModel.IsSidebarCollapsed)
            {
                viewModel.ToggleSidebarCommand.Execute(null);
            }
        }
    }

    private static void SuppressPaneNavigationKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
        }
    }

    private void RequestSelectedFindFocus()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is not MainWindowViewModel { SelectedTerminal: { } selected })
                {
                    return;
                }

                TextBox? findBox = TerminalTabsControl.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(candidate =>
                        candidate.Tag?.ToString() == "TerminalFind"
                        && candidate.IsEffectivelyVisible
                        && candidate.DataContext is TerminalPaneViewModel { Terminal: { } terminal }
                        && ReferenceEquals(terminal, selected));
                if (findBox is not null)
                {
                    _ = findBox.Focus();
                    findBox.SelectAll();
                }
            },
            DispatcherPriority.Loaded);
    }

    private async void TerminalHost_OnPointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        int increment = TerminalZoomGesture.GetIncrement(
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.Delta.Y);

        if (increment == 0
            || e.Source is not Visual source
            || source.FindAncestorOfType<RoyalTerminalHost>(includeSelf: true)
                is not { Session: { } session }
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        TerminalTabViewModel? terminal = viewModel.TerminalTabs
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Session, session));
        if (terminal is null)
        {
            return;
        }

        viewModel.SelectedTerminal = terminal;
        await viewModel.ChangeTerminalFontSizeAsync(terminal.FontSize + increment);
    }

    private async void WorkspaceTabClose_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: WorkspaceTabViewModel workspace }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedWorkspace = workspace;
            await viewModel.ClosePaneAsync(workspace.ActivePane);
        }
    }

    private async void ConnectionDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel
            {
                ConnectionErrorMessage: { } message,
                ConnectionErrorTechnicalDetails: { } details,
            })
        {
            return;
        }

        await new ConnectionErrorWindow(message, details).ShowDialog(this);
    }

    private async void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        _deviceSearchCancellation?.Cancel();
        _deviceSearchCancellation?.Dispose();
        _deviceSearchCancellation = null;
        try
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.ShutdownAsync();
            }
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    private async void DeviceSearch_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox searchBox
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _deviceSearchCancellation?.Cancel();
        _deviceSearchCancellation?.Dispose();
        _deviceSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, _deviceSearchCancellation.Token);
            await viewModel.DeviceExplorer.RefreshAsync(
                searchBox.Text,
                _deviceSearchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void AddDevice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        SavedDeviceOnboardingStep nextStep = SavedDeviceOnboardingPolicy.DetermineNextStep(
            viewModel.DeviceExplorer.Credentials.Count);
        if (nextStep == SavedDeviceOnboardingStep.CreateCredential)
        {
            bool createCredential = await new ConfirmationWindow(
                "SSH credential profile",
                "SSH saved devices need a Windows-protected credential profile. Create one now, or continue to add a web, Telnet, Serial, or Raw TCP device.",
                "Create credential",
                "Continue without credential").ShowDialog<bool>(this);
            if (createCredential && !await CreateCredentialForDeviceAsync(viewModel))
                return;
        }

        var editor = new DeviceEditorWindow(null, viewModel.DeviceExplorer.Credentials,
            _serialPortCatalog, _deviceWebAccessService);
        DeviceEditRequest? request = await editor.ShowDialog<DeviceEditRequest?>(this);
        if (request is not null)
        {
            await SaveDeviceAsync(viewModel, request);
        }
    }

    private async void EditDevice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !viewModel.DeviceExplorer.HasSelectedDevice
            || viewModel.DeviceExplorer.SelectedDevice is not { } device)
        {
            return;
        }

        DeviceEditRequest existing = viewModel.DeviceExplorer.CreateEditRequest(device);
        var editor = new DeviceEditorWindow(existing, viewModel.DeviceExplorer.Credentials,
            _serialPortCatalog, _deviceWebAccessService);
        DeviceEditRequest? request = await editor.ShowDialog<DeviceEditRequest?>(this);
        if (request is not null)
        {
            await SaveDeviceAsync(viewModel, request);
        }
    }

    private async void CreateSite_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.DeviceExplorer.CanOrganize)
            return;
        string? name = await new ExplorerNameWindow(true, string.Empty).ShowDialog<string?>(this);
        if (name is null)
            return;
        try
        {
            await viewModel.DeviceExplorer.CreateSiteAsync(name);
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The site could not be created.", exception.Message).ShowDialog(this);
        }
    }

    private async void CreateFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.DeviceExplorer.CanOrganize)
            return;
        DeviceExplorerNodeViewModel? parent = viewModel.DeviceExplorer.SelectedNode;
        if (parent?.SiteId is null)
        {
            await new ConnectionErrorWindow(
                "Select a site first.",
                "Choose a site or a device within that site to create an Organization folder.")
                .ShowDialog(this);
            return;
        }
        string location = viewModel.DeviceExplorer.GetFolderLocation(parent);
        string? name = await new ExplorerNameWindow(false, location).ShowDialog<string?>(this);
        if (name is null)
            return;
        try
        {
            await viewModel.DeviceExplorer.CreateFolderAsync(name, parent);
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The folder could not be created.", exception.Message).ShowDialog(this);
        }
    }

    private async void SortExplorerAlphabetically_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !viewModel.DeviceExplorer.CanSortSelectedAlphabetically)
            return;
        try
        {
            await viewModel.DeviceExplorer.SortSelectedAlphabeticallyAsync();
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The device order could not be reset.", exception.Message).ShowDialog(this);
        }
    }

    private async void DeleteEmptyFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || viewModel.DeviceExplorer.SelectedNode is not { } node
            || node.Kind == DeviceExplorerNodeKind.Device
            || node.Kind == DeviceExplorerNodeKind.Site && node.SiteId is null)
            return;
        string kind = node.Kind == DeviceExplorerNodeKind.Site ? "site" : "folder";
        bool confirmed = await new ConfirmationWindow(
            $"Delete empty {kind}?",
            $"Delete \"{node.Label}\"? Move devices out of this site or folder first.",
            $"Delete {kind}").ShowDialog<bool>(this);
        if (!confirmed)
            return;
        try
        {
            await viewModel.DeviceExplorer.DeleteEmptyNodeAsync(node);
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                $"The {kind} could not be deleted.", exception.Message).ShowDialog(this);
        }
    }

    private void DeviceTree_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCandidate = null;
        _dragPressEvent = null;
        TreeViewItem? item = FindDeviceTreeItem(e.Source);
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        if (item?.DataContext is not DeviceExplorerNodeViewModel node)
        {
            if (e.GetCurrentPoint(DeviceTree).Properties.IsRightButtonPressed)
            {
                DeviceTree.SelectedItems.Clear();
                DeviceTree.SelectedItem = null;
                viewModel.DeviceExplorer.SetSelectedNodes([]);
                viewModel.DeviceExplorer.SelectedNode = null;
            }
            return;
        }
        if (e.GetCurrentPoint(DeviceTree).Properties.IsRightButtonPressed)
        {
            DeviceTree.SelectedItem = node;
            viewModel.DeviceExplorer.SelectedNode = node;
            viewModel.DeviceExplorer.SetSelectedNodes([node]);
            return;
        }
        if (!viewModel.DeviceExplorer.CanOrganize
            || !e.GetCurrentPoint(DeviceTree).Properties.IsLeftButtonPressed)
            return;
        IReadOnlyList<DeviceExplorerNodeViewModel> selected =
            DeviceTree.SelectedItems.OfType<DeviceExplorerNodeViewModel>().ToArray();
        _dragCandidate = new ExplorerDragPayload(
            viewModel.DeviceExplorer.GetDragSources(node, selected));
        _dragPressEvent = e;
        _dragStart = e.GetPosition(DeviceTree);
    }

    private async void DeviceTree_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragInProgress || _dragCandidate is not { } payload || _dragPressEvent is not { } pressEvent
            || !e.GetCurrentPoint(DeviceTree).Properties.IsLeftButtonPressed
            || Math.Abs(e.GetPosition(DeviceTree).X - _dragStart.X) < 6
                && Math.Abs(e.GetPosition(DeviceTree).Y - _dragStart.Y) < 6)
            return;
        _dragCandidate = null;
        _dragPressEvent = null;
        _dragInProgress = true;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ExplorerNodeFormat, payload));
            await DragDrop.DoDragDropAsync(pressEvent, transfer, DragDropEffects.Move);
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The drag could not start.", exception.Message).ShowDialog(this);
        }
        finally
        {
            ClearDeviceDropIndicator();
            _dragInProgress = false;
        }
    }

    private void DeviceTree_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragCandidate = null;
        _dragPressEvent = null;
    }

    private void DeviceNode_OnDragOver(object? sender, DragEventArgs e)
    {
        ExplorerDragPayload? payload = e.DataTransfer.TryGetValue(ExplorerNodeFormat);
        TreeViewItem? item = FindDropTarget(e);
        DeviceExplorerNodeViewModel? target = item?.DataContext as DeviceExplorerNodeViewModel;
        if (payload is not null && target is not null && item is not null
            && DataContext is MainWindowViewModel viewModel
            && viewModel.DeviceExplorer.CanDrop(payload.Nodes, target))
        {
            bool intoFolder = target.Kind != DeviceExplorerNodeKind.Device
                && payload.Nodes.All(source => source.Kind == DeviceExplorerNodeKind.Device);
            bool insertAfter = e.GetPosition(item).Y >= item.Bounds.Height / 2;
            ShowDeviceDropIndicator(target, intoFolder, insertAfter);
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            ClearDeviceDropIndicator();
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DeviceNode_OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDeviceDropIndicator();
    }

    private void ShowDeviceDropIndicator(
        DeviceExplorerNodeViewModel target, bool intoFolder, bool insertAfter)
    {
        if (!ReferenceEquals(_deviceDropTarget, target))
            ClearDeviceDropIndicator();
        _deviceDropTarget = target;
        target.IsDropInto = intoFolder;
        target.IsDropBefore = !intoFolder && !insertAfter;
        target.IsDropAfter = !intoFolder && insertAfter;
    }

    private void ClearDeviceDropIndicator()
    {
        if (_deviceDropTarget is not { } target)
            return;
        target.IsDropBefore = false;
        target.IsDropAfter = false;
        target.IsDropInto = false;
        _deviceDropTarget = null;
    }

    private async void DeviceNode_OnDrop(object? sender, DragEventArgs e)
    {
        ExplorerDragPayload? payload = e.DataTransfer.TryGetValue(ExplorerNodeFormat);
        TreeViewItem? item = FindDropTarget(e);
        ClearDeviceDropIndicator();
        if (item?.DataContext is not DeviceExplorerNodeViewModel target
            || payload is null
            || DataContext is not MainWindowViewModel viewModel
            || !viewModel.DeviceExplorer.CanDrop(payload.Nodes, target))
            return;
        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;
        bool insertAfter = e.GetPosition(item).Y >= item.Bounds.Height / 2;
        try
        {
            await viewModel.DeviceExplorer.MoveOrReorderAsync(payload.Nodes, target, insertAfter);
        }
        catch (Exception exception)
        {
            await viewModel.DeviceExplorer.RefreshAsync();
            await new ConnectionErrorWindow(
                "The device or folder could not be moved.", exception.Message).ShowDialog(this);
        }
    }

    private TreeViewItem? FindDropTarget(DragEventArgs e)
    {
        TreeViewItem? item = FindDeviceTreeItem(e.Source);
        if (item is not null)
            return item;
        return FindDeviceTreeItem(DeviceTree.InputHitTest(e.GetPosition(DeviceTree)));
    }

    private static TreeViewItem? FindDeviceTreeItem(object? source)
    {
        Visual? visual = source as Visual;
        return visual as TreeViewItem
            ?? visual?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
    }

    private void DeviceTree_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DeviceExplorer.SetSelectedNodes(
                DeviceTree.SelectedItems.OfType<DeviceExplorerNodeViewModel>().ToArray());
        }
    }

    private async void DeleteDevice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IReadOnlyList<Guid> deviceIds = viewModel.DeviceExplorer.GetSelectedDeviceIds();
        if (deviceIds.Count == 0)
        {
            return;
        }

        bool single = deviceIds.Count == 1;
        string heading = single ? "Delete saved device?" : $"Delete {deviceIds.Count} saved devices?";
        string message = single
            ? $"Delete \"{viewModel.DeviceExplorer.SelectedDevice?.Name ?? "the selected device"}\" and its recent-session metadata? Its credential profile will remain available."
            : $"Delete these {deviceIds.Count} devices and their recent-session metadata? Credential profiles will remain available.";
        bool confirmed = await new ConfirmationWindow(
            heading, message, single ? "Delete device" : "Delete devices").ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await viewModel.DeviceExplorer.DeleteDevicesAsync(deviceIds);
        }
        catch (Exception exception)
        {
            await viewModel.DeviceExplorer.RefreshAsync();
            await new ConnectionErrorWindow(
                "The selected devices could not all be deleted. Check the refreshed list before retrying.",
                exception.GetType().Name).ShowDialog(this);
        }
    }

    private async void ConnectSavedDevice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.DeviceExplorer.HasSelectedDevice)
        {
            await viewModel.OpenSelectedSavedDeviceAsync();
        }
    }

    private async void OpenSavedTerminal_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.DeviceExplorer.CanOpenSelectedTerminal)
            await viewModel.OpenSelectedSavedTerminalAsync();
    }

    private async void OpenSavedWeb_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.DeviceExplorer.CanOpenSelectedWeb)
            await viewModel.OpenSelectedWebInterfaceAsync();
    }

    private async void DeviceTree_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && viewModel.DeviceExplorer.HasSelectedDevice)
        {
            e.Handled = true;
            await viewModel.OpenSelectedSavedDeviceAsync();
        }
    }

    private async void DeviceTree_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && DataContext is MainWindowViewModel viewModel
            && viewModel.DeviceExplorer.HasSelectedDevice)
        {
            e.Handled = true;
            await viewModel.OpenSelectedSavedDeviceAsync();
        }
    }

    private async void Credentials_OnClick(object? sender, RoutedEventArgs e)
    {
        await OpenCredentialsAsync();
    }

    private async Task OpenCredentialsAsync()
    {
        if (_credentialProfileService is null
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await new CredentialProfilesWindow(_credentialProfileService).ShowDialog(this);
        await viewModel.DeviceExplorer.RefreshAsync();
    }

    private async Task<bool> CreateCredentialForDeviceAsync(MainWindowViewModel viewModel)
    {
        if (_credentialProfileService is null)
        {
            await new ConnectionErrorWindow(
                "Credential profiles are unavailable.",
                "The credential service was not initialized.").ShowDialog(this);
            return false;
        }

        var editor = new CredentialEditorWindow(null);
        CredentialEditorResult? result = await editor.ShowDialog<CredentialEditorResult?>(this);
        if (result is null)
        {
            return false;
        }

        try
        {
            await _credentialProfileService.SaveAsync(
                result.Request,
                result.Password,
                CancellationToken.None);
            await viewModel.DeviceExplorer.RefreshAsync();
            return viewModel.DeviceExplorer.Credentials.Count > 0;
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The credential profile could not be saved.",
                exception.Message).ShowDialog(this);
            return false;
        }
    }

    private async Task SaveDeviceAsync(
        MainWindowViewModel viewModel,
        DeviceEditRequest request)
    {
        try
        {
            await viewModel.DeviceExplorer.SaveAsync(request);
        }
        catch (Exception exception)
        {
            await new ConnectionErrorWindow(
                "The saved device could not be saved.",
                exception.Message).ShowDialog(this);
        }
    }

    private void SidebarResize_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle
            || DataContext is not MainWindowViewModel { IsSidebarCollapsed: false } viewModel)
        {
            return;
        }

        _isResizingSidebar = true;
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartWidth = viewModel.SidebarWidth;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void SidebarResize_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingSidebar || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var delta = e.GetPosition(this).X - _resizeStartPoint.X;
        viewModel.PreviewSidebarWidth(_resizeStartWidth + delta);
        e.Handled = true;
    }

    private async void SidebarResize_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingSidebar)
        {
            return;
        }

        _isResizingSidebar = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.CommitSidebarWidthAsync();
        }
    }
}
