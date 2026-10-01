using Microsoft.Extensions.DependencyInjection;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Infrastructure.Credentials;
using NetOpsTerminal.Infrastructure.Persistence;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Infrastructure.Settings;
using NetOpsTerminal.Infrastructure.Ssh;
using NetOpsTerminal.Security.Credentials;
using NetOpsTerminal.Security.Ssh;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Infrastructure.Logging;

namespace NetOpsTerminal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNetOpsTerminalInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationDataPathProvider, LocalApplicationDataPathProvider>();
        services.AddSingleton<IDeviceWebAccessService, DeviceWebAccessService>();
        services.AddSingleton<IApplicationSettingsStore, JsonApplicationSettingsStore>();
        services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
        services.AddSingleton<IKnownHostsStore, OpenSshKnownHostsStore>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IApplicationDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IDeviceRepository, SqliteDeviceRepository>();
        services.AddSingleton<ISiteRepository, SqliteSiteRepository>();
        services.AddSingleton<IDeviceGroupRepository, SqliteDeviceGroupRepository>();
        services.AddSingleton<IExplorerOrderRepository, SqliteExplorerOrderRepository>();
        services.AddSingleton<ICredentialProfileRepository, SqliteCredentialProfileRepository>();
        services.AddSingleton<IRecentDeviceRepository, SqliteRecentDeviceRepository>();
        services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ISessionLogService, FileSessionLogService>();
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        return services;
    }
}
