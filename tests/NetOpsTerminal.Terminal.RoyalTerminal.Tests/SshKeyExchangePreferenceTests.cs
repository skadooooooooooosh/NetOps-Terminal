using Renci.SshNet;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class SshKeyExchangePreferenceTests
{
    [Fact]
    public void PrefersGroup14Sha256WithoutRemovingOtherAlgorithms()
    {
        using var authentication = new PasswordAuthenticationMethod("operator", "test-only");
        var connectionInfo = new ConnectionInfo("example.invalid", "operator", authentication);
        string[] before = connectionInfo.KeyExchangeAlgorithms.Keys.ToArray();

        SshKeyExchangePreference.Apply(connectionInfo);

        string[] after = connectionInfo.KeyExchangeAlgorithms.Keys.ToArray();
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));
        Assert.True(Array.IndexOf(after, SshKeyExchangePreference.PreferredAlgorithm)
            < Array.IndexOf(after, "diffie-hellman-group16-sha512"));
        Assert.True(Array.IndexOf(after, "ecdh-sha2-nistp256")
            < Array.IndexOf(after, SshKeyExchangePreference.PreferredAlgorithm));
    }

    [Fact]
    public void ApplyingPreferenceTwiceDoesNotChangeOrder()
    {
        using var authentication = new PasswordAuthenticationMethod("operator", "test-only");
        var connectionInfo = new ConnectionInfo("example.invalid", "operator", authentication);
        SshKeyExchangePreference.Apply(connectionInfo);
        string[] once = connectionInfo.KeyExchangeAlgorithms.Keys.ToArray();

        SshKeyExchangePreference.Apply(connectionInfo);

        Assert.Equal(once, connectionInfo.KeyExchangeAlgorithms.Keys);
    }
}
