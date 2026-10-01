using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Core.Tests;

public sealed class DeviceWebAccessTests
{
    [Theory]
    [InlineData("ftp://switch.test")]
    [InlineData("https://user:password@switch.test")]
    [InlineData("https://switch.test/?token=secret")]
    [InlineData("https://switch.test/#secret")]
    public void WebAddressRejectsUnsupportedOrSensitiveUrls(string address)
    {
        Assert.Throws<ArgumentException>(() => new DeviceWebAccessSettings(
            address, WebBrowserPreference.WindowsDefault, DeviceOpenAction.Web));
    }

    [Fact]
    public void WebOnlyDeviceDoesNotNeedTerminalCredentials()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var web = new DeviceWebAccessSettings("https://switch.test/manage",
            WebBrowserPreference.MicrosoftEdge, DeviceOpenAction.Web);
        var device = new DeviceProfile(Guid.NewGuid(), "Web switch", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Ssh, "switch.test", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.WebOnly, web);

        Assert.Equal(DeviceAccessMode.WebOnly, device.AccessMode);
        Assert.Null(device.CredentialProfileId);
        Assert.Equal("https://switch.test/manage", device.WebAccess?.Address);
    }

    [Fact]
    public void TerminalAndWebStillRequiresSshCredential()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var web = new DeviceWebAccessSettings("https://switch.test/",
            WebBrowserPreference.WindowsDefault, DeviceOpenAction.Terminal);

        Assert.Throws<ArgumentException>(() => new DeviceProfile(
            Guid.NewGuid(), "Both", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Ssh, "switch.test", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.TerminalAndWeb, web));
    }
}
