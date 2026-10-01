using System.Diagnostics;
using System.Net.Sockets;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Application.Devices;

namespace NetOpsTerminal.Infrastructure.Platform;

public sealed class DeviceWebAccessService : IDeviceWebAccessService
{
    public void Open(DeviceWebAccessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ProcessStartInfo start = settings.Browser switch
        {
            WebBrowserPreference.WindowsDefault => new ProcessStartInfo
            {
                FileName = settings.Address,
                UseShellExecute = true,
            },
            WebBrowserPreference.MicrosoftEdge => ForBrowser("msedge.exe", settings.Address),
            WebBrowserPreference.GoogleChrome => ForBrowser("chrome.exe", settings.Address),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
        using Process? process = Process.Start(start);
        if (process is null)
            throw new InvalidOperationException("Windows could not open the selected browser.");
    }

    public async Task<bool> CheckPortAsync(DeviceWebAccessSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Uri address = new(settings.Address);
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            await client.ConnectAsync(address.Host, address.Port, timeout.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static ProcessStartInfo ForBrowser(string executable, string address)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
        };
        start.ArgumentList.Add(address);
        return start;
    }
}
