using NetOpsTerminal.Infrastructure.Credentials;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public async Task RoundTripUsesWindowsCredentialManagerAndCleansUp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore();
        string secretId = $"qa-{Guid.NewGuid():N}";
        const string expected = "test-only-credential-Ω";
        try
        {
            await store.SetSecretAsync(secretId, expected, CancellationToken.None);

            Assert.Equal(
                expected,
                await store.GetSecretAsync(secretId, CancellationToken.None));
        }
        finally
        {
            await store.DeleteSecretAsync(secretId, CancellationToken.None);
        }

        Assert.Null(await store.GetSecretAsync(secretId, CancellationToken.None));
    }

    [Fact]
    public async Task MissingCredentialReturnsNull()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore();

        Assert.Null(
            await store.GetSecretAsync(
                $"missing-{Guid.NewGuid():N}",
                CancellationToken.None));
    }
}
