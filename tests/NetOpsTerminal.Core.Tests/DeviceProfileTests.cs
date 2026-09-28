using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Core.Tests;

public sealed class DeviceProfileTests
{
    [Fact]
    public void ConstructorNormalizesDeviceMetadata()
    {
        Guid credentialId = Guid.NewGuid();
        var device = CreateDevice(
            name: "  Core-SW-01  ",
            host: "switch.example.test.",
            credentialId: credentialId);

        Assert.Equal("Core-SW-01", device.Name);
        Assert.Equal("switch.example.test", device.Connection.Host);
        Assert.Equal(22, device.Connection.Port);
        Assert.Equal(credentialId, device.CredentialProfileId);
        Assert.DoesNotContain(
            device.GetType().GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad host")]
    [InlineData("bad\nhost")]
    public void ConnectionRejectsInvalidHost(string host)
    {
        Assert.Throws<ArgumentException>(() => new SshDeviceConnectionSettings(host));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public void ConnectionRejectsInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SshDeviceConnectionSettings("host.test", port));
    }

    [Fact]
    public void DeviceRequiresCredentialReference()
    {
        Assert.Throws<ArgumentException>(
            () => CreateDevice("switch", "host.test", Guid.Empty));
    }

    private static DeviceProfile CreateDevice(
        string name,
        string host,
        Guid credentialId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new DeviceProfile(
            Guid.NewGuid(),
            name,
            string.Empty,
            new SshDeviceConnectionSettings(host),
            DeviceType.Switch,
            "Vendor",
            "Model",
            null,
            null,
            DeviceEnvironment.Lab,
            credentialId,
            null,
            false,
            string.Empty,
            now,
            now,
            null);
    }
}
