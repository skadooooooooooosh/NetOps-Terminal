using NetOpsTerminal.Transports.Abstractions;

namespace NetOpsTerminal.Transports.Tests;

public sealed class NetworkEndpointTests
{
    [Fact]
    public void ConstructorTrimsValidHost()
    {
        var endpoint = new NetworkEndpoint("  switch.lab.example  ", 22);

        Assert.Equal("switch.lab.example", endpoint.Host);
        Assert.Equal(22, endpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsMissingHost(string host)
    {
        Assert.Throws<ArgumentException>(() => new NetworkEndpoint(host, 22));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void ConstructorRejectsInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkEndpoint("switch.lab.example", port));
    }
}
