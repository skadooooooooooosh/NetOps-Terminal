using RoyalTerminal.Avalonia.Controls;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal static class TerminalFontRedraw
{
    private const double FontSizeTolerance = 0.001;

    public static bool Apply(TerminalControl control, int fontSize)
    {
        ArgumentNullException.ThrowIfNull(control);

        return Apply(
            control.TerminalFontSize,
            fontSize,
            value => control.TerminalFontSize = value,
            control.InvalidateTextRendering,
            control.InvalidateTerminal);
    }

    internal static bool Apply(
        double currentFontSize,
        int fontSize,
        Action<double> setFontSize,
        Action invalidateTextRendering,
        Action invalidateTerminal)
    {
        ArgumentNullException.ThrowIfNull(setFontSize);
        ArgumentNullException.ThrowIfNull(invalidateTextRendering);
        ArgumentNullException.ThrowIfNull(invalidateTerminal);

        if (Math.Abs(currentFontSize - fontSize) < FontSizeTolerance)
        {
            return false;
        }

        setFontSize(fontSize);
        invalidateTextRendering();
        invalidateTerminal();
        return true;
    }
}
