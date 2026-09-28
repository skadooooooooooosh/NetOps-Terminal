namespace NetOpsTerminal.App.Services;

internal enum QuickConnectProtocol
{
    Ssh,
    Telnet,
    Serial,
    RawTcp,
}

internal static class QuickConnectProtocolSelection
{
    public static QuickConnectProtocol FromSelectedIndex(int selectedIndex)
    {
        return selectedIndex switch
        {
            0 => QuickConnectProtocol.Ssh,
            1 => QuickConnectProtocol.Telnet,
            2 => QuickConnectProtocol.Serial,
            3 => QuickConnectProtocol.RawTcp,
            _ => throw new ArgumentOutOfRangeException(
                nameof(selectedIndex),
                selectedIndex,
                "Select a supported connection protocol."),
        };
    }

    public static string GetSummary(QuickConnectProtocol protocol)
    {
        return protocol switch
        {
            QuickConnectProtocol.Ssh => "Secure shell with verified host identity",
            QuickConnectProtocol.Telnet => "Plaintext terminal access for trusted networks",
            QuickConnectProtocol.Serial => "Direct console access through a local COM port",
            QuickConnectProtocol.RawTcp => "Unencrypted text stream to an authorized endpoint",
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null),
        };
    }
}
