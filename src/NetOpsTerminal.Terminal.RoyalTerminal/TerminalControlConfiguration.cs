using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal static class TerminalControlConfiguration
{
    public static TerminalControl Apply(
        TerminalControl control,
        ITerminalPasteConfirmationService pasteConfirmationService)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(pasteConfirmationService);

        control.ReflowOnResize = false;
        control.PasteSafetyPolicy = TerminalPasteSafetyPolicy.ConfirmUnsafe;
        control.UnsafePasteHandler = context => ConfirmUnsafePasteAsync(
            context,
            pasteConfirmationService);
        return control;
    }

    internal static int CountLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return 0;
        }

        int lines = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                lines++;
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (text[index] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static async ValueTask<TerminalPasteSafetyDecision> ConfirmUnsafePasteAsync(
        TerminalPasteContext context,
        ITerminalPasteConfirmationService pasteConfirmationService)
    {
        try
        {
            var request = new TerminalPasteConfirmationRequest(
                context.Text,
                CountLines(context.Text),
                context.Risk == TerminalPasteRisk.ControlSequence);
            bool confirmed = await pasteConfirmationService
                .ConfirmAsync(request)
                .ConfigureAwait(true);
            return confirmed
                ? TerminalPasteSafetyDecision.Allow
                : TerminalPasteSafetyDecision.Cancel;
        }
        catch
        {
            return TerminalPasteSafetyDecision.Cancel;
        }
    }
}
