using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Security.Ssh;
using System.Security.Cryptography;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NetOpsTerminal.Ui.Tests;

public static class VisualTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<NetOpsTerminal.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Real Avalonia views with memory-only services. No Windows automation,
/// sockets, shell processes, credential store, or operator device data.
/// NETOPS_UI_EVIDENCE_DIR optionally retains sanitized rendered PNGs.
/// </summary>
public sealed class GraphiteUiTests
{
    [Theory]
    [InlineData(1366, 768, 1.0)]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1366, 768, 1.25)]
    [InlineData(1920, 1080, 1.5)]
    public Task RenderShellAndSettings(int width, int height, double scale) => OnUiThread(async () =>
    {
        using var vm = MainWindowViewModelTests.CreateVisualTestViewModel();
        await vm.InitializeAsync();
        using var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        try
        {
            Render(window, $"welcome-{width}x{height}-{scale:0.##}", scale);
            await vm.ToggleSidebarCommand.ExecuteAsync(null);
            Render(window, $"explorer-{width}x{height}-{scale:0.##}", scale);
            vm.ShowSettingsCommand.Execute(null);
            Render(window, $"settings-{width}x{height}-{scale:0.##}", scale);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task RenderQuickConnectProtocol(int protocol) => OnUiThread(() =>
    {
        var dialog = new QuickConnectWindow();
        dialog.Show();
        try
        {
            dialog.FindControl<ComboBox>("ProtocolComboBox")!.SelectedIndex = protocol;
            Render(dialog, $"quick-connect-{protocol}");
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Render(dialog, $"quick-connect-{protocol}-scrolled");
            Assert.True(dialog.IsVisible);
        }
        finally
        {
            dialog.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RenderCredentialAndDeviceDialogs() => OnUiThread(() =>
    {
        Window[] dialogs =
        [
            new DeviceEditorWindow(),
            new CredentialEditorWindow(),
            new CredentialProfilesWindow(),
            new ConfirmationWindow("Close test tabs?", "This is a view-only rendering check.", "Close tabs"),
            new DeviceExportWindow(true, "1 test device"),
            new DeviceImportReviewWindow(new DeviceImportPreview(1,
                new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero),
                [new DeviceImportPreviewItem("LAB-SWITCH-01", "switch.example.test", 22, "Test Site", "Test Organization", "", "", true, true)]), []),
            new DeviceTransferResultWindow("Preview complete", "No device data was imported."),
            new ExplorerNameWindow(true, "Test Site"),
            new SshHostKeyPromptWindow(new SshHostEndpoint("switch.example.test", 22), RenderingPublicKey(1), null, false),
            new SshHostKeyPromptWindow(new SshHostEndpoint("switch.example.test", 22), RenderingPublicKey(2), RenderingPublicKey(1), true),
            new TerminalPasteConfirmationWindow("Review test text", "No text will be sent during this rendering check.", "show version", "Paste"),
            new ConnectionErrorWindow("A test connection could not be opened.",
                "No device was contacted. This is sanitized rendering evidence."),
        ];
        foreach (Window dialog in dialogs)
        {
            dialog.Show();
            try { Render(dialog, dialog is SshHostKeyPromptWindow { DataContext: SshHostKeyPromptViewModel { IsChanged: true } } ? "SshHostKeyPromptWindow-changed" : dialog.GetType().Name); }
            finally { dialog.Close(); }
        }
        return Task.CompletedTask;
    });


    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public Task ExplorerOverlayKeepsWorkspaceSizeAndDismissal(int width, int height) => OnUiThread(async () =>
    {
        using var vm = MainWindowViewModelTests.CreateVisualTestViewModel();
        await vm.InitializeAsync();
        using var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        try
        {
            window.UpdateLayout();
            var workspace = window.FindControl<Grid>("WorkspaceHost")!;
            Rect originalBounds = workspace.Bounds;
            Assert.Equal(52, window.FindControl<Border>("ActivityRail")!.Bounds.Width);
            Assert.Equal(24, window.FindControl<Border>("StatusBar")!.Bounds.Height);
            Button[] railButtons = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("rail-icon")).ToArray();
            Assert.Equal(12, railButtons.Length);
            foreach (Button button in railButtons)
            {
                Assert.Equal(new Size(36, 36), button.Bounds.Size);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(button)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.NotNull(ToolTip.GetTip(button));
            }
            await vm.ToggleSidebarCommand.ExecuteAsync(null);
            window.UpdateLayout();
            Assert.True(window.FindControl<Grid>("DeviceExplorerOverlay")!.IsVisible);
            Assert.Equal(originalBounds, workspace.Bounds);

            var point = new Point(width - 30, 100);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(vm.IsSidebarCollapsed);

            await vm.ToggleSidebarCommand.ExecuteAsync(null);
            vm.ToggleDeviceExplorerPinCommand.Execute(null);
            window.UpdateLayout();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(vm.IsSidebarExpanded);
            Assert.True(vm.IsDeviceExplorerPinned);
            Assert.Equal(originalBounds, workspace.Bounds);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task QuickConnectRejectsEmptySshAndKeepsProtocolWarnings() => OnUiThread(() =>
    {
        var dialog = new QuickConnectWindow();
        dialog.Show();
        try
        {
            var connect = dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "QuickConnect.Connect");
            connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible);
            Assert.True(dialog.FindControl<TextBlock>("ValidationErrorText")!.IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(dialog.FindControl<TextBlock>("ValidationErrorText")!.Text));
            Assert.True(dialog.FindControl<TextBox>("SshPortTextBox")!.IsReadOnly);
            Assert.Equal("22", dialog.FindControl<TextBox>("SshPortTextBox")!.Text);
            dialog.FindControl<ComboBox>("ProtocolComboBox")!.SelectedIndex = 1;
            Assert.True(dialog.FindControl<StackPanel>("TelnetPanel")!.IsVisible);
            Assert.False(dialog.FindControl<CheckBox>("TelnetAcknowledgementCheckBox")!.IsChecked ?? false);
            Assert.Equal("23", dialog.FindControl<TextBox>("TelnetPortTextBox")!.Text);
            Assert.Equal(string.Empty, dialog.FindControl<TextBox>("SshPasswordTextBox")!.Text ?? string.Empty);
            Render(dialog, "quick-connect-validation");
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HelpFlyoutOpensAndRetainsShortcutContent() => OnUiThread(async () =>
    {
        using var vm = MainWindowViewModelTests.CreateVisualTestViewModel();
        await vm.InitializeAsync();
        using var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            var button = window.FindControl<Button>("KeyboardShortcutsButton")!;
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            string[] labels = content.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty).ToArray();
            Assert.Contains("Quick Connect", labels);
            Assert.Contains("Lock / unlock input", labels);
            Assert.Contains("Disconnect", labels);
            Assert.Contains("F6", labels);
            string? output = Environment.GetEnvironmentVariable("NETOPS_UI_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
            {
                using var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)Math.Ceiling(content.Bounds.Width), (int)Math.Ceiling(content.Bounds.Height)));
                bitmap.Render(content);
                bitmap.Save(Path.Combine(output, "help.png"), PngBitmapEncoderOptions.Default);
            }
            flyout.Hide();
            Assert.False(flyout.IsOpen);
        }
        finally { window.Close(); }
    });


    [Fact]
    public Task PrimaryActionTextMeetsContrastTarget() => OnUiThread(() =>
    {
        var dialog = new QuickConnectWindow();
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var button = dialog.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Classes.Contains("accent"));
            var label = button.GetVisualDescendants().OfType<TextBlock>().First();
            Color foreground = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
            Color background = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
            double ratio = (Math.Max(Luminance(foreground), Luminance(background)) + 0.05)
                / (Math.Min(Luminance(foreground), Luminance(background)) + 0.05);
            Assert.True(ratio >= 4.5, $"Primary action text contrast is {ratio:0.00}:1.");
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    private static double Luminance(Color color)
    {
        static double Linear(byte component)
        {
            double value = component / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }


    [Fact]
    public Task PendingAndFailedTabChromeKeepsTerminalOnlyBadgesHidden() => OnUiThread(async () =>
    {
        using var vm = MainWindowViewModelTests.CreateVisualTestViewModel();
        await vm.InitializeAsync();
        var pending = new TerminalPaneViewModel(Guid.NewGuid());
        pending.BeginConnection(new WorkspaceLaunchReference
        {
            Kind = WorkspaceLaunchKind.TransientConnection,
            DisplayName = "Mock session - rendering only",
        }, "Simulated connecting state. No endpoint is contacted.");
        var tab = new WorkspaceTabViewModel(Guid.NewGuid(), pending, pending);
        vm.WorkspaceTabs.Add(tab);
        vm.SelectedWorkspace = tab;
        using var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            Render(window, "mock-connecting-tabs");
            Assert.All(window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("tab-close")),
                button => Assert.Equal(24, button.Bounds.Height));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                label => label.IsEffectivelyVisible
                    && (label.Text?.Contains("INPUT LOCKED", StringComparison.Ordinal) == true
                        || label.Text?.Contains("LOGGING", StringComparison.Ordinal) == true));
            pending.EndConnection("Simulated failure for visual QA. No device was contacted.");
            Render(window, "mock-failed-tabs");
            Assert.True(vm.IsFooterError);
        }
        finally { window.Close(); }
    });


    private static SshHostKey RenderingPublicKey(byte marker)
    {
        byte[] bytes = Enumerable.Repeat(marker, 32).ToArray();
        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('=');
        return new SshHostKey("ssh-ed25519", fingerprint, Convert.ToBase64String(bytes), 256);
    }

    private static async Task OnUiThread(Func<Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(VisualTestApplication));
        await session.Dispatch(async () => { await test(); return true; }, CancellationToken.None);
    }

    private static void Render(Window window, string name, double scale = 1)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ClientSize.Width > 0 && window.ClientSize.Height > 0);
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(window.ClientSize.Width * scale),
                (int)Math.Ceiling(window.ClientSize.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        string? output = Environment.GetEnvironmentVariable("NETOPS_UI_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
        }
    }
}
