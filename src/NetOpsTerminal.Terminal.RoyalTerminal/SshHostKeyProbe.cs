using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using NetOpsTerminal.Security.Ssh;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

public interface ISshHostKeyProbe
{
    Task<SshHostKey> ProbeAsync(
        SshConnectionOptions options,
        CancellationToken cancellationToken);
}

public sealed class SshHostKeyProbe : ISshHostKeyProbe
{
    public async Task<SshHostKey> ProbeAsync(
        SshConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] probePassword = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var authentication = new PasswordAuthenticationMethod(options.Username, probePassword);
            var connectionInfo = new ConnectionInfo(
                options.Host,
                options.Port,
                options.Username,
                authentication)
            {
                Timeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
            };
            SshKeyExchangePreference.Apply(connectionInfo);
            using var client = new SshClient(connectionInfo);
            SshHostKey? presentedKey = null;

            void CaptureHostKey(object? sender, HostKeyEventArgs eventArgs)
            {
                _ = sender;
                string fingerprint = $"SHA256:{Convert.ToBase64String(
                    SHA256.HashData(eventArgs.HostKey)).TrimEnd('=')}";
                presentedKey = new SshHostKey(
                    ReadKeyAlgorithm(eventArgs.HostKey),
                    fingerprint,
                    Convert.ToBase64String(eventArgs.HostKey),
                    eventArgs.KeyLength);
                eventArgs.CanTrust = false;
            }

            client.HostKeyReceived += CaptureHostKey;
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (presentedKey is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return presentedKey;
            }
            finally
            {
                client.HostKeyReceived -= CaptureHostKey;
            }

            return presentedKey
                ?? throw new SshConnectionException("The SSH server did not present a host key.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(probePassword);
        }
    }

    private static string ReadKeyAlgorithm(byte[] hostKey)
    {
        if (hostKey.Length < sizeof(uint))
        {
            throw new SshConnectionException("The SSH server presented malformed host-key data.");
        }

        int algorithmLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(hostKey));
        if (algorithmLength <= 0 || algorithmLength > hostKey.Length - sizeof(uint))
        {
            throw new SshConnectionException("The SSH server presented malformed host-key data.");
        }

        return Encoding.ASCII.GetString(hostKey, sizeof(uint), algorithmLength);
    }
}
