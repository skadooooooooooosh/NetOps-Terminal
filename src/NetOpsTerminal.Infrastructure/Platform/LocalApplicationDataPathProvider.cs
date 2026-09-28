namespace NetOpsTerminal.Infrastructure.Platform;

public sealed class LocalApplicationDataPathProvider : IApplicationDataPathProvider
{
    private static readonly string ApplicationDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetOpsTerminal");

    public string SettingsFilePath { get; } = Path.Combine(ApplicationDataDirectory, "settings.json");

    public string KnownHostsFilePath { get; } = Path.Combine(
        ApplicationDataDirectory,
        "ssh",
        "known_hosts");

    public string SessionLogsDirectoryPath { get; } = Path.Combine(
        ApplicationDataDirectory,
        "session-logs");
}
