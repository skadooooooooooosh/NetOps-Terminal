using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class TerminalControlConfigurationTests
{
    [Fact]
    public void ApplyConfiguresResizeAndPasteSafety()
    {
        var control = new TerminalControl
        {
            ReflowOnResize = true,
            PasteSafetyPolicy = TerminalPasteSafetyPolicy.None,
        };
        var confirmation = new RecordingPasteConfirmationService(true);

        TerminalControl configured = TerminalControlConfiguration.Apply(
            control,
            confirmation);

        Assert.Same(control, configured);
        Assert.False(configured.ReflowOnResize);
        Assert.Equal(
            TerminalPasteSafetyPolicy.ConfirmUnsafe,
            configured.PasteSafetyPolicy);
        Assert.NotNull(configured.UnsafePasteHandler);
    }

    [Theory]
    [InlineData("show version", 1)]
    [InlineData("show version\nshow clock", 2)]
    [InlineData("line one\r\nline two\rline three", 3)]
    [InlineData("line one\n", 2)]
    public void CountLinesHandlesTerminalLineEndings(string text, int expected)
    {
        Assert.Equal(expected, TerminalControlConfiguration.CountLines(text));
    }

    [Fact]
    public async Task UnsafeMultilinePasteUsesConfirmationService()
    {
        var confirmation = new RecordingPasteConfirmationService(true);
        TerminalControl control = TerminalControlConfiguration.Apply(
            new TerminalControl(),
            confirmation);
        const string text = "configure terminal\ninterface Gi1/0/1";

        TerminalPasteSafetyDecision decision = await control.UnsafePasteHandler!(
            new TerminalPasteContext(text, TerminalPasteRisk.Multiline));

        Assert.Equal(TerminalPasteSafetyDecision.Allow, decision);
        TerminalPasteConfirmationRequest request = Assert.Single(confirmation.Requests);
        Assert.Equal(text, request.Text);
        Assert.Equal(2, request.LineCount);
        Assert.False(request.ContainsControlSequences);
    }

    [Fact]
    public async Task RejectedUnsafePasteIsCancelled()
    {
        var confirmation = new RecordingPasteConfirmationService(false);
        TerminalControl control = TerminalControlConfiguration.Apply(
            new TerminalControl(),
            confirmation);

        TerminalPasteSafetyDecision decision = await control.UnsafePasteHandler!(
            new TerminalPasteContext("line one\nline two", TerminalPasteRisk.Multiline));

        Assert.Equal(TerminalPasteSafetyDecision.Cancel, decision);
    }

    [Fact]
    public async Task ConfirmationFailureCancelsPaste()
    {
        TerminalControl control = TerminalControlConfiguration.Apply(
            new TerminalControl(),
            new ThrowingPasteConfirmationService());

        TerminalPasteSafetyDecision decision = await control.UnsafePasteHandler!(
            new TerminalPasteContext("line one\nline two", TerminalPasteRisk.Multiline));

        Assert.Equal(TerminalPasteSafetyDecision.Cancel, decision);
    }

    [Fact]
    public async Task ControlSequenceRiskIsReportedToConfirmationService()
    {
        var confirmation = new RecordingPasteConfirmationService(true);
        TerminalControl control = TerminalControlConfiguration.Apply(
            new TerminalControl(),
            confirmation);

        _ = await control.UnsafePasteHandler!(
            new TerminalPasteContext("show clock\u001B", TerminalPasteRisk.ControlSequence));

        Assert.True(Assert.Single(confirmation.Requests).ContainsControlSequences);
    }

    private sealed class RecordingPasteConfirmationService(bool result)
        : ITerminalPasteConfirmationService
    {
        public List<TerminalPasteConfirmationRequest> Requests { get; } = [];

        public ValueTask<bool> ConfirmAsync(TerminalPasteConfirmationRequest request)
        {
            Requests.Add(request);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingPasteConfirmationService
        : ITerminalPasteConfirmationService
    {
        public ValueTask<bool> ConfirmAsync(TerminalPasteConfirmationRequest request)
        {
            return ValueTask.FromException<bool>(new InvalidOperationException("Unavailable"));
        }
    }
}
