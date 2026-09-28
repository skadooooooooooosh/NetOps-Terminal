using System.Diagnostics;
using NetOpsTerminal.Infrastructure.Platform;

namespace NetOpsTerminal.App.Services;

public interface ISessionLogsFolderOpener
{
    void Open();
}

public sealed class SessionLogsFolderOpener(IApplicationDataPathProvider paths)
    : ISessionLogsFolderOpener
{
    public void Open()
    {
        string folder = paths.SessionLogsDirectoryPath;
        Directory.CreateDirectory(folder);

        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }
}
