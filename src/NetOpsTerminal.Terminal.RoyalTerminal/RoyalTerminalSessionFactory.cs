using Microsoft.Extensions.Logging;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Security.Ssh;
using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

public sealed class RoyalTerminalSessionFactory : ITerminalSessionFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IShellProfileCatalog _shellProfileCatalog;
    private readonly ISshHostKeyProbe _sshHostKeyProbe;
    private readonly ISshHostKeyTrustService _sshHostKeyTrustService;
    private readonly ITerminalPasteConfirmationService _pasteConfirmationService;

    public RoyalTerminalSessionFactory(
        ILoggerFactory loggerFactory,
        ISshHostKeyTrustService sshHostKeyTrustService,
        ISshHostKeyProbe sshHostKeyProbe,
        ITerminalPasteConfirmationService pasteConfirmationService)
        : this(
            loggerFactory,
            new DefaultShellProfileCatalog(),
            sshHostKeyTrustService,
            sshHostKeyProbe,
            pasteConfirmationService)
    {
    }

    internal RoyalTerminalSessionFactory(
        ILoggerFactory loggerFactory,
        IShellProfileCatalog shellProfileCatalog,
        ISshHostKeyTrustService sshHostKeyTrustService,
        ISshHostKeyProbe sshHostKeyProbe,
        ITerminalPasteConfirmationService pasteConfirmationService)
    {
        _loggerFactory = loggerFactory;
        _shellProfileCatalog = shellProfileCatalog;
        _sshHostKeyTrustService = sshHostKeyTrustService;
        _sshHostKeyProbe = sshHostKeyProbe;
        _pasteConfirmationService = pasteConfirmationService;
    }

    public Task<ITerminalSession> CreateAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SessionId == Guid.Empty)
        {
            throw new ArgumentException("A session ID is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ArgumentException("A display name is required.", nameof(request));
        }

        ITerminalSession session;
        if (request.ConnectionType == ConnectionType.LocalShell)
        {
            LocalShellKind shellKind = request.ShellKind
                ?? throw new ArgumentException("A local shell kind is required.", nameof(request));
            ShellProfile profile = ResolveProfile(shellKind);
            string workingDirectory = ResolveWorkingDirectory(request.WorkingDirectory);
            session = new RoyalTerminalSession(
                request.SessionId,
                request.DisplayName.Trim(),
                shellKind,
                profile,
                workingDirectory,
                _pasteConfirmationService,
                _loggerFactory.CreateLogger<RoyalTerminalSession>());
        }
        else if (request.ConnectionType == ConnectionType.Ssh)
        {
            SshConnectionOptions options = request.SshOptions
                ?? throw new ArgumentException("SSH connection options are required.", nameof(request));
            session = new RoyalSshTerminalSession(
                request.SessionId,
                request.DisplayName.Trim(),
                options,
                _sshHostKeyProbe,
                _sshHostKeyTrustService,
                _pasteConfirmationService,
                _loggerFactory.CreateLogger<RoyalSshTerminalSession>());
        }
        else if (request.ConnectionType == ConnectionType.Telnet)
        {
            TelnetConnectionOptions options = request.TelnetOptions
                ?? throw new ArgumentException("Telnet connection options are required.", nameof(request));
            session = new RoyalProtocolTerminalSession(
                request.SessionId,
                request.DisplayName.Trim(),
                options,
                _pasteConfirmationService,
                _loggerFactory.CreateLogger<RoyalProtocolTerminalSession>());
        }
        else if (request.ConnectionType == ConnectionType.Serial)
        {
            SerialConnectionOptions options = request.SerialOptions
                ?? throw new ArgumentException("Serial connection options are required.", nameof(request));
            session = new RoyalProtocolTerminalSession(
                request.SessionId,
                request.DisplayName.Trim(),
                options,
                _pasteConfirmationService,
                _loggerFactory.CreateLogger<RoyalProtocolTerminalSession>());
        }
        else if (request.ConnectionType == ConnectionType.RawTcp)
        {
            RawTcpConnectionOptions options = request.RawTcpOptions
                ?? throw new ArgumentException("Raw TCP connection options are required.", nameof(request));
            session = new RoyalProtocolTerminalSession(
                request.SessionId,
                request.DisplayName.Trim(),
                options,
                _pasteConfirmationService,
                _loggerFactory.CreateLogger<RoyalProtocolTerminalSession>());
        }
        else
        {
            request.SshOptions?.Dispose();
            throw new NotSupportedException($"Connection type {request.ConnectionType} is not supported.");
        }

        return Task.FromResult(session);
    }

    private ShellProfile ResolveProfile(LocalShellKind shellKind)
    {
        IReadOnlyList<ShellProfile> profiles = _shellProfileCatalog.GetProfiles();
        string[] preferredIds = shellKind switch
        {
            LocalShellKind.PowerShell => ["pwsh", "powershell"],
            LocalShellKind.CommandPrompt => ["cmd"],
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind, "Unsupported local shell."),
        };

        foreach (string id in preferredIds)
        {
            ShellProfile? profile = profiles.FirstOrDefault(
                candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (profile is not null)
            {
                return profile;
            }
        }

        throw new PlatformNotSupportedException($"{shellKind} is not available on this computer.");
    }

    private static string ResolveWorkingDirectory(string? requestedDirectory)
    {
        if (!string.IsNullOrWhiteSpace(requestedDirectory))
        {
            string fullPath = Path.GetFullPath(requestedDirectory);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException("The requested terminal working directory does not exist.");
            }

            return fullPath;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(userProfile) ? userProfile : Environment.CurrentDirectory;
    }
}
