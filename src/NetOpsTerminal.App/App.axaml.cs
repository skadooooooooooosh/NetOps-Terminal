using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.App.Bootstrap;
using NetOpsTerminal.App.Views;

namespace NetOpsTerminal.App;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ServiceConfiguration.Build();
            var logger = _services.GetRequiredService<ILogger<App>>();
            LogApplicationStarting(logger, "NetOps Terminal", 8);

            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            desktop.Exit += async (_, _) =>
            {
                LogApplicationStopped(logger);
                if (_services is { } services)
                {
                    _services = null;
                    await services.DisposeAsync();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Starting {ApplicationName} at phase {Phase}")]
    private static partial void LogApplicationStarting(
        ILogger logger,
        string applicationName,
        int phase);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "NetOps Terminal stopped")]
    private static partial void LogApplicationStopped(ILogger logger);
}
