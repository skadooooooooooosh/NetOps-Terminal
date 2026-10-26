using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.Terminal.Abstractions;
using NetOpsTerminal.Terminal.RoyalTerminal;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal.Services;

namespace NetOpsTerminal.Ui.Tests;

public sealed class TerminalClipboardUiTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public Task CopiesCleanSelectionWithoutChangingUpstreamSelectionRules(bool reverse, bool rectangle, bool spans) => OnUiThread(async () =>
    {
        var window = new Window { Content = new Control(), Width = 500, Height = 300 };
        window.Show();
        try
        {
            var owner = (Control)window.Content!;
            var service = new TerminalSessionService();
            var screen = new TerminalScreen(40, 4);
            using var renderer = new SkiaTerminalRenderer();
            string[] lines = ["show clock", "  description  Uplink", "", "exit"];
            for (int row = 0; row < lines.Length; row++)
            {
                for (int column = 0; column < screen.Columns; column++)
                {
                    ref TerminalCell cell = ref screen.GetRow(row)[column];
                    cell.Width = 1;
                    cell.Codepoint = column < lines[row].Length ? lines[row][column] : 0;
                }
            }
            screen.GetRow(0)[5].Grapheme = "\ud83d\ude00";
            screen.GetRow(0)[5].Width = 2;
            screen.GetRow(0)[6].Width = 0;
            renderer.SelectionStart = reverse ? (40, 3) : (0, 0);
            renderer.SelectionEnd = reverse ? (0, 0) : (40, 3);
            renderer.SelectionIsRectangle = rectangle;
            if (spans)
                renderer.SetSelectionSpans([new TerminalHighlightSpan(0, 0, 10, TerminalHighlightKind.Selection),
                    new TerminalHighlightSpan(1, 0, 25, TerminalHighlightKind.Selection), new TerminalHighlightSpan(2, 0, 0, TerminalHighlightKind.Selection),
                    new TerminalHighlightSpan(3, 0, 3, TerminalHighlightKind.Selection)]);

            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            await new DefaultTerminalSelectionService().CopySelectionAsync(owner, service, screen, renderer);
            string original = Assert.IsType<string>(await clipboard.TryGetTextAsync());
            await new CleanTerminalSelectionService().CopySelectionAsync(owner, service, screen, renderer);
            string actual = Assert.IsType<string>(await clipboard.TryGetTextAsync());

            Assert.Equal(TerminalCopyTextFormatter.TrimLineEnds(original), actual);
            Assert.Contains("\ud83d\ude00", actual);
            Assert.Contains("  description  Uplink", actual);
            Assert.Contains(Environment.NewLine + Environment.NewLine, actual);
            Assert.EndsWith("exit", actual);
            Assert.Equal(TerminalControlConfiguration.CountLines(original),
                TerminalControlConfiguration.CountLines(actual));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PasteKeepsExternalTextAndConfirmationUnchanged() => OnUiThread(async () =>
    {
        var window = new Window { Content = new Control() };
        window.Show();
        try
        {
            var owner = (Control)window.Content!;
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            const string text = "  command   \r\n\tsecond  \n";
            await clipboard.SetTextAsync(text);
            var service = new CleanTerminalSelectionService();
            var sent = new List<string>();
            var prompts = new List<string>();

            await service.PasteAsync(owner, sent.Add, new TerminalPasteRequest(
                BracketedPasteEnabled: false,
                SafetyPolicy: TerminalPasteSafetyPolicy.ConfirmUnsafe,
                UnsafePasteHandler: context =>
                {
                    prompts.Add(context.Text);
                    return ValueTask.FromResult(TerminalPasteSafetyDecision.Cancel);
                }));
            Assert.Empty(sent);
            Assert.Equal(text, Assert.Single(prompts));
            await service.PasteAsync(owner, sent.Add, new TerminalPasteRequest(
                BracketedPasteEnabled: false,
                SafetyPolicy: TerminalPasteSafetyPolicy.ConfirmUnsafe,
                UnsafePasteHandler: _ => ValueTask.FromResult(TerminalPasteSafetyDecision.Allow)));
            Assert.Equal(text, Assert.Single(sent));
            Assert.Equal(text, await clipboard.TryGetTextAsync());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CopyingOnlySpacesClearsPreviouslyCopiedCommands() => OnUiThread(async () =>
    {
        var window = new Window { Content = new Control() };
        window.Show();
        try
        {
            var screen = new TerminalScreen(3, 1);
            for (int column = 0; column < 3; column++)
                screen.GetRow(0)[column].Width = 1;
            using var renderer = new SkiaTerminalRenderer
            {
                SelectionStart = (0, 0),
                SelectionEnd = (3, 0),
            };
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            await clipboard.SetTextAsync("previous command");
            await new CleanTerminalSelectionService().CopySelectionAsync(
                (Control)window.Content!, new TerminalSessionService(), screen, renderer);
            Assert.True(string.IsNullOrEmpty(await clipboard.TryGetTextAsync()));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task CopyWithoutSelectionDoesNotTouchClipboard() => OnUiThread(async () =>
    {
        var window = new Window { Content = new Control() };
        window.Show();
        try
        {
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            const string external = "external text   ";
            await clipboard.SetTextAsync(external);
            await new CleanTerminalSelectionService().CopySelectionAsync(
                (Control)window.Content!, new TerminalSessionService(), null, null);
            Assert.Equal(external, await clipboard.TryGetTextAsync());
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PastePreviewOpensAtTopLeftWithoutChangingText(bool requestConstructor) => OnUiThread(() =>
    {
        string text = new string('A', 1000) + "   \r\n"
            + string.Join("\n", Enumerable.Repeat("  next line  ", 80));
        var window = requestConstructor
            ? new TerminalPasteConfirmationWindow(new TerminalPasteConfirmationRequest(text, 81, false))
            : new TerminalPasteConfirmationWindow("Paste preview", "Test only", text, "Paste");
        var preview = window.FindControl<TextBox>("PreviewTextBox")!;
        preview.CaretIndex = preview.Text!.Length;
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(text, preview.Text);
            Assert.Equal(0, preview.CaretIndex);
            Assert.Equal(0, preview.SelectionStart);
            Assert.Equal(0, preview.SelectionEnd);
            var scroll = Assert.Single(preview.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(Vector.Zero, scroll.Offset);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(820, 600, 280)]
    [InlineData(640, 480, 150)]
    public Task PasteDialogKeepsUsefulPreviewAndVisibleActions(int width, int height, int minimumPreviewHeight) => OnUiThread(() =>
    {
        string text = string.Join("\n", Enumerable.Range(1, 11).Select(line => $"  example line {line}"));
        var window = new TerminalPasteConfirmationWindow(new TerminalPasteConfirmationRequest(text, 11, false))
        {
            Width = width,
            Height = height,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var preview = window.FindControl<TextBox>("PreviewTextBox")!;
            Assert.True(preview.Bounds.Height >= minimumPreviewHeight,
                $"Preview height {preview.Bounds.Height} is below {minimumPreviewHeight}.");
            Assert.Equal(text, preview.Text);
            Assert.True(preview.IsReadOnly);
            string? evidence = Environment.GetEnvironmentVariable("NETOPS_UI_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                using var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height),
                    new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(evidence, $"paste-dialog-{width}x{height}.png"),
                    PngBitmapEncoderOptions.Default);
            }
            foreach (var button in window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is "Cancel" or "Paste"))
            {
                var origin = button.TranslatePoint(default, window)!.Value;
                Assert.True(origin.Y >= 0 && origin.Y + button.Bounds.Height <= window.ClientSize.Height);
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= window.ClientSize.Width);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
    private static async Task OnUiThread(Func<Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(VisualTestApplication));
        await session.Dispatch(async () => { await test(); return true; }, CancellationToken.None);
    }
}