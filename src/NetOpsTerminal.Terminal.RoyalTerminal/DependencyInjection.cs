using Microsoft.Extensions.DependencyInjection;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

public static class DependencyInjection
{
    public static IServiceCollection AddRoyalTerminalSessions(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISshHostKeyProbe, SshHostKeyProbe>();
        services.AddSingleton<ISerialPortCatalog, SystemSerialPortCatalog>();
        services.AddSingleton<ITerminalSessionFactory, RoyalTerminalSessionFactory>();
        return services;
    }
}
