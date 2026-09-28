namespace NetOpsTerminal.Terminal.Abstractions;

public sealed record TerminalPasteConfirmationRequest(
    string Text,
    int LineCount,
    bool ContainsControlSequences);

public interface ITerminalPasteConfirmationService
{
    ValueTask<bool> ConfirmAsync(TerminalPasteConfirmationRequest request);
}
