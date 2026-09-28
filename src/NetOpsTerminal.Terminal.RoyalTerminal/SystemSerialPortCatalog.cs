using System.IO.Ports;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed class SystemSerialPortCatalog : ISerialPortCatalog
{
    public IReadOnlyList<string> GetAvailablePortNames()
    {
        return SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ParsePortNumber)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ParsePortNumber(string name)
    {
        return name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out int portNumber)
                ? portNumber
                : int.MaxValue;
    }
}
