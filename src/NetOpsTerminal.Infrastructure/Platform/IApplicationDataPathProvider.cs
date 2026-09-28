namespace NetOpsTerminal.Infrastructure.Platform;

public interface IApplicationDataPathProvider
{
    string SettingsFilePath { get; }

    string KnownHostsFilePath { get; }

    string SessionLogsDirectoryPath => Path.Combine(
        Path.GetDirectoryName(SettingsFilePath) ?? string.Empty,
        "session-logs");
}
