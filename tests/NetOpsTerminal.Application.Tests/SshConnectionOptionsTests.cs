using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Tests;

public sealed class SshConnectionOptionsTests
{
    [Fact]
    public void PasswordIsRedactedAndClearedWhenDisposed()
    {
        const string passwordValue = "test-only-password";
        using var password = EphemeralSecret.Create(passwordValue);
        using var options = new SshConnectionOptions(
            "server.test",
            22,
            "operator",
            password);
        var request = new TerminalSessionRequest(Guid.NewGuid(), "operator@server.test", options);

        string displayed = $"{password} {options} {request}";
        Assert.DoesNotContain(passwordValue, displayed, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", displayed, StringComparison.Ordinal);

        options.Dispose();

        Assert.True(password.IsDisposed);
        Assert.Throws<ObjectDisposedException>(password.Reveal);
    }

    [Theory]
    [InlineData("", 22, "operator")]
    [InlineData("invalid host", 22, "operator")]
    [InlineData("server.test", 0, "operator")]
    [InlineData("server.test", 65536, "operator")]
    [InlineData("server.test", 22, "")]
    public void InvalidEndpointOrUsernameIsRejected(string host, int port, string username)
    {
        using var password = EphemeralSecret.Create("test-only");

        Assert.ThrowsAny<ArgumentException>(
            () => new SshConnectionOptions(host, port, username, password));
    }
}
