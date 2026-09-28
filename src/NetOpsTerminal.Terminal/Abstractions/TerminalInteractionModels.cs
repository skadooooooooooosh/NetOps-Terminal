namespace NetOpsTerminal.Terminal.Abstractions;

public sealed class TerminalOutputReceivedEventArgs(ReadOnlyMemory<byte> data) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}

public readonly record struct TerminalSearchResult(int TotalMatches, int SelectedMatchNumber)
{
    public static TerminalSearchResult Empty { get; } = new(0, 0);
}
