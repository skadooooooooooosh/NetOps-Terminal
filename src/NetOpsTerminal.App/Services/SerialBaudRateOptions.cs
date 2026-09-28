namespace NetOpsTerminal.App.Services;

internal static class SerialBaudRateOptions
{
    public const int Default = 9600;

    public static IReadOnlyList<int> Common { get; } =
    [
        110,
        300,
        600,
        1200,
        2400,
        4800,
        9600,
        14400,
        19200,
        38400,
        57600,
        115200,
        230400,
        460800,
        921600,
    ];
}
