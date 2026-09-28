namespace NetOpsTerminal.Terminal.Abstractions;

public static class TerminalFontSizePolicy
{
    public const int Default = 14;
    public const int Minimum = 8;
    public const int Maximum = 32;

    public static int Normalize(int fontSize)
    {
        return Math.Clamp(fontSize, Minimum, Maximum);
    }
}
