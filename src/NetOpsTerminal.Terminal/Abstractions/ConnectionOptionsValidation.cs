namespace NetOpsTerminal.Terminal.Abstractions;

internal static class ConnectionOptionsValidation
{
    public static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || host.Length > 253
            || host.Any(char.IsControl)
            || host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Enter a valid host name or IP address.", nameof(host));
        }

        return host.Trim().TrimEnd('.');
    }

    public static void ValidatePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);
    }

    public static void ValidateEncoding(TerminalTextEncoding encoding)
    {
        if (!Enum.IsDefined(encoding))
        {
            throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }

    public static void ValidateTimeout(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, 120);
    }
}
