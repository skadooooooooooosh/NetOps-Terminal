using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.App.Services;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Infrastructure;
using NetOpsTerminal.Security.Ssh;
using NetOpsTerminal.Terminal.Abstractions;
using NetOpsTerminal.Terminal.RoyalTerminal;

namespace NetOpsTerminal.App.Bootstrap;

internal static class ServiceConfiguration
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffK";
                options.UseUtcTimestamp = true;
            });
            builder.AddDebug();
        });

        services.AddNetOpsTerminalInfrastructure();
        services.AddSingleton<ISshHostKeyPrompt, AvaloniaSshHostKeyPrompt>();
        services.AddSingleton<ISshHostKeyTrustService, SshHostKeyTrustService>();
        services.AddSingleton<ITerminalPasteConfirmationService, AvaloniaTerminalPasteConfirmationService>();
        services.AddRoyalTerminalSessions();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IDeviceCatalogService, DeviceCatalogService>();
        services.AddSingleton<IDeviceTransferService, DeviceTransferService>();
        services.AddSingleton<ICredentialProfileService, CredentialProfileService>();
        services.AddSingleton<ISavedDeviceSessionService, SavedDeviceSessionService>();
        services.AddSingleton<ISessionLogsFolderOpener, SessionLogsFolderOpener>();
        services.AddSingleton<DeviceExplorerViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
