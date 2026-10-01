using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Security.Credentials;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Tests;

public sealed class Phase4ApplicationServicesTests
{
    [Fact]
    public async Task CredentialUpdateKeepsReferenceAndUpdatesProtectedSecret()
    {
        var database = new RecordingDatabaseInitializer();
        var credentials = new InMemoryCredentialRepository();
        var devices = new InMemoryDeviceRepository();
        var secrets = new InMemoryCredentialStore();
        var service = new CredentialProfileService(
            database,
            credentials,
            devices,
            secrets,
            NullLogger<CredentialProfileService>.Instance);

        await service.SaveAsync(
            new CredentialProfileEditRequest(null, "Network Admin", "operator"),
            "first-test-secret",
            CancellationToken.None);
        CredentialProfile created = Assert.Single(await credentials.GetAllAsync(CancellationToken.None));
        await service.SaveAsync(
            new CredentialProfileEditRequest(created.Id, "Network Admin", "operator2"),
            "second-test-secret",
            CancellationToken.None);
        CredentialProfile updated = Assert.Single(await credentials.GetAllAsync(CancellationToken.None));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.SecretReference, updated.SecretReference);
        Assert.Equal("operator2", updated.Username);
        Assert.Equal(
            "second-test-secret",
            await secrets.GetSecretAsync(updated.SecretReference, CancellationToken.None));
        Assert.Equal(2, database.CallCount);
    }

    [Fact]
    public async Task CredentialDeleteIsBlockedWhileDeviceReferencesIt()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var credential = new CredentialProfile(
            Guid.NewGuid(),
            "Shared",
            "operator",
            "credential-shared",
            now,
            now);
        var credentials = new InMemoryCredentialRepository(credential);
        var devices = new InMemoryDeviceRepository(
            CreateDevice("Device A", "host-a.test", credential.Id),
            CreateDevice("Device B", "host-b.test", credential.Id));
        var secrets = new InMemoryCredentialStore();
        await secrets.SetSecretAsync(credential.SecretReference, "test-secret", CancellationToken.None);
        var service = new CredentialProfileService(
            new RecordingDatabaseInitializer(),
            credentials,
            devices,
            secrets,
            NullLogger<CredentialProfileService>.Instance);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteAsync(credential.Id, CancellationToken.None));

        Assert.Contains("2 saved device", error.Message, StringComparison.Ordinal);
        Assert.NotNull(await credentials.GetAsync(credential.Id, CancellationToken.None));
        Assert.NotNull(await secrets.GetSecretAsync(credential.SecretReference, CancellationToken.None));
    }

    [Fact]
    public async Task SavedDeviceConnectionResolvesCredentialAndRecordsRecent()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var credential = new CredentialProfile(
            Guid.NewGuid(),
            "Shared",
            "operator",
            "credential-shared",
            now,
            now);
        DeviceProfile device = CreateDevice("Core Switch", "switch.test", credential.Id);
        var credentials = new InMemoryCredentialRepository(credential);
        var devices = new InMemoryDeviceRepository(device);
        var recents = new InMemoryRecentDeviceRepository();
        var secrets = new InMemoryCredentialStore();
        await secrets.SetSecretAsync(credential.SecretReference, "test-secret", CancellationToken.None);
        await using var sessions = new RecordingSessionManager();
        var service = new SavedDeviceSessionService(
            new RecordingDatabaseInitializer(),
            devices,
            credentials,
            recents,
            secrets,
            sessions,
            NullLogger<SavedDeviceSessionService>.Instance);

        SavedDeviceSessionResult result = await service.OpenAsync(
            device.Id,
            CancellationToken.None);

        Assert.Equal(device.Id, result.Device.Id);
        Assert.Equal("switch.test", sessions.LastRequest!.SshOptions!.Host);
        Assert.Equal("operator", sessions.LastRequest.SshOptions.Username);
        Assert.Equal("test-secret", sessions.LastRequest.SshOptions.Password.Reveal());
        Assert.Equal(device.Id, Assert.Single(recents.RecentIds));
        Assert.Equal(SessionState.Connected, result.Session.State);
    }

    [Theory]
    [InlineData(ConnectionType.Telnet, "legacy.test", 23)]
    [InlineData(ConnectionType.Serial, "COM3", 115200)]
    [InlineData(ConnectionType.RawTcp, "console.test", 2001)]
    public async Task SavedNonSshDeviceOpensWithoutCredentialLookup(
        ConnectionType type, string host, int port)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var device = new DeviceProfile(Guid.NewGuid(), type.ToString(), string.Empty,
            new SavedDeviceConnectionSettings(type, host, port,
                telnetAcknowledged: type == ConnectionType.Telnet),
            DeviceType.Other, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Lab, null, null, false, string.Empty, now, now, null);
        var recents = new InMemoryRecentDeviceRepository();
        await using var sessions = new RecordingSessionManager();
        var service = new SavedDeviceSessionService(
            new RecordingDatabaseInitializer(),
            new InMemoryDeviceRepository(device),
            new InMemoryCredentialRepository(),
            recents,
            new InMemoryCredentialStore(),
            sessions,
            NullLogger<SavedDeviceSessionService>.Instance);

        SavedDeviceSessionResult result = await service.OpenAsync(device.Id, CancellationToken.None);

        Assert.Equal(type, sessions.LastRequest!.ConnectionType);
        Assert.Equal(device.Id, Assert.Single(recents.RecentIds));
        Assert.Equal(SessionState.Connected, result.Session.State);
    }

    [Fact]
    public async Task WebOnlyDeviceCannotOpenTerminalSession()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var device = new DeviceProfile(Guid.NewGuid(), "GUI switch", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Ssh, "gui.test", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.WebOnly,
            new DeviceWebAccessSettings("https://gui.test/",
                WebBrowserPreference.WindowsDefault, DeviceOpenAction.Web));
        await using var sessions = new RecordingSessionManager();
        var service = new SavedDeviceSessionService(
            new RecordingDatabaseInitializer(), new InMemoryDeviceRepository(device),
            new InMemoryCredentialRepository(), new InMemoryRecentDeviceRepository(),
            new InMemoryCredentialStore(), sessions,
            NullLogger<SavedDeviceSessionService>.Instance);

        SavedDeviceConnectionException error = await Assert.ThrowsAsync<SavedDeviceConnectionException>(
            () => service.OpenAsync(device.Id, CancellationToken.None));

        Assert.Contains("web browser", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sessions.LastRequest);
    }
    [Fact]
    public async Task RecentMetadataFailureDoesNotHideConnectedSession()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var credential = new CredentialProfile(
            Guid.NewGuid(),
            "Shared",
            "operator",
            "credential-shared",
            now,
            now);
        DeviceProfile device = CreateDevice("Core Switch", "switch.test", credential.Id);
        var credentials = new InMemoryCredentialRepository(credential);
        var devices = new InMemoryDeviceRepository(device);
        var recents = new InMemoryRecentDeviceRepository { ThrowOnRecord = true };
        var secrets = new InMemoryCredentialStore();
        await secrets.SetSecretAsync(credential.SecretReference, "test-secret", CancellationToken.None);
        await using var sessions = new RecordingSessionManager();
        var service = new SavedDeviceSessionService(
            new RecordingDatabaseInitializer(),
            devices,
            credentials,
            recents,
            secrets,
            sessions,
            NullLogger<SavedDeviceSessionService>.Instance);

        SavedDeviceSessionResult result = await service.OpenAsync(
            device.Id,
            CancellationToken.None);

        Assert.Equal(SessionState.Connected, result.Session.State);
        Assert.Same(result.Session, Assert.Single(sessions.Sessions));
    }

    [Fact]
    public async Task CatalogCreatesHierarchyAndFiltersFavorites()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var credential = new CredentialProfile(
            Guid.NewGuid(),
            "Shared",
            "operator",
            "credential-shared",
            now,
            now);
        var credentials = new InMemoryCredentialRepository(credential);
        var devices = new InMemoryDeviceRepository();
        var sites = new InMemorySiteRepository();
        var groups = new InMemoryDeviceGroupRepository();
        var recents = new InMemoryRecentDeviceRepository();
        using var service = new DeviceCatalogService(
            new RecordingDatabaseInitializer(),
            devices,
            sites,
            groups,
            new InMemoryExplorerOrderRepository(),
            credentials,
            recents,
            NullLogger<DeviceCatalogService>.Instance);
        await service.SaveAsync(
            new DeviceEditRequest(
                null,
                "Core Switch",
                string.Empty,
                "switch.test",
                22,
                DeviceType.Switch,
                "Example Vendor",
                "X1",
                "Singapore",
                "Core",
                DeviceEnvironment.Production,
                credential.Id,
                null,
                true,
                string.Empty),
            CancellationToken.None);

        DeviceCatalogSnapshot favorite = await service.LoadAsync(
            "example vendor",
            DeviceCatalogFilter.Favorites,
            CancellationToken.None);

        DeviceProfile loaded = Assert.Single(favorite.Devices);
        Assert.True(loaded.IsFavorite);
        Assert.Single(favorite.Sites);
        Assert.Single(favorite.Groups);
        Assert.Equal(favorite.Sites[0].Id, loaded.SiteId);
        Assert.Equal(favorite.Groups[0].Id, loaded.GroupId);
        Assert.Equal(1, favorite.AllDeviceCount);
        Assert.Equal(1, favorite.FavoriteDeviceCount);
        Assert.Equal(0, favorite.RecentDeviceCount);
    }

    [Theory]
    [InlineData(ConnectionType.Telnet, "legacy.test", 23)]
    [InlineData(ConnectionType.Serial, "COM3", 115200)]
    [InlineData(ConnectionType.RawTcp, "console.test", 2001)]
    public async Task CatalogSavesNonSshWithoutCredential(
        ConnectionType type, string host, int port)
    {
        var devices = new InMemoryDeviceRepository();
        using var service = new DeviceCatalogService(
            new RecordingDatabaseInitializer(),
            devices,
            new InMemorySiteRepository(),
            new InMemoryDeviceGroupRepository(),
            new InMemoryExplorerOrderRepository(),
            new InMemoryCredentialRepository(),
            new InMemoryRecentDeviceRepository(),
            NullLogger<DeviceCatalogService>.Instance);
        var request = new DeviceEditRequest(
            null, type.ToString(), string.Empty, host, port, DeviceType.Other,
            string.Empty, string.Empty, string.Empty, string.Empty,
            DeviceEnvironment.Lab, null, null, false, string.Empty)
        {
            ConnectionType = type,
            TelnetAcknowledged = type == ConnectionType.Telnet,
        };

        await service.SaveAsync(request, CancellationToken.None);

        DeviceProfile saved = Assert.Single(await devices.GetAllAsync(CancellationToken.None));
        Assert.Equal(type, saved.ConnectionType);
        Assert.Null(saved.CredentialProfileId);
    }

    [Fact]
    public async Task CatalogSavesGuiOnlyWithoutCredentialAndIgnoresHiddenCliFields()
    {
        var devices = new InMemoryDeviceRepository();
        using var service = new DeviceCatalogService(
            new RecordingDatabaseInitializer(),
            devices,
            new InMemorySiteRepository(),
            new InMemoryDeviceGroupRepository(),
            new InMemoryExplorerOrderRepository(),
            new InMemoryCredentialRepository(),
            new InMemoryRecentDeviceRepository(),
            NullLogger<DeviceCatalogService>.Instance);
        var web = new DeviceWebAccessSettings("https://gui.test/manage",
            WebBrowserPreference.WindowsDefault, DeviceOpenAction.Web);
        var request = new DeviceEditRequest(null, "GUI switch", string.Empty,
            string.Empty, 23, DeviceType.Switch, string.Empty, string.Empty,
            string.Empty, string.Empty, DeviceEnvironment.Unknown,
            null, null, false, string.Empty)
        {
            ConnectionType = ConnectionType.Ssh,
            AccessMode = DeviceAccessMode.WebOnly,
            WebAccess = web,
        };

        await service.SaveAsync(request, CancellationToken.None);

        DeviceProfile saved = Assert.Single(await devices.GetAllAsync(CancellationToken.None));
        Assert.Equal(DeviceAccessMode.WebOnly, saved.AccessMode);
        Assert.Equal("gui.test", saved.Connection.Host);
        Assert.Equal(22, saved.Connection.Port);
        Assert.Null(saved.CredentialProfileId);
    }
    [Fact]
    public async Task CatalogRejectsUnacknowledgedTelnet()
    {
        using var service = new DeviceCatalogService(
            new RecordingDatabaseInitializer(),
            new InMemoryDeviceRepository(),
            new InMemorySiteRepository(),
            new InMemoryDeviceGroupRepository(),
            new InMemoryExplorerOrderRepository(),
            new InMemoryCredentialRepository(),
            new InMemoryRecentDeviceRepository(),
            NullLogger<DeviceCatalogService>.Instance);
        var request = new DeviceEditRequest(
            null, "Legacy", string.Empty, "legacy.test", 23, DeviceType.Other,
            string.Empty, string.Empty, string.Empty, string.Empty,
            DeviceEnvironment.Lab, null, null, false, string.Empty)
        {
            ConnectionType = ConnectionType.Telnet,
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(request, CancellationToken.None));
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
            string.Empty,
            string.Empty,
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

    private sealed class RecordingDatabaseInitializer : IApplicationDatabaseInitializer
    {
        public int CallCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = [];

        public Task<string?> GetSecretAsync(string secretId, CancellationToken cancellationToken)
        {
            _secrets.TryGetValue(secretId, out string? value);
            return Task.FromResult(value);
        }

        public Task SetSecretAsync(string secretId, string value, CancellationToken cancellationToken)
        {
            _secrets[secretId] = value;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken)
        {
            _secrets.Remove(secretId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCredentialRepository(params CredentialProfile[] profiles)
        : ICredentialProfileRepository
    {
        private readonly Dictionary<Guid, CredentialProfile> _profiles =
            profiles.ToDictionary(profile => profile.Id);

        public Task<IReadOnlyList<CredentialProfile>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CredentialProfile>>(_profiles.Values.ToArray());
        }

        public Task<CredentialProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            _profiles.TryGetValue(id, out CredentialProfile? profile);
            return Task.FromResult(profile);
        }

        public Task SaveAsync(CredentialProfile profile, CancellationToken cancellationToken)
        {
            _profiles[profile.Id] = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            _profiles.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryDeviceRepository(params DeviceProfile[] devices)
        : IDeviceRepository
    {
        private readonly Dictionary<Guid, DeviceProfile> _devices =
            devices.ToDictionary(device => device.Id);

        public Task<IReadOnlyList<DeviceProfile>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeviceProfile>>(_devices.Values.ToArray());
        }

        public Task<DeviceProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            _devices.TryGetValue(id, out DeviceProfile? device);
            return Task.FromResult(device);
        }

        public Task SaveAsync(DeviceProfile device, CancellationToken cancellationToken)
        {
            _devices[device.Id] = device;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            _devices.Remove(id);
            return Task.CompletedTask;
        }

        public Task<int> CountByCredentialAsync(
            Guid credentialProfileId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _devices.Values.Count(device => device.CredentialProfileId == credentialProfileId));
        }
    }

    private sealed class InMemorySiteRepository : ISiteRepository
    {
        private readonly List<Site> _sites = [];

        public Task<IReadOnlyList<Site>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Site>>(_sites.ToArray());
        }

        public Task<Site> GetOrCreateAsync(string name, CancellationToken cancellationToken)
        {
            Site? existing = _sites.FirstOrDefault(
                site => string.Equals(site.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var site = new Site(Guid.NewGuid(), name);
            _sites.Add(site);
            return Task.FromResult(site);
        }
        public Task DeleteEmptyAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

    }

    private sealed class InMemoryDeviceGroupRepository : IDeviceGroupRepository
    {
        private readonly List<DeviceGroup> _groups = [];

        public Task<IReadOnlyList<DeviceGroup>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeviceGroup>>(_groups.ToArray());
        }

        public Task<DeviceGroup> GetOrCreateChildAsync(
            string name, Guid? siteId, Guid parentGroupId, CancellationToken cancellationToken)
        {
            DeviceGroup? existing = _groups.FirstOrDefault(group =>
                group.SiteId == siteId && group.ParentGroupId == parentGroupId
                && string.Equals(group.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return Task.FromResult(existing);
            var group = new DeviceGroup(Guid.NewGuid(), name, siteId, parentGroupId);
            _groups.Add(group);
            return Task.FromResult(group);
        }

        public Task<DeviceGroup> GetOrCreateAsync(
            string name,
            Guid? siteId,
            CancellationToken cancellationToken)
        {
            DeviceGroup? existing = _groups.FirstOrDefault(
                group => group.SiteId == siteId
                    && string.Equals(group.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var group = new DeviceGroup(Guid.NewGuid(), name, siteId);
            _groups.Add(group);
            return Task.FromResult(group);
        }
        public Task DeleteEmptyAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

    }

    private sealed class InMemoryExplorerOrderRepository : IExplorerOrderRepository
    {
        public Task<IReadOnlyDictionary<Guid, int>> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

        public Task SaveAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ClearAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class InMemoryRecentDeviceRepository : IRecentDeviceRepository
    {
        public List<Guid> RecentIds { get; } = [];

        public bool ThrowOnRecord { get; init; }

        public Task<IReadOnlyList<Guid>> GetRecentDeviceIdsAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(
                RecentIds.Take(maximumCount).ToArray());
        }

        public Task RecordConnectionAsync(
            Guid deviceId,
            DateTimeOffset connectedAtUtc,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRecord)
            {
                throw new IOException("Test-only recent metadata failure.");
            }

            RecentIds.Remove(deviceId);
            RecentIds.Insert(0, deviceId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSessionManager : ISessionManager
    {
        private readonly List<ITerminalSession> _sessions = [];

        public TerminalSessionRequest? LastRequest { get; private set; }

        public IReadOnlyCollection<ITerminalSession> Sessions => _sessions;

        public async Task<ITerminalSession> OpenAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var session = new RecordingTerminalSession(request);
            await session.ConnectAsync(cancellationToken);
            _sessions.Add(session);
            return session;
        }

        public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            ITerminalSession? session = _sessions.FirstOrDefault(item => item.Id == sessionId);
            if (session is null)
            {
                return;
            }

            _sessions.Remove(session);
            await session.DisposeAsync();
        }

        public async Task CloseAllAsync(CancellationToken cancellationToken)
        {
            foreach (ITerminalSession session in _sessions.ToArray())
            {
                await CloseAsync(session.Id, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseAllAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingTerminalSession(TerminalSessionRequest request)
        : ITerminalSession
    {
        public Guid Id => request.SessionId;

        public string DisplayName => request.DisplayName;

        public ConnectionType ConnectionType => request.ConnectionType;

        public string ConnectionLabel => "SSH";

        public LocalShellKind? ShellKind => null;

        public SessionState State { get; private set; } = SessionState.Created;

        public int FontSize { get; private set; } = TerminalFontSizePolicy.Default;

        public bool IsInputLocked { get; private set; }

        public event EventHandler<SessionState>? StateChanged;

        public event EventHandler<Exception>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            State = SessionState.Connected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            State = SessionState.Disconnected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task ReconnectAsync(CancellationToken cancellationToken)
        {
            return ConnectAsync(cancellationToken);
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void SetFontSize(int fontSize)
        {
            FontSize = TerminalFontSizePolicy.Normalize(fontSize);
        }

        public void SetInputLocked(bool isLocked)
        {
            IsInputLocked = isLocked;
        }

        public ValueTask DisposeAsync()
        {
            request.SshOptions?.Dispose();
            State = SessionState.Disposed;
            StateChanged?.Invoke(this, State);
            return ValueTask.CompletedTask;
        }
    }
}
