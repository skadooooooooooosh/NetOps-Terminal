using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;

namespace NetOpsTerminal.Core.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceFrameworkOrInfrastructurePackages()
    {
        var forbiddenPrefixes = new[] { "Avalonia", "Microsoft.Data.Sqlite", "Renci.SshNet", "RoyalApps" };
        var references = typeof(ConnectionType).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => forbiddenPrefixes.Any(
                prefix => reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));
    }

    [Fact]
    public void SessionStateContainsThePlannedLifecycleStates()
    {
        var states = Enum.GetNames<SessionState>();

        Assert.Equal(
            [
                "Created",
                "Connecting",
                "Connected",
                "Disconnecting",
                "Disconnected",
                "Reconnecting",
                "Faulted",
                "Disposed",
            ],
            states);
    }
}
