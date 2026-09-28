namespace NetOpsTerminal.App.Input;

public static class TerminalZoomGesture
{
    public static int GetIncrement(bool controlPressed, double verticalDelta)
    {
        if (!controlPressed || verticalDelta == 0)
        {
            return 0;
        }

        return verticalDelta > 0 ? 1 : -1;
    }
}
