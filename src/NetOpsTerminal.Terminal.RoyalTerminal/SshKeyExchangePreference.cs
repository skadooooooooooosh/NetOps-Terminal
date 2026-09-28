using Renci.SshNet;
using Renci.SshNet.Security;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal static class SshKeyExchangePreference
{
    internal const string PreferredAlgorithm = "diffie-hellman-group14-sha256";
    private const string SlowAlgorithm = "diffie-hellman-group16-sha512";

    internal static void Apply(ConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        var algorithms = connectionInfo.KeyExchangeAlgorithms;
        KeyValuePair<string, Func<IKeyExchange>>[] defaults = algorithms.ToArray();
        int preferredIndex = Array.FindIndex(defaults,
            pair => pair.Key == PreferredAlgorithm);
        int slowIndex = Array.FindIndex(defaults,
            pair => pair.Key == SlowAlgorithm);
        if (preferredIndex < 0 || slowIndex < 0 || preferredIndex < slowIndex)
        {
            return;
        }

        algorithms.Clear();
        for (int index = 0; index < defaults.Length; index++)
        {
            if (index == slowIndex)
            {
                algorithms.Add(defaults[preferredIndex].Key, defaults[preferredIndex].Value);
            }

            if (index != preferredIndex)
            {
                algorithms.Add(defaults[index].Key, defaults[index].Value);
            }
        }
    }
}
