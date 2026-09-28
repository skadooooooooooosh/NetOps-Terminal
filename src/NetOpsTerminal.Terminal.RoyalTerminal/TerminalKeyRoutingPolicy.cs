using Avalonia.Input;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal static class TerminalKeyRoutingPolicy
{
    public static bool ShouldBlock(
        Key key,
        KeyModifiers modifiers,
        bool isInputLocked)
    {
        if (IsReservedApplicationShortcut(key, modifiers))
        {
            return true;
        }

        return isInputLocked && !IsLockedSessionSafeShortcut(key, modifiers);
    }

    private static bool IsReservedApplicationShortcut(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F6)
        {
            return modifiers is KeyModifiers.None or KeyModifiers.Shift;
        }

        if (modifiers == KeyModifiers.Alt)
        {
            return key is Key.R or Key.G or Key.L or Key.X
                or Key.Right or Key.Down or Key.P or Key.C;
        }

        if (modifiers == KeyModifiers.Control)
        {
            return key is Key.Tab or Key.W or Key.F or Key.N or Key.OemQuestion
                or Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus
                or Key.D0 or Key.NumPad0;
        }

        if (modifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            return key is Key.Tab or Key.Add or Key.OemPlus
                or Key.Subtract or Key.OemMinus or Key.D0 or Key.NumPad0;
        }

        return false;
    }

    private static bool IsLockedSessionSafeShortcut(Key key, KeyModifiers modifiers)
    {
        return key == Key.C
            && modifiers.HasFlag(KeyModifiers.Control)
            && modifiers.HasFlag(KeyModifiers.Shift);
    }
}
