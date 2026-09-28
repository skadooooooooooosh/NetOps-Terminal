namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal static class TerminalFocusPolicy
{
    public static bool CanFocus(
        bool isAttachedToVisualTree,
        bool isEffectivelyVisible,
        bool isWindowActive,
        bool hasTerminalControl)
    {
        return isAttachedToVisualTree
            && isEffectivelyVisible
            && isWindowActive
            && hasTerminalControl;
    }
}
