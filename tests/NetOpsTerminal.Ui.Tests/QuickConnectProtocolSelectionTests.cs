using NetOpsTerminal.App.Services;

namespace NetOpsTerminal.Ui.Tests;

public sealed class QuickConnectProtocolSelectionTests
{
    [Theory]
    [InlineData(0, "Ssh", "Secure shell with verified host identity")]
    [InlineData(1, "Telnet", "Plaintext terminal access for trusted networks")]
    [InlineData(2, "Serial", "Direct console access through a local COM port")]
    [InlineData(3, "RawTcp", "Unencrypted text stream to an authorized endpoint")]
    public void SelectedIndexMapsToSupportedProtocolAndSummary(
        int selectedIndex,
        string expectedProtocol,
        string expectedSummary)
    {
        QuickConnectProtocol protocol =
            QuickConnectProtocolSelection.FromSelectedIndex(selectedIndex);

        Assert.Equal(expectedProtocol, protocol.ToString());
        Assert.Equal(expectedSummary, QuickConnectProtocolSelection.GetSummary(protocol));
    }

    [Fact]
    public void UnsupportedSelectedIndexIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuickConnectProtocolSelection.FromSelectedIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuickConnectProtocolSelection.FromSelectedIndex(4));
    }
}
