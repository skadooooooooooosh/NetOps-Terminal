using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Security.Credentials;
using NetOpsTerminal.Terminal.Abstractions;
using Microsoft.Extensions.Logging;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class SavedDeviceSessionService(
    IApplicationDatabaseInitializer databaseInitializer,
    IDeviceRepository deviceRepository,
    ICredentialProfileRepository credentialRepository,
    IRecentDeviceRepository recentDeviceRepository,
    ICredentialStore credentialStore,
    ISessionManager sessionManager,
    ILogger<SavedDeviceSessionService> logger)
    : ISavedDeviceSessionService
{
    public async Task<SavedDeviceSessionResult> OpenAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        DeviceProfile device = await deviceRepository
            .GetAsync(deviceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SavedDeviceConnectionException("The saved device no longer exists.");
        TerminalSessionRequest request;
        SshConnectionOptions? sshOptions = null;
        EphemeralSecret? secret = null;
        try
        {
            SavedDeviceConnectionSettings connection = device.Connection;
            if (device.ConnectionType == ConnectionType.Ssh)
            {
                if (device.CredentialProfileId is not { } credentialId)
                    throw new SavedDeviceConnectionException("This SSH device has no credential profile.");
                CredentialProfile credential = await credentialRepository
                    .GetAsync(credentialId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new SavedDeviceConnectionException("The device's credential profile no longer exists.");
                string? password = await credentialStore
                    .GetSecretAsync(credential.SecretReference, cancellationToken)
                    .ConfigureAwait(false);
                if (password is null)
                    throw new SavedDeviceConnectionException(
                        "The saved password is unavailable. Open Credentials and update this credential profile.");
                secret = EphemeralSecret.Create(password);
                sshOptions = new SshConnectionOptions(connection.Host, connection.Port,
                    device.UsernameOverride ?? credential.Username, secret);
                secret = null;
                request = new TerminalSessionRequest(Guid.NewGuid(), device.Name, sshOptions);
            }
            else
            {
                if (device.ConnectionType == ConnectionType.Telnet && !connection.TelnetAcknowledged)
                    throw new SavedDeviceConnectionException(
                        "This Telnet device must be edited to acknowledge its plaintext connection before use.");
                var encoding = Enum.Parse<TerminalTextEncoding>(connection.Encoding);
                request = device.ConnectionType switch
                {
                    ConnectionType.Telnet => new TerminalSessionRequest(Guid.NewGuid(), device.Name,
                        new TelnetConnectionOptions(connection.Host, connection.Port,
                            connection.TerminalType, encoding, connection.ConnectTimeoutSeconds)),
                    ConnectionType.Serial => new TerminalSessionRequest(Guid.NewGuid(), device.Name,
                        new SerialConnectionOptions(connection.Host, connection.Port,
                            connection.SerialDataBits, Enum.Parse<SerialParity>(connection.SerialParity),
                            Enum.Parse<SerialStopBits>(connection.SerialStopBits),
                            Enum.Parse<SerialFlowControl>(connection.SerialFlowControl), encoding)),
                    ConnectionType.RawTcp => new TerminalSessionRequest(Guid.NewGuid(), device.Name,
                        new RawTcpConnectionOptions(connection.Host, connection.Port, encoding,
                            Enum.Parse<TerminalLineEnding>(connection.RawLineEnding),
                            connection.ConnectTimeoutSeconds)),
                    _ => throw new SavedDeviceConnectionException("Unsupported saved-device protocol."),
                };
            }
            ITerminalSession session = await sessionManager
                .OpenAsync(request, cancellationToken)
                .ConfigureAwait(false);
            sshOptions = null;
            DateTimeOffset connectedAt = DateTimeOffset.UtcNow;
            try
            {
                await recentDeviceRepository
                    .RecordConnectionAsync(device.Id, connectedAt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogRecentUpdateFailed(logger, device.Id, exception.GetType().Name);
            }

            return new SavedDeviceSessionResult(device, session);
        }
        finally
        {
            sshOptions?.Dispose();
            secret?.Dispose();
        }
    }

    [LoggerMessage(
        EventId = 251,
        Level = LogLevel.Warning,
        Message = "Saved device {DeviceId} connected, but recent-session metadata could not be updated; exception {ExceptionType}")]
    private static partial void LogRecentUpdateFailed(
        ILogger logger,
        Guid deviceId,
        string exceptionType);
}
