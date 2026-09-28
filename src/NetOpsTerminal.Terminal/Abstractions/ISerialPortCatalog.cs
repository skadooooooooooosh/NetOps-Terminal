namespace NetOpsTerminal.Terminal.Abstractions;

public interface ISerialPortCatalog
{
    IReadOnlyList<string> GetAvailablePortNames();
}
